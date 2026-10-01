using System.Globalization;
using RhinoKit.Licensing;

namespace PluginHub.Services;

/// <summary>
/// Makes activation codes and puts them in the register - new ones, and extensions of existing
/// ones for the same device. Every code is checked before it is handed out: against the hub's
/// key, and against the key the plug-in actually carries.
/// </summary>
public sealed class Licenses
{
    private readonly HubPaths _paths;
    private readonly Config _config;
    private readonly KeyStore _keys;
    private readonly Register _register;
    private readonly Settings _settings;

    public Licenses(HubPaths paths, Config config, KeyStore keys, Register register, Settings settings)
    {
        _paths    = paths;
        _config   = config;
        _keys     = keys;
        _register = register;
        _settings = settings;
    }

    public IssueResult Issue(LicenseRequest request)
    {
        var project = Plugin(request.Product);
        string machineId = MachineIds.Normalize(request.MachineId);
        DateOnly? end = Ends.Parse(request.End);
        decimal? price = Prices.Parse(request.Price);

        Customer? customer = null;
        if (!string.IsNullOrWhiteSpace(request.CustomerId))
            customer = _register.FindCustomer(request.CustomerId) ?? throw new HubError("Diesen Kunden gibt es nicht mehr.");
        else if (!string.IsNullOrWhiteSpace(request.CustomerName))
            customer = _register.FindOrAddCustomer(request.CustomerName, request.CustomerEmail,
                request.Language ?? _settings.Shared.DefaultLanguage);

        var (license, fit) = Make(project, machineId, end);
        license.CustomerId = customer?.Id;
        license.Trial      = request.Trial && end != null;
        license.Price      = price;
        license.Paid       = request.Paid;
        license.Channel    = _settings.Shared.SalesChannel;
        license.Note       = request.Note?.Trim() ?? "";
        license = _register.AddLicense(license);

        Log.Info($"Code issued: {project.Id} for {machineId}, end {end?.ToString("yyyy-MM-dd") ?? "none"}");
        return new IssueResult(license, customer, fit.MatchesPlugin, fit.InBuild);
    }

    /// <summary>A new code for the same product and device - a longer trial, or the bought licence.</summary>
    public IssueResult Extend(string licenseId, ExtendRequest request)
    {
        var old = _register.FindLicense(licenseId) ?? throw new HubError("Diese Lizenz gibt es nicht mehr.");
        if (old.ReplacedBy != null) throw new HubError("Dieser Code wurde schon verlängert – verlängere den neuen.");
        var project = Plugin(old.Product);
        DateOnly? end = Ends.Parse(request.End);

        var (license, fit) = Make(project, old.MachineId, end);
        license.CustomerId = old.CustomerId;
        license.Trial      = request.Trial && end != null;
        license.Price      = Prices.Parse(request.Price);
        license.Paid       = request.Paid;
        license.Channel    = _settings.Shared.SalesChannel;
        license.Note       = request.Note?.Trim() ?? "";
        license = _register.AddExtension(old.Id, license);

        var customer = old.CustomerId == null ? null : _register.FindCustomer(old.CustomerId);
        Log.Info($"Code extended: {project.Id} for {old.MachineId}, end {end?.ToString("yyyy-MM-dd") ?? "none"}");
        return new IssueResult(license, customer, fit.MatchesPlugin, fit.InBuild);
    }

    private (License License, (bool MatchesPlugin, bool InBuild) Fit) Make(ProjectDef project, string machineId, DateOnly? end)
    {
        var key = _keys.Info(project.Id)
            ?? throw new HubError($"Für {project.Name} gibt es noch keinen Schlüssel. Leg ihn unter „Schlüssel und Backup“ an.");

        string code = _keys.Sign(project.Id, machineId, end);
        DateTime? endDay = end?.ToDateTime(TimeOnly.MinValue);
        var grant = ActivationCode.Read(key.PublicKey, machineId, code);
        if (grant == null || grant.End != endDay)
            throw new HubError("Der eben erzeugte Code ließ sich nicht bestätigen. Bitte melde das.");

        string? pluginKey = PluginActivationFile.ReadKey(_paths, project);
        bool matchesPlugin = pluginKey != null && ActivationCode.Read(pluginKey, machineId, code) != null;
        bool inBuild = PluginActivationFile.InBuild(_paths, project, key.PublicKey);

        var license = new License
        {
            Product        = project.Id,
            MachineId      = machineId,
            Code           = code,
            KeyFingerprint = key.Fingerprint,
            Issued         = DateTimeOffset.Now,
            End            = end
        };
        return (license, (matchesPlugin, inBuild));
    }

    private ProjectDef Plugin(string id)
    {
        var project = _config.Project(id);
        if (!project.IsPlugin) throw new HubError($"{project.Name} ist kein Plugin.");
        return project;
    }
}

public sealed record LicenseRequest(string Product, string MachineId, string? CustomerId, string? CustomerName,
    string? CustomerEmail, string? Language, bool Trial, string? End, string? Price, bool Paid, string? Note);

public sealed record ExtendRequest(bool Trial, string? End, string? Price, bool Paid, string? Note);

public sealed record IssueResult(License License, Customer? Customer, bool MatchesPlugin, bool InBuild);

/// <summary>
/// Where a code stands today. State: unlimited | valid | ending | expired | oldkey | replaced.
/// A code that was extended counts as replaced; one signed with a key the hub no longer holds is
/// dead whatever its date says.
/// </summary>
public sealed record LicenseStatus(string State, string Label, int? DaysLeft)
{
    public static LicenseStatus Of(License license, string? currentFingerprint, DateOnly today, int endingSoonDays)
    {
        if (license.ReplacedBy != null) return new("replaced", "verlängert", null);
        if (currentFingerprint == null || license.KeyFingerprint != currentFingerprint)
            return new("oldkey", "alter Schlüssel – ungültig", null);
        if (license.End is not DateOnly end) return new("unlimited", "unbegrenzt", null);

        int left = end.DayNumber - today.DayNumber;
        string date = end.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        if (left < 0) return new("expired", $"abgelaufen am {date}", left);
        if (left <= endingSoonDays)
            return new("ending", left switch { 0 => "läuft heute ab", 1 => "läuft morgen ab", _ => $"läuft in {left} Tagen ab" }, left);
        return new("valid", $"gültig bis {date}", left);
    }
}

/// <summary>A licence as the page sees it: the stored fields and where it stands.</summary>
public sealed record LicenseView(License License, LicenseStatus Status);

/// <summary>End dates as the page sends them ("2027-01-31"), empty for no end.</summary>
public static class Ends
{
    public static DateOnly? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            throw new HubError($"„{text}“ ist kein Datum.");
        if (end < DateOnly.FromDateTime(DateTime.Today))
            throw new HubError("Das Enddatum liegt in der Vergangenheit – der Code wäre sofort ungültig.");
        return end;
    }
}

/// <summary>The machine id as MachineId.ForUser shows it: 24 hex digits in six groups of four.</summary>
public static class MachineIds
{
    public static string Normalize(string? input)
    {
        string text = input?.Trim() ?? "";
        if (text.Length == 0) throw new HubError("Füge die Geräte-ID aus dem Aktivierungsfenster ein.");

        var foreign = text.Where(c => !char.IsAsciiHexDigit(c) && c != '-' && !char.IsWhiteSpace(c)).Distinct().ToList();
        if (foreign.Count > 0)
            throw new HubError($"Eine Geräte-ID besteht nur aus 0–9 und A–F. Nicht dazu gehört: {string.Join(" ", foreign)}");

        string hex = new string(text.Where(char.IsAsciiHexDigit).ToArray()).ToUpperInvariant();
        if (hex.Length != 24)
            throw new HubError($"Eine Geräte-ID hat 24 Zeichen in sechs Vierergruppen – diese hat {hex.Length}.");

        return string.Join("-", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 4, 4)));
    }
}

/// <summary>Prices as typed: "49", "49,90", "49.90 €" - empty for none.</summary>
public static class Prices
{
    public static decimal? Parse(string? input)
    {
        string text = (input ?? "").Replace("€", "").Replace(" ", "").Trim();
        if (text.Length == 0) return null;
        if (!decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal price))
            throw new HubError($"„{input}“ ist kein Preis. Schreib ihn z. B. als 49 oder 49,90.");
        return Math.Round(price, 2);
    }
}
