using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PluginHub.Services;

/// <summary>
/// The customer register: who got which activation code, for which product and device, until
/// when, whether it was paid, and which mails went out. Kept in register.json in the private
/// folder. Every code the hub makes is written here, because codes cannot be looked up anywhere
/// else - signing the same machine id twice gives two different codes, both valid.
/// </summary>
public sealed class Register
{
    private readonly HubPaths _paths;
    private readonly object _gate = new();
    private readonly RegisterData _data;

    public Register(HubPaths paths)
    {
        _paths = paths;
        _data  = Json.Read<RegisterData>(paths.RegisterFile) ?? new RegisterData();
    }

    public RegisterData Snapshot()
    {
        lock (_gate) return JsonSerializer.Deserialize<RegisterData>(JsonSerializer.Serialize(_data, Json.Options), Json.Options)!;
    }

    /// <summary>Changes whenever anything in the register does (backups compare it).</summary>
    public string Hash()
    {
        lock (_gate) return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(_data, Json.Options)));
    }

    public Customer? FindCustomer(string id)
    {
        lock (_gate) return _data.Customers.FirstOrDefault(c => c.Id == id);
    }

    public License? FindLicense(string id)
    {
        lock (_gate) return _data.Licenses.FirstOrDefault(l => l.Id == id);
    }

    public Customer AddCustomer(CustomerFields fields) => Change(data =>
    {
        var customer = new Customer { Id = NewId("k"), Created = DateTimeOffset.Now };
        Apply(customer, fields);
        data.Customers.Add(customer);
        return customer;
    });

    /// <summary>The customer of that name (any case), or a new one.</summary>
    public Customer FindOrAddCustomer(string name, string? email, string language) => Change(data =>
    {
        string wanted = RequireName(name);
        var existing = data.Customers.FirstOrDefault(c => string.Equals(c.Name, wanted, StringComparison.CurrentCultureIgnoreCase));
        if (existing != null)
        {
            if (existing.Email.Length == 0 && !string.IsNullOrWhiteSpace(email)) existing.Email = email.Trim();
            return existing;
        }
        var customer = new Customer
        {
            Id       = NewId("k"),
            Name     = wanted,
            Email    = email?.Trim() ?? "",
            Language = language == "en" ? "en" : "de",
            Created  = DateTimeOffset.Now
        };
        data.Customers.Add(customer);
        return customer;
    });

    public Customer UpdateCustomer(string id, CustomerFields fields) => Change(data =>
    {
        var customer = Find(data, id);
        Apply(customer, fields);
        return customer;
    });

    public void DeleteCustomer(string id, bool withLicenses) => Change(data =>
    {
        var customer = Find(data, id);
        int own = data.Licenses.Count(l => l.CustomerId == id);
        if (own > 0 && !withLicenses)
            throw new HubError($"{customer.Name} hat {own} Lizenz(en). Lösch sie mit oder behalte den Kunden.");
        data.Licenses.RemoveAll(l => l.CustomerId == id);
        data.Customers.Remove(customer);
        return true;
    });

    public License AddLicense(License license) => Change(data =>
    {
        license.Id = NewId("l");
        data.Licenses.Add(license);
        return license;
    });

    /// <summary>Adds the code that extends <paramref name="oldId"/> and links the two.</summary>
    public License AddExtension(string oldId, License license) => Change(data =>
    {
        var old = data.Licenses.FirstOrDefault(l => l.Id == oldId) ?? throw new HubError("Diese Lizenz gibt es nicht mehr.");
        license.Id       = NewId("l");
        license.Replaces = old.Id;
        old.ReplacedBy   = license.Id;
        data.Licenses.Add(license);
        return license;
    });

    public License UpdateLicense(string id, decimal? price, bool paid, string? note) => Change(data =>
    {
        var license = data.Licenses.FirstOrDefault(l => l.Id == id) ?? throw new HubError("Diese Lizenz gibt es nicht mehr.");
        license.Price = price;
        license.Paid  = paid;
        license.Note  = note?.Trim() ?? "";
        return license;
    });

    public void DeleteLicense(string id) => Change(data =>
    {
        var license = data.Licenses.FirstOrDefault(l => l.Id == id) ?? throw new HubError("Diese Lizenz gibt es nicht mehr.");
        // Loosen the links, so no code points at one that is gone
        foreach (var other in data.Licenses)
        {
            if (other.ReplacedBy == id) other.ReplacedBy = null;
            if (other.Replaces == id) other.Replaces = null;
        }
        data.Licenses.Remove(license);
        return true;
    });

    /// <summary>The follow-up for this code is done - sent, or deliberately skipped.</summary>
    public void MarkFollowUp(string licenseId, bool skipped) => Change(data =>
    {
        var license = data.Licenses.FirstOrDefault(l => l.Id == licenseId) ?? throw new HubError("Diese Lizenz gibt es nicht mehr.");
        license.FollowUp        = DateTimeOffset.Now;
        license.FollowUpSkipped = skipped;
        return true;
    });

    public MailRecord AddMail(MailRecord mail) => Change(data =>
    {
        mail.Id = NewId("m");
        data.Mails.Add(mail);
        return mail;
    });

    /// <summary>Adds what a backup has and the register lacks; nothing is overwritten.</summary>
    public (int Customers, int Licenses) Merge(RegisterData other) => Change(data =>
    {
        int customers = 0, licenses = 0;
        foreach (var c in other.Customers.Where(c => data.Customers.All(x => x.Id != c.Id)))
        {
            data.Customers.Add(c);
            customers++;
        }
        foreach (var l in other.Licenses.Where(l => data.Licenses.All(x => x.Id != l.Id)))
        {
            data.Licenses.Add(l);
            licenses++;
        }
        foreach (var m in other.Mails.Where(m => data.Mails.All(x => x.Id != m.Id)))
            data.Mails.Add(m);
        return (customers, licenses);
    });

    private T Change<T>(Func<RegisterData, T> change)
    {
        lock (_gate)
        {
            var result = change(_data);
            Json.Write(_paths.RegisterFile, _data);
            return result;
        }
    }

    private static void Apply(Customer customer, CustomerFields fields)
    {
        customer.Name        = RequireName(fields.Name);
        customer.Email       = fields.Email?.Trim() ?? "";
        customer.Note        = fields.Note?.Trim() ?? "";
        customer.Language    = fields.Language == "en" ? "en" : "de";
        customer.MailConsent = fields.MailConsent;
        if (customer.Email.Length > 0 && !Settings.IsMailAddress(customer.Email))
            throw new HubError($"„{customer.Email}“ ist keine E-Mail-Adresse.");
    }

    private static Customer Find(RegisterData data, string id) =>
        data.Customers.FirstOrDefault(c => c.Id == id) ?? throw new HubError("Diesen Kunden gibt es nicht mehr.");

    private static string RequireName(string? name)
    {
        string trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0) throw new HubError("Der Kunde braucht einen Namen.");
        return trimmed;
    }

    private static string NewId(string prefix) => prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();

    /// <summary>The register for Excel: semicolons and a BOM, as German Excel expects.</summary>
    public static byte[] Csv(RegisterData data, HubConfig config)
    {
        var german = CultureInfo.GetCultureInfo("de-DE");
        var sb = new StringBuilder();
        sb.Append("Kunde;E-Mail;Produkt;Geräte-ID;Ausgestellt;Gültig bis;Test;Preis;Bezahlt;Notiz;Code\r\n");

        foreach (var l in data.Licenses.OrderBy(l => l.Issued))
        {
            var customer = data.Customers.FirstOrDefault(c => c.Id == l.CustomerId);
            string product = config.Projects.FirstOrDefault(p => p.Id == l.Product)?.Name ?? l.Product;
            Row(sb, customer?.Name, customer?.Email, product, l.MachineId, l.Issued.ToString("dd.MM.yyyy HH:mm", german),
                l.End?.ToString("dd.MM.yyyy", german) ?? "unbegrenzt", l.Trial ? "ja" : "nein",
                l.Price?.ToString("0.00", german), l.Paid ? "ja" : "nein", l.Note, l.Code);
        }
        foreach (var c in data.Customers.Where(c => data.Licenses.All(l => l.CustomerId != c.Id)))
            Row(sb, c.Name, c.Email, "", "", "", "", "", "", "", c.Note, "");

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static void Row(StringBuilder sb, params string?[] cells)
    {
        sb.Append(string.Join(";", cells.Select(Cell))).Append("\r\n");
    }

    private static string Cell(string? value)
    {
        string v = value ?? "";
        // A cell starting like a formula would be run by Excel (a code can start with "+")
        if (v.Length > 0 && "=+-@".Contains(v[0])) v = "'" + v;
        return v.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}

public sealed class RegisterData
{
    public List<Customer> Customers { get; set; } = new();
    public List<License> Licenses { get; set; } = new();
    public List<MailRecord> Mails { get; set; } = new();
}

public sealed class Customer
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Note { get; set; } = "";
    /// <summary>"de" or "en" - the language of mails to this customer.</summary>
    public string Language { get; set; } = "de";
    /// <summary>May the hub suggest follow-up mails ("did you like the trial?") for this customer.</summary>
    public bool MailConsent { get; set; } = true;
    public DateTimeOffset Created { get; set; }
}

public sealed class License
{
    public string Id { get; set; } = "";
    public string? CustomerId { get; set; }
    public string Product { get; set; } = "";
    public string MachineId { get; set; } = "";
    public string Code { get; set; } = "";
    /// <summary>Which key signed it - a code dies with its key.</summary>
    public string KeyFingerprint { get; set; } = "";
    public DateTimeOffset Issued { get; set; }
    /// <summary>The last day the code works; null = no end.</summary>
    public DateOnly? End { get; set; }
    public bool Trial { get; set; }
    public decimal? Price { get; set; }
    public bool Paid { get; set; }
    public string Note { get; set; } = "";
    /// <summary>The code this one extends, and the one that extended it.</summary>
    public string? Replaces { get; set; }
    public string? ReplacedBy { get; set; }
    /// <summary>When the follow-up mail went out - or was skipped.</summary>
    public DateTimeOffset? FollowUp { get; set; }
    public bool FollowUpSkipped { get; set; }
}

public sealed class MailRecord
{
    public string Id { get; set; } = "";
    public string? CustomerId { get; set; }
    public string? LicenseId { get; set; }
    /// <summary>code | followup | extension | test</summary>
    public string Kind { get; set; } = "";
    public string To { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateTimeOffset Sent { get; set; }
}

public sealed record CustomerFields(string Name, string? Email, string? Note, string? Language, bool MailConsent);
