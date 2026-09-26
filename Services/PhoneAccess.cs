using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace PluginHub.Services;

/// <summary>
/// The hub on the phone, through Tailscale. `tailscale serve` takes HTTPS requests from the
/// user's own devices at https://&lt;pc&gt;.&lt;tailnet&gt;.ts.net and hands them to the hub on this
/// PC, saying which Tailscale account sent them. The hub lets them in only while phone access is
/// on, and only for the account it was switched on with (<see cref="Admits"/>).
///
/// Nothing is opened to the internet: Serve is reachable only inside the user's tailnet - the
/// public variant, Funnel, is refused - and no port is opened on the router or in the firewall.
/// While it is on, the hub starts with Windows and stays running, so the phone finds it.
/// </summary>
public sealed class PhoneAccess
{
    // The value in HKCU\...\Run that starts the hub with Windows
    private const string RunKey   = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Plugin Hub";

    private static readonly TimeSpan InfoAge = TimeSpan.FromMinutes(1);

    private readonly HubPaths _paths;
    private readonly HubPort  _port;
    private readonly object   _gate = new();
    private PhoneState _state;

    private TailscaleInfo? _info;
    private DateTime _infoAt = DateTime.MinValue;
    private int _refreshing;

    public PhoneAccess(HubPaths paths, HubPort port)
    {
        _paths = paths;
        _port  = port;
        try { _state = Json.Read<PhoneState>(StateFile) ?? new PhoneState(); }
        catch (Exception ex)
        {
            Log.Error("phone.json unreadable: " + ex.Message);
            _state = new PhoneState();
        }
    }

    private string StateFile => Path.Combine(_paths.PrivateDir, "phone.json");

    /// <summary>On: the hub takes requests from the phone and keeps running.</summary>
    public bool Enabled => _state.Enabled;

    // ── Who may come in ────────────────────────────────────────────────────

    /// <summary>
    /// For a request that came through Tailscale Serve: true when phone access is on, the page
    /// was asked for under this PC's Tailscale name, and the account Tailscale vouches for is the
    /// one phone access was switched on with. Tailscale deletes any account header a device sends
    /// and sets it itself, so it cannot be made up; it is missing for Funnel and tagged devices.
    /// </summary>
    public bool Admits(string? forwardedHost, string? login, bool funnel)
    {
        var s = _state;
        if (!s.Enabled || funnel || string.IsNullOrEmpty(s.Host) || string.IsNullOrEmpty(s.Login)) return false;
        if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(forwardedHost)) return false;

        string host = forwardedHost.Split(':')[0].TrimEnd('.');
        return string.Equals(host, s.Host, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(MailParser.DecodeWords(login).Trim(), s.Login, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>This PC's name in the tailnet, when phone access is on.</summary>
    public string? Host => _state.Enabled && !string.IsNullOrEmpty(_state.Host) ? _state.Host : null;

    /// <summary>The address the phone opens, when phone access is on.</summary>
    public string? Origin => Host is string host ? "https://" + host : null;

    // ── Switching ──────────────────────────────────────────────────────────

    public PhoneView View()
    {
        var info = Tailscale(refresh: true);
        return Describe(info, null);
    }

    /// <summary>
    /// Turns phone access on: Tailscale Serve for the hub's port, the address and account
    /// remembered, and the hub started with Windows. When the tailnet has not allowed Serve yet,
    /// Tailscale asks for it once in the browser - the view then carries that link, and the
    /// user switches on again afterwards.
    /// </summary>
    public PhoneView TurnOn()
    {
        var info = Tailscale(refresh: true);
        if (!info.Installed)
            throw new HubError("Tailscale ist auf diesem PC nicht installiert. Installiere es von tailscale.com/download und melde dich an.");
        if (info.State != "Running")
            throw new HubError("Tailscale läuft nicht oder ist nicht angemeldet. Öffne Tailscale auf diesem PC und melde dich an.");
        if (string.IsNullOrEmpty(info.Host) || string.IsNullOrEmpty(info.Login))
            throw new HubError("Tailscale nennt keinen Gerätenamen oder kein Konto. Ist MagicDNS im Tailnet an?");

        var (ok, output, approval) = Serve("--bg", "--https=443", $"http://127.0.0.1:{_port.Value}");
        if (approval != null)
            return Describe(Tailscale(refresh: true), approval);
        if (!ok)
            throw new HubError("Tailscale Serve ließ sich nicht einschalten: " + Short(output));

        lock (_gate)
        {
            _state = new PhoneState
            {
                Enabled = true,
                Host    = info.Host,
                Login   = info.Login,
                Since   = DateTimeOffset.Now
            };
            Json.Write(StateFile, _state);
        }
        SetAutostart(true);
        Log.Info($"Phone access on: https://{info.Host} for {info.Login}");
        return Describe(Tailscale(refresh: true), null);
    }

    /// <summary>Turns it off: Serve for the hub stopped, no autostart, the address forgotten as a way in.</summary>
    public PhoneView TurnOff()
    {
        var info = Tailscale(refresh: true);
        string? problem = null;
        if (info.Installed && info.ServesHub)
        {
            var (ok, output, _) = Serve("--https=443", "off");
            if (!ok) problem = "Tailscale Serve ließ sich nicht ausschalten: " + Short(output);
        }

        lock (_gate)
        {
            _state = new PhoneState { Enabled = false, Host = _state.Host, Login = _state.Login };
            Json.Write(StateFile, _state);
        }
        SetAutostart(false);
        Log.Info("Phone access off");

        if (problem != null) throw new HubError(problem + " Der Hub lässt das Handy trotzdem nicht mehr herein.");
        return Describe(Tailscale(refresh: true), null);
    }

    /// <summary>
    /// For the notices on the overview: the last known Tailscale state, fetched again in the
    /// background once it is older than a minute - the overview must never wait for the CLI.
    /// Null until it was fetched once.
    /// </summary>
    public TailscaleInfo? CachedInfo()
    {
        if (DateTime.UtcNow - _infoAt > InfoAge && Interlocked.Exchange(ref _refreshing, 1) == 0)
            _ = Task.Run(() =>
            {
                try { Tailscale(refresh: true); }
                finally { Interlocked.Exchange(ref _refreshing, 0); }
            });
        return _info;
    }

    private PhoneView Describe(TailscaleInfo info, string? approval)
    {
        var s = _state;
        return new PhoneView(
            s.Enabled, s.Enabled ? "https://" + s.Host : null, s.Login, s.Since,
            info.Installed, info.State, info.Host, info.Login, info.ServesHub, info.CertReady,
            Autostart(), approval, TestHub());
    }

    // ── Tailscale ──────────────────────────────────────────────────────────

    /// <summary>What `tailscale status` and `tailscale serve status` say about this PC.</summary>
    private TailscaleInfo Tailscale(bool refresh)
    {
        if (!refresh && _info != null) return _info;

        var info = new TailscaleInfo();
        string? exe = TailscaleExe();
        if (exe != null)
        {
            var (code, output) = Run(exe, TimeSpan.FromSeconds(8), "status", "--json");
            info.Installed = code != -2;
            if (code == 0 || output.TrimStart().StartsWith('{'))
                ReadStatus(output, info);

            if (info.State == "Running")
            {
                var (serveCode, serveOutput) = Run(exe, TimeSpan.FromSeconds(8), "serve", "status", "--json");
                if (serveCode == 0) info.ServesHub = ProxiesTo(serveOutput, _port.Value);
            }
        }

        _info   = info;
        _infoAt = DateTime.UtcNow;
        return info;
    }

    private static void ReadStatus(string json, TailscaleInfo info)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            info.State = Str(root, "BackendState");

            if (root.TryGetProperty("Self", out var self))
            {
                info.Host = Str(self, "DNSName")?.TrimEnd('.');
                if (self.TryGetProperty("UserID", out var uid) && root.TryGetProperty("User", out var users) &&
                    users.ValueKind == JsonValueKind.Object &&
                    users.TryGetProperty(uid.ToString(), out var user))
                    info.Login = Str(user, "LoginName");
            }

            // Certificates exist for the names listed here - without them Serve cannot do HTTPS yet
            if (root.TryGetProperty("CertDomains", out var certs) && certs.ValueKind == JsonValueKind.Array)
                info.CertReady = certs.EnumerateArray().Any(c => string.Equals(c.GetString()?.TrimEnd('.'), info.Host,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException) { /* an old or broken CLI - reported as not running */ }
    }

    /// <summary>True when some Serve handler hands requests on to this port on this PC.</summary>
    private static bool ProxiesTo(string json, int port)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Proxies(doc.RootElement).Any(p =>
                p.Contains($"127.0.0.1:{port}", StringComparison.Ordinal) ||
                p.Contains($"localhost:{port}", StringComparison.OrdinalIgnoreCase) ||
                p.Trim() == port.ToString());
        }
        catch (JsonException) { return false; }

        static IEnumerable<string> Proxies(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name == "Proxy" && p.Value.ValueKind == JsonValueKind.String) yield return p.Value.GetString()!;
                    foreach (var inner in Proxies(p.Value)) yield return inner;
                }
            else if (e.ValueKind == JsonValueKind.Array)
                foreach (var item in e.EnumerateArray())
                    foreach (var inner in Proxies(item)) yield return inner;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>
    /// `tailscale serve …`. When the tailnet has not allowed Serve (or HTTPS) yet, the CLI
    /// prints a login.tailscale.com link and waits - that link comes back, and the wait ends.
    /// </summary>
    private (bool Ok, string Output, string? Approval) Serve(params string[] args)
    {
        string exe = TailscaleExe() ?? throw new HubError("Tailscale ist auf diesem PC nicht installiert.");

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8
        };
        psi.ArgumentList.Add("serve");
        foreach (string a in args) psi.ArgumentList.Add(a);

        var output   = new StringBuilder();
        string? link = null;
        using var found = new ManualResetEventSlim();

        void Line(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            lock (output) output.AppendLine(text.Trim());
            int at = text.IndexOf("https://login.tailscale.com/", StringComparison.OrdinalIgnoreCase);
            if (at >= 0 && link == null)
            {
                link = text[at..].Trim().Split(' ')[0];
                found.Set();
            }
        }

        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) => Line(e.Data);
        p.ErrorDataReceived  += (_, e) => Line(e.Data);
        try { p.Start(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw new HubError("Tailscale ließ sich nicht starten: " + ex.Message);
        }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        // Done, or waiting for the user to allow Serve in the browser - whichever comes first
        var exited = Task.Run(() => p.WaitForExit(30_000));
        WaitHandle.WaitAny(new[] { found.WaitHandle, ((IAsyncResult)exited).AsyncWaitHandle }, 31_000);

        if (link != null || !p.HasExited)
        {
            try { p.Kill(true); } catch { }
            string text;
            lock (output) text = output.ToString();
            if (link == null) Log.Error("tailscale serve did not finish: " + text);
            return (false, link == null ? "Tailscale hat nicht geantwortet." : text, link);
        }

        p.WaitForExit();
        lock (output) return (p.ExitCode == 0, output.ToString(), null);
    }

    /// <summary>Runs the CLI; -2 = not there, -1 = no answer in time.</summary>
    private static (int Code, string Output) Run(string exe, TimeSpan timeout, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding  = Encoding.UTF8
            };
            foreach (string a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync();
            var error  = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { p.Kill(true); } catch { }
                return (-1, "");
            }
            p.WaitForExit();
            return (p.ExitCode, output.Result.Length > 0 ? output.Result : error.Result);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return (-2, "");
        }
    }

    /// <summary>
    /// tailscale.exe: PLUGINHUB_TAILSCALE for tests (a stand-in that answers like the CLI),
    /// otherwise where the installer puts it, otherwise on the PATH.
    /// </summary>
    private static string? TailscaleExe()
    {
        if (Environment.GetEnvironmentVariable("PLUGINHUB_TAILSCALE") is { Length: > 0 } test)
            return test;

        string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe");
        if (File.Exists(installed)) return installed;

        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(dir.Trim(), "tailscale.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    private static string Short(string output)
    {
        string text = output.Trim();
        return text.Length > 400 ? text[..400] + " …" : text.Length > 0 ? text : "keine Ausgabe";
    }

    // ── Starting with Windows ──────────────────────────────────────────────

    /// <summary>
    /// A test hub (PLUGINHUB_PRIVATE_DIR) never touches the real autostart: it writes the
    /// command to PLUGINHUB_AUTOSTART_FILE instead, or nowhere.
    /// </summary>
    private static bool TestHub() => Environment.GetEnvironmentVariable("PLUGINHUB_PRIVATE_DIR") is { Length: > 0 };

    private static string? TestAutostartFile() =>
        Environment.GetEnvironmentVariable("PLUGINHUB_AUTOSTART_FILE") is { Length: > 0 } f ? f : null;

    private static string Command() => $"\"{Environment.ProcessPath}\" --no-browser";

    private static bool Autostart()
    {
        if (TestHub()) return TestAutostartFile() is string f && File.Exists(f);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string value && value.Length > 0;
        }
        catch { return false; }
    }

    private static void SetAutostart(bool on)
    {
        if (TestHub())
        {
            if (TestAutostartFile() is not string file) return;
            if (on) File.WriteAllText(file, Command());
            else if (File.Exists(file)) File.Delete(file);
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) key.SetValue(RunValue, Command());
            else if (key.GetValue(RunValue) != null) key.DeleteValue(RunValue);
        }
        catch (Exception ex)
        {
            Log.Error("Autostart: " + ex.Message);
            throw new HubError("Der Autostart ließ sich nicht " + (on ? "eintragen" : "entfernen") + ": " + ex.Message);
        }
    }
}

/// <summary>The hub's own port - for Serve to hand requests to.</summary>
public sealed record HubPort(int Value);

/// <summary>phone.json in the private folder - never in Git: it names this PC and your account.</summary>
public sealed class PhoneState
{
    public bool Enabled { get; set; }
    public string? Host { get; set; }
    public string? Login { get; set; }
    public DateTimeOffset? Since { get; set; }
}

public sealed class TailscaleInfo
{
    public bool Installed { get; set; }

    /// <summary>"Running" when connected and signed in; "NeedsLogin", "Stopped" …</summary>
    public string? State { get; set; }

    /// <summary>This PC in the tailnet: desktop-abc.tail1234.ts.net</summary>
    public string? Host { get; set; }

    /// <summary>The account signed in on this PC.</summary>
    public string? Login { get; set; }

    /// <summary>Serve hands requests on to the hub's port.</summary>
    public bool ServesHub { get; set; }

    /// <summary>The tailnet has HTTPS certificates for this PC's name.</summary>
    public bool CertReady { get; set; }
}

/// <summary>What the settings page shows. Approval: the link Tailscale wants opened once to allow Serve.</summary>
public sealed record PhoneView(
    bool Enabled, string? Address, string? Login, DateTimeOffset? Since,
    bool Installed, string? State, string? TailscaleHost, string? TailscaleLogin, bool ServesHub, bool CertReady,
    bool Autostart, string? Approval, bool TestHub);
