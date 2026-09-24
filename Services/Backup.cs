using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PluginHub.Services;

/// <summary>
/// The backup: every private key and the whole register in one file, encrypted with a
/// password (AES-256-GCM, key from PBKDF2-SHA256). Unlike the DPAPI-protected key files it
/// opens on any PC - with the password, and only with it.
/// </summary>
public sealed class Backup
{
    private const string Format     = "Plugin Hub backup";
    private const int    Version    = 1;
    private const int    Iterations = 600_000;
    private const int    MinLength  = 10;

    private readonly HubPaths _paths;
    private readonly KeyStore _keys;
    private readonly Register _register;
    private readonly HubState _state;

    public Backup(HubPaths paths, KeyStore keys, Register register, HubState state)
    {
        _paths    = paths;
        _keys     = keys;
        _register = register;
        _state    = state;
    }

    public BackupRecord Create(string? password)
    {
        if (password == null || password.Length < MinLength)
            throw new HubError($"Das Passwort braucht mindestens {MinLength} Zeichen.");

        var contents = new BackupContents { Created = DateTimeOffset.Now, Register = _register.Snapshot() };
        foreach (string product in _keys.Products())
        {
            byte[] pkcs8 = _keys.PrivateKey(product);
            contents.Keys.Add(new BackupKey
            {
                Product = product,
                Created = _keys.Info(product)!.Created,
                Pkcs8   = Convert.ToBase64String(pkcs8)
            });
            CryptographicOperations.ZeroMemory(pkcs8);
        }

        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(contents, Json.Options);
        string sealedText;
        try { sealedText = Seal(plain, password); }
        finally { CryptographicOperations.ZeroMemory(plain); }

        string file = Path.Combine(_paths.BackupsDir, $"Plugin Hub Backup {contents.Created:yyyy-MM-dd HHmm}.hubbackup");
        Json.WriteText(file, sealedText);

        var record = new BackupRecord
        {
            At        = contents.Created,
            File      = file,
            Keys      = _keys.Fingerprints(),
            Register  = _register.Hash(),
            Customers = contents.Register.Customers.Count,
            Licenses  = contents.Register.Licenses.Count
        };
        _state.RecordBackup(record);
        Log.Info("Backup written: " + file);
        return record;
    }

    /// <summary>
    /// Brings back keys the hub lacks and adds the register entries it lacks. A key that
    /// differs from the one already here is left alone and reported - replacing it would
    /// silently invalidate codes.
    /// </summary>
    public RestoreResult Restore(string? text, string? password)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new HubError("Wähle zuerst eine Backup-Datei.");
        byte[] plain = Open(text, password ?? "");
        BackupContents contents;
        try
        {
            contents = JsonSerializer.Deserialize<BackupContents>(plain, Json.Options)
                ?? throw new HubError("Die Backup-Datei ist leer.");
        }
        finally { CryptographicOperations.ZeroMemory(plain); }

        var result = new RestoreResult();
        foreach (var key in contents.Keys)
        {
            byte[] pkcs8 = Convert.FromBase64String(key.Pkcs8);
            try
            {
                var existing = _keys.Info(key.Product);
                if (existing == null)
                {
                    _keys.Store(key.Product, pkcs8, key.Created);
                    result.Restored.Add(key.Product);
                }
                else if (existing.PublicKey == KeyStore.PublicKeyOf(pkcs8)) result.Same.Add(key.Product);
                else result.Conflicts.Add(key.Product);
            }
            finally { CryptographicOperations.ZeroMemory(pkcs8); }
        }

        (result.Customers, result.Licenses) = _register.Merge(contents.Register);
        Log.Info($"Backup restored: keys {string.Join(",", result.Restored)}, {result.Customers} customers, {result.Licenses} licenses");
        return result;
    }

    private static string Seal(byte[] plain, string password)
    {
        byte[] salt   = RandomNumberGenerator.GetBytes(16);
        byte[] nonce  = RandomNumberGenerator.GetBytes(12);
        byte[] cipher = new byte[plain.Length];
        byte[] tag    = new byte[16];
        byte[] key    = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key);
            aes.Encrypt(nonce, plain, cipher, tag, Header());
        }
        finally { CryptographicOperations.ZeroMemory(key); }

        return JsonSerializer.Serialize(new BackupEnvelope
        {
            Format     = Format,
            Version    = Version,
            Kdf        = "PBKDF2-SHA256",
            Iterations = Iterations,
            Salt       = Convert.ToBase64String(salt),
            Nonce      = Convert.ToBase64String(nonce),
            Tag        = Convert.ToBase64String(tag),
            Data       = Convert.ToBase64String(cipher)
        }, Json.Options);
    }

    private static byte[] Open(string text, string password)
    {
        BackupEnvelope? envelope;
        try { envelope = JsonSerializer.Deserialize<BackupEnvelope>(text, Json.Options); }
        catch (JsonException) { envelope = null; }
        if (envelope?.Format != Format) throw new HubError("Das ist keine Backup-Datei des Plugin Hub.");
        if (envelope.Version > Version) throw new HubError("Dieses Backup stammt von einem neueren Hub.");
        if (envelope.Iterations is < 100_000 or > 10_000_000) throw new HubError("Die Backup-Datei ist beschädigt.");

        byte[] cipher = Convert.FromBase64String(envelope.Data);
        byte[] plain  = new byte[cipher.Length];
        byte[] key    = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Convert.FromBase64String(envelope.Salt),
            envelope.Iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key);
            aes.Decrypt(Convert.FromBase64String(envelope.Nonce), cipher, Convert.FromBase64String(envelope.Tag), plain, Header());
            return plain;
        }
        catch (CryptographicException)
        {
            throw new HubError("Das Passwort stimmt nicht, oder die Datei ist beschädigt.");
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    // Bound into the encryption, so the header cannot be swapped onto other data
    private static byte[] Header() => Encoding.UTF8.GetBytes($"{Format}/{Version}");
}

public sealed class RestoreResult
{
    public List<string> Restored { get; } = new();
    public List<string> Same { get; } = new();
    public List<string> Conflicts { get; } = new();
    public int Customers { get; set; }
    public int Licenses { get; set; }
}

internal sealed class BackupEnvelope
{
    public string Format { get; set; } = "";
    public int Version { get; set; }
    public string Kdf { get; set; } = "";
    public int Iterations { get; set; }
    public string Salt { get; set; } = "";
    public string Nonce { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Data { get; set; } = "";
}

internal sealed class BackupContents
{
    public DateTimeOffset Created { get; set; }
    public List<BackupKey> Keys { get; set; } = new();
    public RegisterData Register { get; set; } = new();
}

internal sealed class BackupKey
{
    public string Product { get; set; } = "";
    public DateTimeOffset Created { get; set; }
    public string Pkcs8 { get; set; } = "";
}

/// <summary>state.json: when the last backup was made, and of what.</summary>
public sealed class HubState
{
    private readonly HubPaths _paths;
    private readonly object _gate = new();

    public HubState(HubPaths paths)
    {
        _paths = paths;
    }

    public BackupRecord? LastBackup
    {
        get { lock (_gate) return Json.Read<StateData>(_paths.StateFile)?.LastBackup; }
    }

    public void RecordBackup(BackupRecord record)
    {
        lock (_gate)
        {
            var state = Json.Read<StateData>(_paths.StateFile) ?? new StateData();
            state.LastBackup = record;
            Json.Write(_paths.StateFile, state);
        }
    }
}

public sealed class StateData
{
    public BackupRecord? LastBackup { get; set; }
}

public sealed class BackupRecord
{
    public DateTimeOffset At { get; set; }
    public string File { get; set; } = "";
    /// <summary><see cref="KeyStore.Fingerprints"/> at the time.</summary>
    public string Keys { get; set; } = "";
    /// <summary><see cref="Register.Hash"/> at the time.</summary>
    public string Register { get; set; } = "";
    public int Customers { get; set; }
    public int Licenses { get; set; }
}
