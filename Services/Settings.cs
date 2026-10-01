using System.Text;
using System.Text.RegularExpressions;

namespace PluginHub.Services;

/// <summary>
/// The hub's settings, in two places. What belongs to the plug-ins - contact address, trial
/// length, the seller named in the licence terms - lives in Hub\settings.json next to the
/// projects, in Git. The mail account lives in the private folder: mail.json for server and
/// sender, the password in mail.secret under DPAPI. The page never gets the password back.
/// </summary>
public sealed class Settings
{
    private static readonly Regex MailAddress = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private readonly HubPaths _paths;
    private readonly Config _config;
    private readonly object _gate = new();

    public Settings(HubPaths paths, Config config)
    {
        _paths  = paths;
        _config = config;
    }

    private string SharedFile => Path.Combine(_paths.DataDir, "settings.json");
    private string MailFile   => Path.Combine(_paths.PrivateDir, "mail.json");
    private string SecretFile => Path.Combine(_paths.PrivateDir, "mail.secret");

    public HubSettings Shared
    {
        get { lock (_gate) return Json.Read<HubSettings>(SharedFile) ?? new HubSettings(); }
    }

    public MailSettings Mail
    {
        get { lock (_gate) return Json.Read<MailSettings>(MailFile) ?? new MailSettings(); }
    }

    public bool HasPassword => File.Exists(SecretFile);

    /// <summary>Everything the hub needs to send a mail is there.</summary>
    public bool MailReady => HasPassword && Mail is { SenderEmail.Length: > 0, Host.Length: > 0, Port: > 0 };

    public HubSettings SaveShared(HubSettings settings)
    {
        settings.Contact         = settings.Contact.Trim();
        settings.Signature       = settings.Signature.Trim();
        settings.Seller          = settings.Seller.Trim();
        settings.SellerAddress   = settings.SellerAddress.Trim();
        settings.DefaultLanguage = settings.DefaultLanguage == "en" ? "en" : "de";
        // Empty from a page that does not know them yet: keep the defaults
        settings.SalesChannel = string.IsNullOrWhiteSpace(settings.SalesChannel) ? "mail" : settings.SalesChannel.Trim();
        settings.SiteStats    = string.IsNullOrWhiteSpace(settings.SiteStats) ? "off" : settings.SiteStats.Trim();

        if (!HubSettings.SalesChannels.Contains(settings.SalesChannel))
            throw new HubError("Verkaufsweg: „Per Mail“ oder „Online“.");
        if (!HubSettings.SiteStatsSources.Contains(settings.SiteStats))
            throw new HubError("Für die Website-Aufrufe gibt es diese Quelle noch nicht.");
        if (settings.Contact.Length > 0 && !MailAddress.IsMatch(settings.Contact))
            throw new HubError($"„{settings.Contact}“ ist keine E-Mail-Adresse.");
        if (settings.TrialDays is < 1 or > 365) throw new HubError("Die Testdauer muss zwischen 1 und 365 Tagen liegen.");
        if (settings.FollowUpDays is < 0 or > 60) throw new HubError("Die Nachfrage muss 0 bis 60 Tage vor dem Ende liegen.");
        if (settings.EndingSoonDays is < 1 or > 60) throw new HubError("„Läuft bald ab“ muss zwischen 1 und 60 Tagen liegen.");

        lock (_gate)
        {
            string oldContact = Shared.Contact;
            Json.Write(SharedFile, settings);

            // The contact is compiled into the plug-ins: write it where they read it
            if (oldContact != settings.Contact)
                foreach (var project in _config.Current.Projects.Where(p => p.IsPlugin && p.KeyFile != null))
                    PluginActivationFile.WriteContact(_paths, project, settings.Contact);
        }
        return settings;
    }

    public MailSettings SaveMail(MailSettings mail)
    {
        mail.SenderName  = mail.SenderName.Trim();
        mail.SenderEmail = mail.SenderEmail.Trim();
        mail.Host        = mail.Host.Trim();
        mail.UserName    = mail.UserName.Trim();
        if (mail.SenderEmail.Length > 0 && !MailAddress.IsMatch(mail.SenderEmail))
            throw new HubError($"„{mail.SenderEmail}“ ist keine E-Mail-Adresse.");
        if (mail.Port is < 1 or > 65535) throw new HubError("Der Port muss zwischen 1 und 65535 liegen.");

        lock (_gate) Json.Write(MailFile, mail);
        return mail;
    }

    /// <summary>Keeps the mail password under DPAPI; empty removes it.</summary>
    public void SetPassword(string? password)
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(password))
            {
                File.Delete(SecretFile);
                return;
            }
            byte[] protectedBytes = Dpapi.Protect(Encoding.UTF8.GetBytes(password), Entropy);
            Json.WriteText(SecretFile, Convert.ToBase64String(protectedBytes));
        }
    }

    public string Password()
    {
        lock (_gate)
        {
            if (!File.Exists(SecretFile)) throw new HubError("Für den Mailversand fehlt noch das Passwort (Einstellungen → E-Mail).");
            return Encoding.UTF8.GetString(Dpapi.Unprotect(Convert.FromBase64String(File.ReadAllText(SecretFile).Trim()), Entropy));
        }
    }

    public static bool IsMailAddress(string? text) => text != null && MailAddress.IsMatch(text.Trim());

    private static byte[] Entropy => Encoding.UTF8.GetBytes("PluginHub mail password");
}

/// <summary>Hub\settings.json - what belongs to the products, in Git.</summary>
public sealed class HubSettings
{
    /// <summary>The name under mails and reply texts.</summary>
    public string Signature { get; set; } = "";
    /// <summary>Where customers write: the request buttons in the plug-ins, reply-to of mails, the guide.</summary>
    public string Contact { get; set; } = "";
    /// <summary>Who grants the licence - name and address, used in the licence terms.</summary>
    public string Seller { get; set; } = "";
    public string SellerAddress { get; set; } = "";
    /// <summary>The trial length offered first when making a code (a placeholder until decided).</summary>
    public int TrialDays { get; set; } = 30;
    /// <summary>How many days before a trial ends the hub suggests the follow-up mail.</summary>
    public int FollowUpDays { get; set; } = 3;
    /// <summary>From how many days before its end a code counts as "läuft bald ab".</summary>
    public int EndingSoonDays { get; set; } = 7;
    public string DefaultLanguage { get; set; } = "de";

    public static readonly string[] SalesChannels = { "mail", "online" };
    /// <summary>"off" until the website is online - the source of its visit numbers joins this list then.</summary>
    public static readonly string[] SiteStatsSources = { "off" };

    /// <summary>
    /// How codes are sold now: "mail" (requested by mail, made in the hub) or "online" (website and
    /// payment provider). Every new code remembers it, so the statistics keep both ways apart.
    /// </summary>
    public string SalesChannel { get; set; } = "mail";
    /// <summary>Where the website's visit numbers come from - "off" while the site is not online.</summary>
    public string SiteStats { get; set; } = "off";
}

/// <summary>mail.json in the private folder - the account the hub sends from (password apart).</summary>
public sealed class MailSettings
{
    public string SenderName { get; set; } = "";
    public string SenderEmail { get; set; } = "";
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    /// <summary>Login name - for Gmail the address itself; empty means the sender address.</summary>
    public string UserName { get; set; } = "";
}
