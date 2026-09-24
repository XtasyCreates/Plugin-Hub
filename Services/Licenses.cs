using System.Globalization;
using RhinoKit.Licensing;

namespace PluginHub.Services;

/// <summary>
/// Makes an activation code and puts it in the register. Every code is checked before it is
/// handed out - against the hub's key, and against the key the plug-in actually carries.
/// </summary>
public sealed class Licenses
{
    private readonly HubPaths _paths;
    private readonly Config _config;
    private readonly KeyStore _keys;
    private readonly Register _register;

    public Licenses(HubPaths paths, Config config, KeyStore keys, Register register)
    {
        _paths    = paths;
        _config   = config;
        _keys     = keys;
        _register = register;
    }

    public IssueResult Issue(LicenseRequest request)
    {
        var project = _config.Project(request.Product);
        if (!project.IsPlugin) throw new HubError($"{project.Name} ist kein Plugin.");

        string machineId = MachineIds.Normalize(request.MachineId);
        decimal? price   = Prices.Parse(request.Price);
        var key = _keys.Info(project.Id)
            ?? throw new HubError($"Für {project.Name} gibt es noch keinen Schlüssel. Leg ihn unter „Schlüssel und Backup“ an.");

        string code = _keys.Sign(project.Id, machineId);
        if (!ActivationCode.Verify(key.PublicKey, machineId, code))
            throw new HubError("Der eben erzeugte Code ließ sich nicht bestätigen. Bitte melde das.");

        string? pluginKey = PluginKeyFile.Read(_paths, project);
        bool matchesPlugin = pluginKey != null && ActivationCode.Verify(pluginKey, machineId, code);
        bool inBuild = PluginKeyFile.InBuild(_paths, project, key.PublicKey);

        Customer? customer = null;
        if (!string.IsNullOrWhiteSpace(request.CustomerId))
            customer = _register.FindCustomer(request.CustomerId) ?? throw new HubError("Diesen Kunden gibt es nicht mehr.");
        else if (!string.IsNullOrWhiteSpace(request.CustomerName))
            customer = _register.FindOrAddCustomer(request.CustomerName, request.CustomerEmail);

        var license = _register.AddLicense(new License
        {
            CustomerId     = customer?.Id,
            Product        = project.Id,
            MachineId      = machineId,
            Code           = code,
            KeyFingerprint = key.Fingerprint,
            Issued         = DateTimeOffset.Now,
            Price          = price,
            Paid           = request.Paid,
            Note           = request.Note?.Trim() ?? ""
        });
        Log.Info($"Code issued: {project.Id} for {machineId}");
        return new IssueResult(license, customer, matchesPlugin, inBuild);
    }
}

public sealed record LicenseRequest(string Product, string MachineId, string? CustomerId, string? CustomerName,
    string? CustomerEmail, string? Price, bool Paid, string? Note);

public sealed record IssueResult(License License, Customer? Customer, bool MatchesPlugin, bool InBuild);

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
