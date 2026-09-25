using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PluginHub.Services;

/// <summary>
/// Reads a mail as a server hands it out (RFC 5322 with MIME): sender, subject, date, message
/// id and the plain text - from its text/plain part, or from its HTML when that is all it has.
/// Enough for the requests the plug-ins write and for what mail programs make of them:
/// multipart, base64, quoted-printable, encoded words, other charsets.
/// </summary>
internal static class MailParser
{
    private static readonly Regex EncodedWord = new(@"=\?([^?]+)\?([BbQq])\?([^?]*)\?=", RegexOptions.Compiled);
    private static readonly Regex ReplySubject = new(@"^\s*(re|aw|antw|wg|fwd?|tr)\s*:", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static MailParser()
    {
        // windows-1252, iso-8859-15 and the like, which mail programs still send
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static IncomingMail Parse(byte[] raw, DateTimeOffset? arrived = null)
    {
        var (headers, body) = Split(raw);

        // Answers go to Reply-To when there is one
        var (fromName, fromEmail) = Address(Header(headers, "Reply-To") ?? Header(headers, "From") ?? "");
        if (fromName.Length == 0) fromName = Address(Header(headers, "From") ?? "").Name;

        string subject = Header(headers, "Subject") ?? "";
        string? messageId = Header(headers, "Message-ID")?.Trim();
        if (string.IsNullOrEmpty(messageId))
            messageId = "sha256:" + Convert.ToHexString(SHA256.HashData(raw))[..24].ToLowerInvariant();

        string? plain = null, html = null;
        Collect(headers, body, ref plain, ref html, 0);
        string text = plain ?? (html != null ? HtmlToText(html) : "");

        bool reply = ReplySubject.IsMatch(subject) || Header(headers, "In-Reply-To") != null;
        return new IncomingMail(messageId, fromName, fromEmail, subject,
            arrived ?? Date(Header(headers, "Date")) ?? DateTimeOffset.Now,
            text.Replace("\r\n", "\n").Trim(), reply);
    }

    // ── Header and body ──────────────────────────────────────────────────────

    private static (Dictionary<string, string> Headers, byte[] Body) Split(byte[] raw)
    {
        int end = IndexOf(raw, "\r\n\r\n"u8), skip = 4;
        if (end < 0) { end = IndexOf(raw, "\n\n"u8); skip = 2; }
        if (end < 0) { end = raw.Length; skip = 0; }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? name = null;
        var value = new StringBuilder();
        void Keep()
        {
            if (name != null && !headers.ContainsKey(name)) headers[name] = DecodeWords(value.ToString().Trim());
            name = null;
        }

        foreach (string line in HeaderText(raw.AsSpan(0, end)).Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                value.Append(' ').Append(line.Trim());   // a folded line goes on
                continue;
            }
            Keep();
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            name = line[..colon].Trim();
            value.Clear().Append(line[(colon + 1)..]);
        }
        Keep();

        int bodyStart = Math.Min(raw.Length, end + skip);
        return (headers, raw[bodyStart..]);
    }

    /// <summary>ASCII as the RFC says - UTF-8 where a newer mail program writes it straight in.</summary>
    private static string HeaderText(ReadOnlySpan<byte> bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    private static string? Header(Dictionary<string, string> headers, string name) =>
        headers.TryGetValue(name, out var v) ? v : null;

    /// <summary>Walks the parts; the first text/plain and the first text/html that are not attachments.</summary>
    private static void Collect(Dictionary<string, string> headers, byte[] body, ref string? plain, ref string? html, int depth)
    {
        var (type, pars) = ContentType(Header(headers, "Content-Type") ?? "text/plain");
        if ((Header(headers, "Content-Disposition") ?? "").TrimStart().StartsWith("attachment", StringComparison.OrdinalIgnoreCase))
            return;

        if (type.StartsWith("multipart/", StringComparison.Ordinal) && pars.TryGetValue("boundary", out var boundary) && depth < 6)
        {
            foreach (var part in Multipart(body, boundary))
            {
                var (h, b) = Split(part);
                Collect(h, b, ref plain, ref html, depth + 1);
            }
            return;
        }

        if (type == "text/plain" && plain == null) plain = Decode(body, headers, pars);
        else if (type == "text/html" && html == null) html = Decode(body, headers, pars);
    }

    private static List<byte[]> Multipart(byte[] body, string boundary)
    {
        // Latin-1 maps every byte to one char and back, so the parts come out byte for byte
        string s = Encoding.Latin1.GetString(body);
        string delimiter = "--" + boundary;
        var parts = new List<byte[]>();

        int at = s.StartsWith(delimiter, StringComparison.Ordinal) ? 0 : LineStart(s, delimiter, 0);
        while (at >= 0)
        {
            if (string.CompareOrdinal(s, at + delimiter.Length, "--", 0, 2) == 0) break;   // the closing one
            int lineEnd = s.IndexOf('\n', at);
            if (lineEnd < 0) break;
            int next = LineStart(s, delimiter, lineEnd + 1);
            int stop = next < 0 ? s.Length : next;
            string part = s[(lineEnd + 1)..stop];
            if (part.EndsWith("\n", StringComparison.Ordinal)) part = part[..^1];   // the line break before a delimiter is its own
            if (part.EndsWith("\r", StringComparison.Ordinal)) part = part[..^1];
            parts.Add(Encoding.Latin1.GetBytes(part));
            at = next;
        }
        return parts;
    }

    private static int LineStart(string s, string what, int from)
    {
        int i = s.IndexOf("\n" + what, from, StringComparison.Ordinal);
        return i < 0 ? -1 : i + 1;
    }

    private static string Decode(byte[] body, Dictionary<string, string> headers, Dictionary<string, string> pars)
    {
        string cte = (Header(headers, "Content-Transfer-Encoding") ?? "").Trim().ToLowerInvariant();
        byte[] bytes = cte switch
        {
            "base64"           => Base64(Encoding.Latin1.GetString(body)),
            "quoted-printable" => QuotedPrintable(Encoding.Latin1.GetString(body), underscoreIsSpace: false),
            _                  => body
        };
        return Charset(pars.GetValueOrDefault("charset")).GetString(bytes);
    }

    private static (string Type, Dictionary<string, string> Params) ContentType(string value)
    {
        var parts = value.Split(';');
        var pars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string p in parts.Skip(1))
        {
            int eq = p.IndexOf('=');
            if (eq > 0) pars[p[..eq].Trim()] = p[(eq + 1)..].Trim().Trim('"');
        }
        return (parts[0].Trim().ToLowerInvariant(), pars);
    }

    // ── Encodings ────────────────────────────────────────────────────────────

    /// <summary>"=?UTF-8?Q?Gr=C3=BC=C3=9Fe?=" and "=?utf-8?B?…?=" in headers; blanks between two of them drop.</summary>
    internal static string DecodeWords(string text)
    {
        text = Regex.Replace(text, @"(\?=)\s+(=\?)", "$1$2");
        return EncodedWord.Replace(text, m =>
        {
            var encoding = Charset(m.Groups[1].Value.Split('*')[0]);
            byte[] bytes = m.Groups[2].Value is "B" or "b"
                ? Base64(m.Groups[3].Value)
                : QuotedPrintable(m.Groups[3].Value, underscoreIsSpace: true);
            return encoding.GetString(bytes);
        });
    }

    private static byte[] Base64(string text)
    {
        string clean = new(text.Where(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '/').ToArray());
        clean = clean.PadRight(clean.Length + (4 - clean.Length % 4) % 4, '=');
        try { return Convert.FromBase64String(clean); }
        catch (FormatException) { return Array.Empty<byte>(); }
    }

    private static byte[] QuotedPrintable(string text, bool underscoreIsSpace)
    {
        var bytes = new List<byte>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '=')
            {
                // "=" at the end of a line only breaks the line
                if (i + 1 < text.Length && text[i + 1] == '\n') { i += 1; continue; }
                if (i + 2 < text.Length && text[i + 1] == '\r' && text[i + 2] == '\n') { i += 2; continue; }
                if (i + 2 < text.Length && Uri.IsHexDigit(text[i + 1]) && Uri.IsHexDigit(text[i + 2]))
                {
                    bytes.Add(Convert.ToByte(text.Substring(i + 1, 2), 16));
                    i += 2;
                    continue;
                }
            }
            bytes.Add(underscoreIsSpace && c == '_' ? (byte)' ' : (byte)c);
        }
        return bytes.ToArray();
    }

    private static Encoding Charset(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return new UTF8Encoding(false);   // undeclared text is mostly UTF-8 today
        try { return Encoding.GetEncoding(name.Trim().Trim('"')); }
        catch (ArgumentException) { return new UTF8Encoding(false); }
    }

    // ── Pieces ───────────────────────────────────────────────────────────────

    /// <summary>"Anna Beispiel &lt;anna@example.org&gt;" → name and address; the first of a list.</summary>
    private static (string Name, string Email) Address(string value)
    {
        var m = Regex.Match(value, @"^\s*(.*?)\s*<([^<>\s]+@[^<>\s]+)>");
        if (m.Success) return (m.Groups[1].Value.Trim().Trim('"').Trim(), m.Groups[2].Value.Trim());
        var e = Regex.Match(value, @"[^\s<>"",;]+@[^\s<>"",;]+");
        return ("", e.Success ? e.Value : "");
    }

    /// <summary>"Thu, 25 Sep 2026 14:02:11 +0200 (CEST)"</summary>
    private static DateTimeOffset? Date(string? value)
    {
        if (value == null) return null;
        string v = Regex.Replace(value, @"\([^)]*\)", "").Trim();
        v = Regex.Replace(v, @"^[A-Za-z]{3},\s*", "");
        v = Regex.Replace(v, @"\s+", " ");
        v = Regex.Replace(v, @"\s(GMT|UT|UTC|Z)$", " +00:00");
        v = Regex.Replace(v, @"([+-]\d{2})(\d{2})$", "$1:$2");
        return DateTimeOffset.TryParseExact(v, new[] { "d MMM yyyy HH:mm:ss zzz", "d MMM yyyy HH:mm zzz" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private static string HtmlToText(string html)
    {
        string s = Regex.Replace(html, @"<(script|style)\b[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<br\s*/?>|</p>|</div>|</li>|</tr>|</h\d>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]+>", "");
        return WebUtility.HtmlDecode(s);
    }

    private static int IndexOf(byte[] data, ReadOnlySpan<byte> what) => data.AsSpan().IndexOf(what);
}

/// <summary>Reply: the subject starts with Re:/AW:/Fwd: or the mail answers another one.</summary>
internal sealed record IncomingMail(string MessageId, string FromName, string FromEmail, string Subject,
    DateTimeOffset Received, string Text, bool IsReply);
