using System.Security.Cryptography;
using System.Text;
using RhinoKit.Licensing;

namespace PluginHub.Services;

/// <summary>
/// The private activation keys, one ECDSA P-256 pair per plug-in. Each lives in
/// keys\&lt;product&gt;.key in the private folder: the public half in the clear, the private
/// half protected with DPAPI. The private half is decrypted only for the moment it signs,
/// and leaves this process only inside a password-protected backup.
/// </summary>
public sealed class KeyStore
{
    private readonly HubPaths _paths;
    private readonly object _gate = new();

    public KeyStore(HubPaths paths)
    {
        _paths = paths;
    }

    public KeyInfo? Info(string product)
    {
        var file = Read(product);
        return file == null ? null : new KeyInfo(product, file.PublicKey, Fingerprint(file.PublicKey), file.Created);
    }

    public IEnumerable<string> Products() =>
        Directory.EnumerateFiles(_paths.KeysDir, "*.key").Select(f => Path.GetFileNameWithoutExtension(f)!);

    /// <summary>All keys in one line - changes whenever any key does (backups compare it).</summary>
    public string Fingerprints() =>
        string.Join(",", Products().OrderBy(p => p, StringComparer.Ordinal).Select(p => p + ":" + Info(p)?.Fingerprint));

    public KeyInfo Generate(string product)
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] pkcs8 = ec.ExportPkcs8PrivateKey();
        try { return Store(product, pkcs8, DateTimeOffset.Now); }
        finally { CryptographicOperations.ZeroMemory(pkcs8); }
    }

    /// <summary>The activation code for this machine id - see <see cref="ActivationCode"/>.</summary>
    public string Sign(string product, string machineId)
    {
        byte[] pkcs8 = PrivateKey(product);
        try
        {
            using var ec = ECDsa.Create();
            ec.ImportPkcs8PrivateKey(pkcs8, out _);
            return Convert.ToBase64String(ec.SignData(ActivationCode.Message(machineId), HashAlgorithmName.SHA256));
        }
        finally { CryptographicOperations.ZeroMemory(pkcs8); }
    }

    /// <summary>The private key in the clear (PKCS#8) - for signing and the encrypted backup only.</summary>
    public byte[] PrivateKey(string product)
    {
        var file = Read(product) ?? throw new HubError($"Für „{product}“ gibt es noch keinen Schlüssel.");
        return Dpapi.Unprotect(Convert.FromBase64String(file.Protected), Entropy(product));
    }

    /// <summary>Keeps a private key (a new one, or one from a backup) under DPAPI.</summary>
    public KeyInfo Store(string product, byte[] pkcs8, DateTimeOffset created)
    {
        string publicKey = PublicKeyOf(pkcs8);
        var file = new KeyFile
        {
            Product   = product,
            Created   = created,
            PublicKey = publicKey,
            Protected = Convert.ToBase64String(Dpapi.Protect(pkcs8, Entropy(product)))
        };
        lock (_gate) Json.Write(PathOf(product), file);
        return new KeyInfo(product, publicKey, Fingerprint(publicKey), created);
    }

    public static string PublicKeyOf(byte[] pkcs8)
    {
        using var ec = ECDsa.Create();
        ec.ImportPkcs8PrivateKey(pkcs8, out _);
        if (ec.KeySize != 256) throw new HubError("Das ist kein P-256-Schlüssel.");
        return Convert.ToBase64String(ec.ExportSubjectPublicKeyInfo());
    }

    /// <summary>"AB12-CD34": the start of the public key's SHA-256, to tell keys apart at a glance.</summary>
    public static string Fingerprint(string publicKeyB64)
    {
        string hex = Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(publicKeyB64)), 0, 4);
        return hex[..4] + "-" + hex[4..];
    }

    private KeyFile? Read(string product)
    {
        lock (_gate) return Json.Read<KeyFile>(PathOf(product));
    }

    private string PathOf(string product)
    {
        if (product.Length == 0 || !product.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            throw new HubError($"Ungültiger Produktname „{product}“.");
        return Path.Combine(_paths.KeysDir, product + ".key");
    }

    // Ties each protected key to its product: a key file renamed to another product won't open
    private static byte[] Entropy(string product) => Encoding.UTF8.GetBytes("PluginHub activation key " + product);
}

public sealed record KeyInfo(string Product, string PublicKey, string Fingerprint, DateTimeOffset Created);

internal sealed class KeyFile
{
    public string Product { get; set; } = "";
    public DateTimeOffset Created { get; set; }
    public string PublicKey { get; set; } = "";
    public string Protected { get; set; } = "";
}
