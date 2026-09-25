using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace PluginHub.Services;

/// <summary>
/// Just enough IMAP (RFC 3501) to read a mailbox: log in, open a folder read-only (EXAMINE),
/// search by UID, fetch whole messages without marking them read (BODY.PEEK), log out. It never
/// stores, moves or deletes anything. Gmail wants TLS on port 993.
/// </summary>
internal sealed class ImapSession : IDisposable
{
    private static readonly Regex LiteralAtEnd = new(@"\{(\d+)\+?\}$", RegexOptions.Compiled);
    private const int MaxLine = 1_000_000;
    private const int MaxLiteral = 40_000_000;

    private readonly TcpClient _tcp;
    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[16384];
    private int _start, _end;
    private int _tag;

    private ImapSession(TcpClient tcp, Stream stream)
    {
        _tcp    = tcp;
        _stream = stream;
    }

    public static ImapSession Connect(string host, int port, bool tls, TimeSpan timeout)
    {
        var tcp = new TcpClient();
        try
        {
            if (!tcp.ConnectAsync(host, port).Wait(timeout))
                throw new ImapException($"{host} antwortet nicht.");
        }
        catch (AggregateException ex) when (ex.InnerException is SocketException)
        {
            tcp.Dispose();
            throw new ImapException($"{host} ist nicht erreichbar – besteht eine Internetverbindung?");
        }

        tcp.ReceiveTimeout = tcp.SendTimeout = (int)timeout.TotalMilliseconds;
        Stream stream = tcp.GetStream();
        try
        {
            if (tls)
            {
                var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
                ssl.AuthenticateAsClient(host);
                stream = ssl;
            }
        }
        catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException)
        {
            tcp.Dispose();
            throw new ImapException($"Die sichere Verbindung zu {host} kam nicht zustande.");
        }

        var session = new ImapSession(tcp, stream);
        var greeting = session.ReadResponse();
        if (!greeting.Text.StartsWith("* OK", StringComparison.OrdinalIgnoreCase))
        {
            session.Dispose();
            throw new ImapException("Der Mailserver hat die Verbindung abgelehnt.");
        }
        return session;
    }

    public void Login(string user, string password) => Run($"LOGIN {Quote(user)} {Quote(password)}");

    /// <summary>
    /// Opens a folder read-only. UIDVALIDITY says whether UIDs seen before still mean the same
    /// mails; UIDNEXT is the UID the next mail will get.
    /// </summary>
    public MailboxInfo Examine(string folder)
    {
        uint validity = 0, next = 0;
        foreach (var r in Run($"EXAMINE {Quote(folder)}"))
        {
            var v = Regex.Match(r.Text, @"\[UIDVALIDITY (\d+)\]", RegexOptions.IgnoreCase);
            if (v.Success) validity = uint.Parse(v.Groups[1].Value, CultureInfo.InvariantCulture);
            var n = Regex.Match(r.Text, @"\[UIDNEXT (\d+)\]", RegexOptions.IgnoreCase);
            if (n.Success) next = uint.Parse(n.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        return new MailboxInfo(validity, next);
    }

    public List<uint> Search(string criteria)
    {
        var list = new List<uint>();
        foreach (var r in Run($"UID SEARCH {criteria}"))
        {
            if (!r.Text.StartsWith("* SEARCH", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (string part in r.Text[8..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (uint.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out uint uid))
                    list.Add(uid);
        }
        return list;
    }

    /// <summary>Whole messages by UID with the time the server got them - left unread.</summary>
    public List<FetchedMessage> Fetch(IReadOnlyCollection<uint> uids)
    {
        var list = new List<FetchedMessage>();
        if (uids.Count == 0) return list;

        foreach (var r in Run($"UID FETCH {string.Join(",", uids)} (UID INTERNALDATE BODY.PEEK[])"))
        {
            if (r.Literals.Count == 0) continue;
            var uid  = Regex.Match(r.Text, @"\bUID (\d+)", RegexOptions.IgnoreCase);
            var date = Regex.Match(r.Text, "INTERNALDATE \"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (!uid.Success) continue;
            list.Add(new FetchedMessage(uint.Parse(uid.Groups[1].Value, CultureInfo.InvariantCulture),
                date.Success ? InternalDate(date.Groups[1].Value) : null, r.Literals[^1]));
        }
        return list;
    }

    public void Logout()
    {
        try { Run("LOGOUT"); }
        catch { /* leaving anyway */ }
    }

    public void Dispose()
    {
        _stream.Dispose();
        _tcp.Dispose();
    }

    /// <summary>One command; its untagged answers, or an ImapException when it did not end in OK.</summary>
    private List<ImapResponse> Run(string command)
    {
        string tag = "H" + (++_tag).ToString("D3", CultureInfo.InvariantCulture);
        _stream.Write(Encoding.UTF8.GetBytes($"{tag} {command}\r\n"));
        _stream.Flush();

        var untagged = new List<ImapResponse>();
        while (true)
        {
            var response = ReadResponse();
            if (response.Text.StartsWith(tag + " ", StringComparison.Ordinal))
            {
                string status = response.Text[(tag.Length + 1)..];
                if (!status.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                    throw new ImapException(status, untagged);
                return untagged;
            }
            untagged.Add(response);
        }
    }

    /// <summary>A response line - with the literals ({n} and n bytes) it carries taken out.</summary>
    private ImapResponse ReadResponse()
    {
        var text = new StringBuilder();
        var literals = new List<byte[]>();
        while (true)
        {
            string line = ReadLine();
            var m = LiteralAtEnd.Match(line);
            if (!m.Success)
            {
                text.Append(line);
                return new ImapResponse(text.ToString(), literals);
            }
            text.Append(line, 0, m.Index).Append("{literal}");
            int size = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (size > MaxLiteral) throw new ImapException("Eine Mail ist zu groß zum Lesen.");
            literals.Add(ReadBytes(size));
        }
    }

    private string ReadLine()
    {
        var bytes = new List<byte>(256);
        while (true)
        {
            if (_start == _end) Fill();
            byte b = _buffer[_start++];
            if (b == (byte)'\n')
            {
                if (bytes.Count > 0 && bytes[^1] == (byte)'\r') bytes.RemoveAt(bytes.Count - 1);
                return Encoding.Latin1.GetString(bytes.ToArray());
            }
            bytes.Add(b);
            if (bytes.Count > MaxLine) throw new ImapException("Der Mailserver schickt unlesbare Antworten.");
        }
    }

    private byte[] ReadBytes(int count)
    {
        var result = new byte[count];
        int got = 0;
        while (got < count)
        {
            if (_start == _end) Fill();
            int n = Math.Min(count - got, _end - _start);
            Buffer.BlockCopy(_buffer, _start, result, got, n);
            _start += n;
            got    += n;
        }
        return result;
    }

    private void Fill()
    {
        _start = 0;
        try { _end = _stream.Read(_buffer, 0, _buffer.Length); }
        catch (IOException) { throw new ImapException("Der Mailserver antwortet nicht mehr."); }
        if (_end <= 0) throw new ImapException("Der Mailserver hat die Verbindung beendet.");
    }

    private static string Quote(string text) =>
        "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>"25-Sep-2026 14:02:11 +0000"</summary>
    private static DateTimeOffset? InternalDate(string text)
    {
        string t = Regex.Replace(text.Trim(), @"([+-]\d{2})(\d{2})$", "$1:$2");
        return DateTimeOffset.TryParseExact(t, "d-MMM-yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : null;
    }
}

internal sealed record ImapResponse(string Text, List<byte[]> Literals);

internal sealed record MailboxInfo(uint Validity, uint UidNext);

internal sealed record FetchedMessage(uint Uid, DateTimeOffset? Arrived, byte[] Raw);

/// <summary>What the server said instead of OK - "NO [AUTHENTICATIONFAILED] …", with any alerts before it.</summary>
internal sealed class ImapException : Exception
{
    public ImapException(string message, List<ImapResponse>? untagged = null)
        : base(untagged is { Count: > 0 } ? message + " " + string.Join(" ", untagged.Select(u => u.Text)) : message)
    {
    }
}
