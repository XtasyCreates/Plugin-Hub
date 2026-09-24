using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PluginHub.Services;

/// <summary>
/// The customer register: who got which activation code, for which product and device, and
/// whether it was paid. Kept in register.json in the private folder. Every code the hub
/// makes is written here, because codes cannot be looked up anywhere else - signing the
/// same machine id twice gives two different codes, both valid.
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

    public Customer AddCustomer(string name, string? email, string? note) => Change(data =>
    {
        var customer = new Customer
        {
            Id      = NewId("k"),
            Name    = RequireName(name),
            Email   = email?.Trim() ?? "",
            Note    = note?.Trim() ?? "",
            Created = DateTimeOffset.Now
        };
        data.Customers.Add(customer);
        return customer;
    });

    /// <summary>The customer of that name (any case), or a new one.</summary>
    public Customer FindOrAddCustomer(string name, string? email) => Change(data =>
    {
        string wanted = RequireName(name);
        var existing = data.Customers.FirstOrDefault(c => string.Equals(c.Name, wanted, StringComparison.CurrentCultureIgnoreCase));
        if (existing != null)
        {
            if (existing.Email.Length == 0 && !string.IsNullOrWhiteSpace(email)) existing.Email = email.Trim();
            return existing;
        }
        var customer = new Customer { Id = NewId("k"), Name = wanted, Email = email?.Trim() ?? "", Created = DateTimeOffset.Now };
        data.Customers.Add(customer);
        return customer;
    });

    public Customer UpdateCustomer(string id, string name, string? email, string? note) => Change(data =>
    {
        var customer = Find(data, id);
        customer.Name  = RequireName(name);
        customer.Email = email?.Trim() ?? "";
        customer.Note  = note?.Trim() ?? "";
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
        if (data.Licenses.RemoveAll(l => l.Id == id) == 0) throw new HubError("Diese Lizenz gibt es nicht mehr.");
        return true;
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
        sb.Append("Kunde;E-Mail;Produkt;Geräte-ID;Ausgestellt;Preis;Bezahlt;Notiz;Code\r\n");

        foreach (var l in data.Licenses.OrderBy(l => l.Issued))
        {
            var customer = data.Customers.FirstOrDefault(c => c.Id == l.CustomerId);
            string product = config.Projects.FirstOrDefault(p => p.Id == l.Product)?.Name ?? l.Product;
            Row(sb, customer?.Name, customer?.Email, product, l.MachineId, l.Issued.ToString("dd.MM.yyyy HH:mm", german),
                l.Price?.ToString("0.00", german), l.Paid ? "ja" : "nein", l.Note, l.Code);
        }
        foreach (var c in data.Customers.Where(c => data.Licenses.All(l => l.CustomerId != c.Id)))
            Row(sb, c.Name, c.Email, "", "", "", "", "", c.Note, "");

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
}

public sealed class Customer
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Note { get; set; } = "";
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
    public decimal? Price { get; set; }
    public bool Paid { get; set; }
    public string Note { get; set; } = "";
}
