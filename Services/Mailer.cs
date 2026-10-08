using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace PluginHub.Services;

/// <summary>
/// Mails to customers: drafts from the templates in Hub\templates\mails.json, the follow-ups
/// that are due, and sending over SMTP with the account from the settings. Nothing is sent
/// unless the page asks for exactly that mail - the hub only suggests what is due.
///
/// PLUGINHUB_MAIL_PICKUP=&lt;folder&gt; writes mails into that folder instead of sending them -
/// for tests, which must never reach a real inbox.
/// </summary>
public sealed class Mailer
{
    public static readonly string[] Kinds = { "code", "followup", "extension" };

    private static readonly Regex Placeholder = new(@"\{(\w+)\}", RegexOptions.Compiled);
    private static readonly CultureInfo German  = CultureInfo.GetCultureInfo("de-DE");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-GB");

    private readonly HubPaths _paths;
    private readonly Config _config;
    private readonly Settings _settings;
    private readonly Register _register;
    private readonly KeyStore _keys;
    private readonly object _gate = new();

    public Mailer(HubPaths paths, Config config, Settings settings, Register register, KeyStore keys)
    {
        _paths    = paths;
        _config   = config;
        _settings = settings;
        _register = register;
        _keys     = keys;
    }

    private string TemplateFile => Path.Combine(_paths.DataDir, "templates", "mails.json");

    public Dictionary<string, Dictionary<string, MailTemplate>> Templates
    {
        get { lock (_gate) return Json.Read<Dictionary<string, Dictionary<string, MailTemplate>>>(TemplateFile) ?? new(); }
    }

    public void SaveTemplates(Dictionary<string, Dictionary<string, MailTemplate>> templates)
    {
        foreach (var kind in Kinds)
            foreach (var lang in new[] { "de", "en" })
                if (!templates.TryGetValue(kind, out var byLang) || !byLang.TryGetValue(lang, out var t) ||
                    string.IsNullOrWhiteSpace(t.Subject) || string.IsNullOrWhiteSpace(t.Body))
                    throw new HubError($"Die Vorlage „{kind}“ ({lang}) braucht Betreff und Text.");
        lock (_gate) Json.Write(TemplateFile, templates);
    }

    /// <summary>The mail of this kind for this code, in the customer's language unless one is given.</summary>
    public MailDraft Draft(string kind, string licenseId, string? language = null)
    {
        if (!Kinds.Contains(kind)) throw new HubError($"Unbekannte Mail-Art „{kind}“.");
        var license = _register.FindLicense(licenseId) ?? throw new HubError("Diese Lizenz gibt es nicht mehr.");
        var customer = license.CustomerId == null ? null : _register.FindCustomer(license.CustomerId);
        var project = _config.Project(license.Product);
        string lang = language is "de" or "en" ? language : customer?.Language ?? _settings.Shared.DefaultLanguage;

        var template = Templates.GetValueOrDefault(kind)?.GetValueOrDefault(lang)
            ?? throw new HubError($"Die Mail-Vorlage „{kind}“ ({lang}) fehlt in Hub\\templates\\mails.json.");

        var shared = _settings.Shared;
        var values = new Dictionary<string, string>
        {
            ["name"]              = customer?.Name ?? "",
            ["product"]           = project.Name,
            ["code"]              = license.Code,
            ["deviceId"]          = license.MachineId,
            ["end"]               = license.End is DateOnly end ? DateText(end, lang) : "",
            ["validity"]          = Validity(license.End, lang),
            ["daysLeft"]          = license.End is DateOnly e ? (e.DayNumber - Today.DayNumber).ToString(CultureInfo.InvariantCulture) : "",
            ["command"]           = project.StartCommand ?? "",
            ["activationCommand"] = project.ActivationCommand ?? project.StartCommand ?? "",
            ["signature"]         = shared.Signature,
            ["contact"]           = shared.Contact
        };

        return new MailDraft(kind, license.Id, customer?.Email ?? "", lang, Fill(template.Subject, values), Fill(template.Body, values));
    }

    /// <summary>Follow-ups to suggest now: trials close to their end whose customer may be asked.</summary>
    public List<DueMail> Due()
    {
        var shared = _settings.Shared;
        var data = _register.Snapshot();
        var list = new List<DueMail>();

        foreach (var l in data.Licenses)
        {
            if (!l.Trial || l.End is not DateOnly end || l.ReplacedBy != null || l.FollowUp != null) continue;
            int left = end.DayNumber - Today.DayNumber;
            // Due from FollowUpDays before the end; after two weeks past it, the moment has gone
            if (left > shared.For(l.Product).FollowUpDays || left < -14) continue;
            if (_keys.Info(l.Product)?.Fingerprint != l.KeyFingerprint) continue;

            var customer = data.Customers.FirstOrDefault(c => c.Id == l.CustomerId);
            if (customer == null || !customer.MailConsent || !Settings.IsMailAddress(customer.Email)) continue;

            string product = _config.Current.Projects.FirstOrDefault(p => p.Id == l.Product)?.Name ?? l.Product;
            list.Add(new DueMail(l.Id, customer.Id, customer.Name, product, end, left, Draft("followup", l.Id)));
        }
        return list.OrderBy(d => d.DaysLeft).ToList();
    }

    /// <summary>
    /// Sends a mail about a code - as the page shows it, edits included - and records it.
    /// <paramref name="inReplyTo"/>: the message id of the customer's request, so the answer
    /// lands in the same conversation in both mailboxes.
    /// </summary>
    public MailRecord Send(string kind, string licenseId, string subject, string body, string? inReplyTo = null)
    {
        var draft = Draft(kind, licenseId);
        if (!Settings.IsMailAddress(draft.To))
            throw new HubError("Beim Kunden steht keine gültige E-Mail-Adresse.");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
            throw new HubError("Betreff und Text dürfen nicht leer sein.");

        Deliver(draft.To, subject.Trim(), body, inReplyTo);

        var license = _register.FindLicense(licenseId)!;
        if (kind == "followup") _register.MarkFollowUp(licenseId, skipped: false);
        return _register.AddMail(new MailRecord
        {
            CustomerId = license.CustomerId,
            LicenseId  = licenseId,
            Kind       = kind,
            To         = draft.To,
            Subject    = subject.Trim(),
            Sent       = DateTimeOffset.Now
        });
    }

    /// <summary>A short mail to the sender address - proves the account works.</summary>
    public string SendTest()
    {
        var mail = _settings.Mail;
        if (!Settings.IsMailAddress(mail.SenderEmail)) throw new HubError("Trag zuerst die Absender-Adresse ein.");
        Deliver(mail.SenderEmail, "Plugin Hub: Test-Mail",
            "Diese Mail kommt vom Plugin Hub. Wenn du sie liest, klappt der Versand.\r\n\r\n" +
            $"Gesendet am {DateTime.Now.ToString("dd.MM.yyyy 'um' HH:mm", German)}.");
        return mail.SenderEmail;
    }

    private void Deliver(string to, string subject, string body, string? inReplyTo = null)
    {
        var mail = _settings.Mail;
        if (!Settings.IsMailAddress(mail.SenderEmail))
            throw new HubError("Der Mailversand ist noch nicht eingerichtet (Einstellungen → E-Mail).");

        using var message = new MailMessage(
            new MailAddress(mail.SenderEmail, mail.SenderName.Length > 0 ? mail.SenderName : null, Encoding.UTF8),
            new MailAddress(to))
        {
            Subject         = subject,
            SubjectEncoding = Encoding.UTF8,
            Body            = body.Replace("\r\n", "\n").Replace("\n", "\r\n"),
            BodyEncoding    = Encoding.UTF8,
            IsBodyHtml      = false
        };
        string contact = _settings.Shared.Contact;
        if (Settings.IsMailAddress(contact) && !contact.Equals(mail.SenderEmail, StringComparison.OrdinalIgnoreCase))
            message.ReplyToList.Add(contact);

        // An answer to a request: mail programs thread it with the request
        string? parent = inReplyTo?.Trim();
        if (parent is { Length: > 2 } && parent.StartsWith('<') && parent.EndsWith('>') && !parent.Any(char.IsWhiteSpace))
        {
            message.Headers.Add("In-Reply-To", parent);
            message.Headers.Add("References", parent);
        }

        using var client = new SmtpClient(mail.Host, mail.Port) { Timeout = 30_000 };
        string? pickup = Environment.GetEnvironmentVariable("PLUGINHUB_MAIL_PICKUP");
        if (!string.IsNullOrEmpty(pickup))
        {
            Directory.CreateDirectory(pickup);
            client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            client.PickupDirectoryLocation = pickup;
        }
        else
        {
            client.EnableSsl   = true;   // STARTTLS on 587, as Gmail wants it
            client.Credentials = new NetworkCredential(mail.UserName.Length > 0 ? mail.UserName : mail.SenderEmail, _settings.Password());
        }

        try
        {
            client.Send(message);
            Log.Info($"Mail sent to {to}: {subject}");
        }
        catch (SmtpException ex)
        {
            Log.Error($"Mail to {to} failed: {ex}");
            string reason = ex.Message.Contains("5.7.8") || ex.Message.Contains("Authentication") || ex.StatusCode == SmtpStatusCode.MustIssueStartTlsFirst
                ? "Die Anmeldung wurde abgelehnt – stimmen Absender-Adresse und App-Passwort?"
                : ex.InnerException?.Message ?? ex.Message;
            throw new HubError("Die Mail ging nicht raus. " + reason);
        }
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private static string DateText(DateOnly day, string lang) =>
        lang == "en" ? day.ToString("d MMMM yyyy", English) : day.ToString("dd.MM.yyyy", German);

    // A code works through its last day, as the activation window says since v48
    private static string Validity(DateOnly? end, string lang) => end is DateOnly day
        ? lang == "en" ? $"The code works until 23:59 on {DateText(day, lang)}." : $"Der Code gilt bis zum {DateText(day, lang)}, 23:59 Uhr."
        : lang == "en" ? "The code has no end date." : "Der Code gilt unbegrenzt.";

    private static string Fill(string text, Dictionary<string, string> values)
    {
        string filled = Placeholder.Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
        // "Hallo {name}," without a name would read "Hallo ,"
        return filled.Replace(" ,", ",");
    }
}

public sealed class MailTemplate
{
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
}

public sealed record MailDraft(string Kind, string LicenseId, string To, string Language, string Subject, string Body);

public sealed record DueMail(string LicenseId, string CustomerId, string CustomerName, string Product,
    DateOnly End, int DaysLeft, MailDraft Draft);
