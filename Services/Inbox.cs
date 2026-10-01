using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PluginHub.Services;

/// <summary>
/// Code requests from customers - "Request a free trial" or "Buy a license" in the activation window, or a mail of their
/// own with the device id - arrive in the mailbox of the hub's mail account, the contact address.
/// The hub reads that mailbox over IMAP with the same login it sends with, and only reads:
/// nothing in the mailbox is marked, moved or deleted. A mail counts as a request when it carries
/// a device id and is not a reply; product, trial or licence and version come from the text the
/// plug-in wrote.
///
/// The requests and what became of them are kept in the private folder (inbox.json) - they hold
/// customers' addresses. While the hub runs it looks every two minutes, and on request.
///
/// Tests: PLUGINHUB_MAIL_DROP=&lt;folder&gt; reads the .eml files there instead of a mailbox, and
/// PLUGINHUB_IMAP=host:port talks to that server without TLS. A test never logs in to a real account.
/// </summary>
public sealed class Inbox
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    private static readonly Regex DeviceId =
        new(@"(?<![0-9A-Fa-f])[0-9A-Fa-f]{4}(?:[-\s][0-9A-Fa-f]{4}){5}(?![0-9A-Fa-f])", RegexOptions.Compiled);
    private static readonly Regex VersionText = new(@"\(version ([^)\s]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static readonly string[] Statuses = { "neu", "erledigt", "ignoriert" };

    private const int FirstLookBackDays = 30;
    private const int Batch = 20;

    private readonly HubPaths _paths;
    private readonly Config _config;
    private readonly Settings _settings;
    private readonly object _file = new();
    private readonly object _checking = new();

    public Inbox(HubPaths paths, Config config, Settings settings)
    {
        _paths    = paths;
        _config   = config;
        _settings = settings;
    }

    private string StateFile => Path.Combine(_paths.PrivateDir, "inbox.json");

    private static string? DropFolder => Environment.GetEnvironmentVariable("PLUGINHUB_MAIL_DROP") is { Length: > 0 } d ? d : null;

    /// <summary>A mailbox to read: the mail account is set up, or a test folder stands in for it.</summary>
    public bool Ready => _settings.MailReady || DropFolder != null;

    public InboxState State()
    {
        lock (_file) return Json.Read<InboxState>(StateFile) ?? new InboxState();
    }

    public int NewCount => State().Requests.Count(r => r.Status == "neu");

    /// <summary>Looks for new mail now; returns how many new requests came in.</summary>
    public int Check()
    {
        lock (_checking)
        {
            var mail = _settings.Mail;
            if (!Ready)
                throw new HubError("Der Hub liest die Anfragen mit dem Mailkonto aus den Einstellungen – richte es zuerst ein (Einstellungen → E-Mail).");

            var seen = State();
            bool sameBox = string.Equals(seen.Account, mail.SenderEmail, StringComparison.OrdinalIgnoreCase);

            List<IncomingMail> mails;
            uint validity = sameBox ? seen.UidValidity : 0, lastUid = sameBox ? seen.LastUid : 0;
            string? error = null;
            try
            {
                if (DropFolder is { } drop) mails = ReadDrop(drop);
                else (mails, validity, lastUid) = ReadMailbox(mail, validity, lastUid);
            }
            catch (Exception ex) when (ex is ImapException or IOException or SocketException or HubError)
            {
                mails = new();
                error = ex is HubError ? ex.Message : Explain(ex.Message);
                Log.Error("Inbox: " + ex.Message);
            }

            // Read the file again: while the mailbox was being read, the page may have handled a request
            int added = 0;
            lock (_file)
            {
                var state = State();
                state.Account     = mail.SenderEmail;
                state.UidValidity = validity;
                state.LastUid     = lastUid;
                state.LastCheck   = DateTimeOffset.Now;
                state.LastError   = error;

                var plugins = _config.Current.Projects.Where(p => p.IsPlugin).ToList();
                foreach (var m in mails.OrderBy(m => m.Received))
                {
                    if (state.Requests.Any(r => r.MessageId == m.MessageId)) continue;
                    if (Recognize(m, plugins) is not { } request) continue;
                    state.Requests.Add(request);
                    added++;
                }
                Json.Write(StateFile, state);
            }

            if (error != null) throw new HubError(error);
            if (added > 0) Log.Info($"Inbox: {added} new code request(s)");
            return added;
        }
    }

    /// <summary>Marks a request as handled (with the code made for it), turned down, or open again.</summary>
    public CodeRequest Handle(string id, string status, string? licenseId)
    {
        if (!Statuses.Contains(status)) throw new HubError($"Unbekannter Stand „{status}“.");
        lock (_file)
        {
            var state = State();
            var request = state.Requests.FirstOrDefault(r => r.Id == id) ?? throw new HubError("Diese Anfrage gibt es nicht mehr.");
            request.Status    = status;
            request.HandledAt = status == "neu" ? null : DateTimeOffset.Now;
            if (!string.IsNullOrEmpty(licenseId)) request.LicenseId = licenseId;
            Json.Write(StateFile, state);
            return request;
        }
    }

    /// <summary>The requests for the page, each with the customer it probably is and the codes that device has.</summary>
    public InboxView View(RegisterView register)
    {
        var state = State();
        var names = _config.Current.Projects.ToDictionary(p => p.Id, p => p.Name);

        RequestView Describe(CodeRequest r)
        {
            var codes = register.Licenses.Where(l => l.License.MachineId == r.MachineId).ToList();
            var customer =
                register.Customers.FirstOrDefault(c => r.FromEmail.Length > 0 && string.Equals(c.Email, r.FromEmail, StringComparison.OrdinalIgnoreCase))
                ?? register.Customers.FirstOrDefault(c => codes.Any(l => l.License.CustomerId == c.Id));
            return new RequestView(r, customer?.Id, customer?.Name, customer?.Language,
                codes.Select(l => new KnownCode(l.License.Id, names.GetValueOrDefault(l.License.Product, l.License.Product),
                    l.Status.State, l.Status.Label)).ToList());
        }

        return new InboxView(Ready, state.Account.Length > 0 ? state.Account : _settings.Mail.SenderEmail,
            state.LastCheck, state.LastError, (int)Interval.TotalMinutes,
            state.Requests.OrderBy(r => r.Status == "neu" ? 0 : 1).ThenByDescending(r => r.Received)
                .Take(200).Select(Describe).ToList());
    }

    /// <summary>Looks every two minutes while the hub runs - the first time shortly after it starts.</summary>
    public void Watch(CancellationToken stop)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(8), stop);
                while (!stop.IsCancellationRequested)
                {
                    if (Ready)
                    {
                        try { Check(); }
                        catch (HubError) { /* kept as LastError - the page says it */ }
                        catch (Exception ex) { Log.Error("Inbox: " + ex); }
                    }
                    await Task.Delay(Interval, stop);
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    // ── Reading ──────────────────────────────────────────────────────────────

    private (List<IncomingMail> Mails, uint Validity, uint LastUid) ReadMailbox(MailSettings mail, uint validity, uint lastUid)
    {
        var (host, port, tls) = Server(mail);
        using var imap = ImapSession.Connect(host, port, tls, TimeSpan.FromSeconds(25));
        imap.Login(mail.UserName.Length > 0 ? mail.UserName : mail.SenderEmail, _settings.Password());

        var box = imap.Examine("INBOX");
        if (box.Validity != validity) lastUid = 0;   // the folder was rebuilt: its UIDs start over

        // The first time: the last month. After that: whatever came after the newest mail seen -
        // "n:*" answers with the newest mail even when nothing came after n, hence the filter.
        var uids = lastUid == 0
            ? imap.Search("SINCE " + DateTime.Today.AddDays(-FirstLookBackDays).ToString("d-MMM-yyyy", CultureInfo.InvariantCulture))
            : imap.Search($"UID {lastUid + 1}:*").Where(u => u > lastUid).ToList();

        var mails = new List<IncomingMail>();
        foreach (var chunk in uids.Order().Chunk(Batch))
            foreach (var fetched in imap.Fetch(chunk))
            {
                try { mails.Add(MailParser.Parse(fetched.Raw, fetched.Arrived)); }
                catch (Exception ex) { Log.Error($"Inbox: mail {fetched.Uid} could not be read: {ex.Message}"); }
            }
        imap.Logout();

        uint newest = Math.Max(uids.Count > 0 ? uids.Max() : 0, box.UidNext > 0 ? box.UidNext - 1 : 0);
        return (mails, box.Validity, Math.Max(lastUid, newest));
    }

    private static List<IncomingMail> ReadDrop(string folder)
    {
        if (!Directory.Exists(folder)) return new();
        return Directory.GetFiles(folder, "*.eml").OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => MailParser.Parse(File.ReadAllBytes(f))).ToList();
    }

    /// <summary>The account mails go out from is the one they come in to: smtp.gmail.com → imap.gmail.com.</summary>
    private static (string Host, int Port, bool Tls) Server(MailSettings mail)
    {
        if (Environment.GetEnvironmentVariable("PLUGINHUB_IMAP") is { Length: > 0 } test && test.Split(':') is [var h, var p])
            return (h, int.Parse(p, CultureInfo.InvariantCulture), false);

        string host = mail.Host.StartsWith("smtp.", StringComparison.OrdinalIgnoreCase) ? "imap." + mail.Host[5..] : mail.Host;
        return (host, 993, true);
    }

    private static string Explain(string message)
    {
        if (message.Contains("AUTHENTICATIONFAILED", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Invalid credentials", StringComparison.OrdinalIgnoreCase))
            return "Gmail hat die Anmeldung zum Lesen abgelehnt – stimmt das App-Passwort? (Einstellungen → E-Mail)";
        if (message.Contains("IMAP", StringComparison.OrdinalIgnoreCase) &&
            (message.Contains("disabled", StringComparison.OrdinalIgnoreCase) || message.Contains("not enabled", StringComparison.OrdinalIgnoreCase)))
            return "In Gmail ist IMAP ausgeschaltet: Gmail → Einstellungen → „Weiterleitung und POP/IMAP“ → IMAP aktivieren.";
        return "Das Postfach ließ sich nicht lesen: " + message;
    }

    // ── What a request is ────────────────────────────────────────────────────

    /// <summary>
    /// A mail with a device id that is not a reply. The plug-in's request names the product in
    /// its subject, ticks "a free trial" or "a full license" - the button the customer clicked,
    /// or what they marked in an older plug-in's mail - and gives the version in brackets.
    /// </summary>
    internal static CodeRequest? Recognize(IncomingMail mail, List<ProjectDef> plugins)
    {
        if (mail.IsReply) return null;
        var found = DeviceId.Match(mail.Text);
        if (!found.Success) return null;

        string machineId;
        try { machineId = MachineIds.Normalize(found.Value); }
        catch (HubError) { return null; }

        string? product = plugins.FirstOrDefault(p => mail.Subject.Contains(p.Name, StringComparison.OrdinalIgnoreCase))?.Id
                       ?? plugins.FirstOrDefault(p => mail.Text.Contains(p.Name, StringComparison.OrdinalIgnoreCase))?.Id;
        bool trial = Ticked(mail.Text, @"a free trial");
        bool full  = Ticked(mail.Text, @"a full licen[cs]e");
        var version = VersionText.Match(mail.Text);

        return new CodeRequest
        {
            Id        = "q" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant(),
            MessageId = mail.MessageId,
            Received  = mail.Received,
            FromName  = mail.FromName,
            FromEmail = mail.FromEmail,
            Subject   = mail.Subject,
            Product   = product,
            MachineId = machineId,
            Wants     = trial == full ? null : trial ? "trial" : "full",
            Version   = version.Success ? version.Groups[1].Value : null,
            Text      = mail.Text.Length > 4000 ? mail.Text[..4000] + " …" : mail.Text
        };
    }

    private static bool Ticked(string text, string label) =>
        Regex.IsMatch(text, @"\[\s*[xX✓✔*]\s*\]\s*" + label, RegexOptions.IgnoreCase);
}

/// <summary>inbox.json in the private folder.</summary>
public sealed class InboxState
{
    /// <summary>The mailbox the UIDs below belong to.</summary>
    public string Account { get; set; } = "";
    public uint UidValidity { get; set; }
    /// <summary>The newest mail already looked at.</summary>
    public uint LastUid { get; set; }
    public DateTimeOffset? LastCheck { get; set; }
    public string? LastError { get; set; }
    public List<CodeRequest> Requests { get; set; } = new();
}

public sealed class CodeRequest
{
    public string Id { get; set; } = "";
    public string MessageId { get; set; } = "";
    public DateTimeOffset Received { get; set; }
    public string FromName { get; set; } = "";
    public string FromEmail { get; set; } = "";
    public string Subject { get; set; } = "";
    /// <summary>A plug-in id from projects.json; null when the mail names none.</summary>
    public string? Product { get; set; }
    public string MachineId { get; set; } = "";
    /// <summary>trial | full | null - what the customer ticked.</summary>
    public string? Wants { get; set; }
    public string? Version { get; set; }
    public string Text { get; set; } = "";
    /// <summary>neu | erledigt | ignoriert</summary>
    public string Status { get; set; } = "neu";
    /// <summary>The code made for it.</summary>
    public string? LicenseId { get; set; }
    public DateTimeOffset? HandledAt { get; set; }
}

public sealed record InboxView(bool Ready, string Account, DateTimeOffset? LastCheck, string? Error, int IntervalMinutes,
    List<RequestView> Requests);

/// <summary>Customer: matched by address, else by a code for the same device. Codes: every code that device has.</summary>
public sealed record RequestView(CodeRequest Request, string? CustomerId, string? CustomerName, string? CustomerLanguage,
    List<KnownCode> Codes);

public sealed record KnownCode(string LicenseId, string Product, string State, string Label);

public sealed record InboxStatusRequest(string Status, string? LicenseId);
