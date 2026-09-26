using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace PluginHub.Services;

/// <summary>
/// Rhino packages (.yak) - how a plug-in is installed through Rhino's own Package Manager.
/// Yak.exe comes with Rhino 8. A release builds one next to the ZIP; uploading it to
/// McNeel's package server is a click of its own, after signing in to the Rhino account
/// once. Uploaded, a package is public: anyone finds it in the Package Manager, and the
/// plug-in still asks for its activation code.
///
/// The package holds the built files (.rhp, .rui), the guide and licence terms of the
/// customer package, the plug-in's icon and the manifest - author is the seller named in
/// the licence terms.
/// </summary>
public sealed class YakPackages
{
    private readonly HubPaths _paths;
    private readonly Builds _builds;
    private readonly object _gate = new();

    public YakPackages(HubPaths paths, Builds builds)
    {
        _paths  = paths;
        _builds = builds;
    }

    /// <summary>Hub\packages.json: which release went to the Package Manager when.</summary>
    private string PushFile => Path.Combine(_paths.DataDir, "packages.json");

    /// <summary>Yak.exe: PLUGINHUB_YAK for tests (a stand-in), otherwise Rhino 8's, otherwise Rhino 9's.</summary>
    public static string? Exe()
    {
        if (Environment.GetEnvironmentVariable("PLUGINHUB_YAK") is { Length: > 0 } test) return test;
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (string rhino in new[] { "Rhino 8", "Rhino 9", "Rhino 9 WIP" })
        {
            string exe = Path.Combine(programs, rhino, "System", "Yak.exe");
            if (File.Exists(exe)) return exe;
        }
        return null;
    }

    /// <summary>Yak keeps the token of the Rhino account in yak.yml once `yak login` ran.</summary>
    public static bool SignedIn =>
        File.Exists(Environment.GetEnvironmentVariable("PLUGINHUB_YAK_TOKEN") is { Length: > 0 } test ? test
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "McNeel", "yak.yml"));

    public static string PackageName(ProjectDef p) => p.PackageName ?? p.Name.Replace(" ", "");

    // ── Building ───────────────────────────────────────────────────────────

    /// <summary>
    /// Builds &lt;name&gt;-&lt;version&gt;-rh8_nn-win.yak into <paramref name="folder"/>. The "rh8_nn" is
    /// the Rhino version the plug-in was built against - Yak reads it from the .rhp.
    /// </summary>
    public PackageFile Build(ProjectDef p, string version, string author,
        IEnumerable<(string Name, byte[] Bytes)> docs, string folder, Action<string> say)
    {
        string exe = Exe() ?? throw new HubError("Yak.exe fehlt – es kommt mit Rhino 8 (Rhino 8\\System\\Yak.exe).");
        if (string.IsNullOrWhiteSpace(author))
            throw new HubError("Für das Rhino-Paket fehlt der Anbieter – trag ihn unter Einstellungen ein.");

        var built = p.Package.Select(_paths.Resolve).ToList();
        var missing = built.Where(f => !File.Exists(f)).ToList();
        if (missing.Count > 0) throw new HubError("Für das Rhino-Paket fehlt: " + string.Join(", ", missing.Select(_paths.Relative)));

        string stage = Path.Combine(Path.GetTempPath(), "PluginHub-yak-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(stage);
        try
        {
            foreach (string file in built) File.Copy(file, Path.Combine(stage, Path.GetFileName(file)));
            foreach (var (name, text) in docs) File.WriteAllBytes(Path.Combine(stage, name), text);

            string icon = Path.Combine(_paths.Resolve(p.Folder), "icons", "package.png");
            bool hasIcon = File.Exists(icon);
            if (hasIcon) File.Copy(icon, Path.Combine(stage, "icon.png"));

            File.WriteAllText(Path.Combine(stage, "manifest.yml"), Manifest(p, version, author, hasIcon), new UTF8Encoding(false));

            var (code, output) = Run(exe, stage, TimeSpan.FromMinutes(2), "build", "--platform", "win");
            foreach (string line in output.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).TakeLast(12))
                say("  " + line);
            string? made = Directory.GetFiles(stage, "*.yak").FirstOrDefault();
            if (code != 0 || made == null)
                throw new HubError("Yak konnte das Rhino-Paket nicht bauen (Code " + code + ").");

            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, Path.GetFileName(made));
            File.Copy(made, target, overwrite: true);
            byte[] bytes = File.ReadAllBytes(target);
            return new PackageFile(Path.GetFileName(target), bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
        finally
        {
            try { Directory.Delete(stage, recursive: true); } catch { }
        }
    }

    /// <summary>manifest.yml - text in quotes, the description as a folded block, so nothing in it can break the YAML.</summary>
    private static string Manifest(ProjectDef p, string version, string author, bool icon)
    {
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("name: ").Append(PackageName(p)).Append('\n');
        sb.Append("version: ").Append(version).Append('\n');
        sb.Append("authors:\n- ").Append(Quote(author)).Append('\n');
        sb.Append("description: >-\n  ").Append((p.PackageDescription ?? p.Note ?? p.Name).Replace("\r", "").Replace("\n", " ")).Append('\n');
        if (p.PackageKeywords.Count > 0)
        {
            sb.Append("keywords:\n");
            foreach (string k in p.PackageKeywords) sb.Append("- ").Append(Quote(k)).Append('\n');
        }
        if (icon) sb.Append("icon: icon.png\n");
        return sb.ToString();
    }

    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    // ── A probe of the current build ───────────────────────────────────────

    /// <summary>
    /// A package of the build as it is now (x.y.z-test), into the plug-in's bin\Release\package -
    /// to see what a release would ship. Nothing is uploaded, nothing committed.
    /// </summary>
    public BuildJob Probe(ProjectDef p, string author, Func<string, List<(string Name, byte[] Bytes)>> docs)
    {
        if (!p.IsPlugin || p.Output == null) throw new HubError($"{p.Name} ist kein Plugin.");
        string rhp = _paths.Resolve(p.Output);
        if (!File.Exists(rhp)) throw new HubError($"{p.Name} ist noch nicht gebaut.");
        string version = FileVersionInfo.GetVersionInfo(rhp).ProductVersion ?? "0.0.0-test";
        string folder = Path.Combine(Path.GetDirectoryName(rhp)!, "package");

        return _builds.StartJob(new List<ProjectDef>(), $"Probepaket {p.Name} {version}", job =>
        {
            job.Add($"Rhino-Paket aus dem aktuellen Build ({version}) bauen");
            var file = Build(p, version, author.Length > 0 ? author : "(Anbieter fehlt noch)", docs(version), folder, job.Add);
            job.Add($"Fertig: {_paths.Relative(Path.Combine(folder, file.Name))} – {file.Size / 1024} KB");
            job.Add("Eine .yak ist ein ZIP: umbenannt in .zip zeigt der Explorer, was drin ist.");
            Shell.Select(Path.Combine(folder, file.Name));
        });
    }

    // ── Signing in and uploading ───────────────────────────────────────────

    /// <summary>`yak login`: the browser opens for the Rhino account; Yak keeps the token afterwards.</summary>
    public BuildJob Login()
    {
        string exe = Exe() ?? throw new HubError("Yak.exe fehlt – es kommt mit Rhino 8.");
        return _builds.StartJob(new List<ProjectDef>(), "Bei Rhino anmelden", job =>
        {
            job.Add("Der Browser öffnet die Anmeldung bei Rhino Accounts – melde dich dort an und erlaube Yak den Zugriff.");
            var (code, output) = Run(exe, _paths.Root, TimeSpan.FromMinutes(10), "login");
            foreach (string line in output.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0 && !l.Contains("token", StringComparison.OrdinalIgnoreCase)).TakeLast(8))
                job.Add("  " + line);
            if (code != 0 || !SignedIn) throw new HubError("Die Anmeldung ist nicht zustande gekommen.");
            job.Add("Angemeldet – jetzt lässt sich ein Paket hochladen.");
        });
    }

    /// <summary>Uploads a release's package to McNeel's server - after that it is public.</summary>
    public BuildJob Push(ProjectDef p, string version, string file)
    {
        string exe = Exe() ?? throw new HubError("Yak.exe fehlt – es kommt mit Rhino 8.");
        if (!SignedIn) throw new HubError("Melde dich zuerst bei Rhino an (Knopf „Bei Rhino anmelden“).");
        if (!File.Exists(file)) throw new HubError("Das Rhino-Paket dieses Releases fehlt.");
        if (Pushed(p.Id, version) != null) throw new HubError($"{p.Name} {version} ist schon im Package Manager.");

        return _builds.StartJob(new List<ProjectDef>(), $"{p.Name} {version} zum Package Manager", job =>
        {
            job.Add($"{Path.GetFileName(file)} hochladen");
            var (code, output) = Run(exe, Path.GetDirectoryName(file)!, TimeSpan.FromMinutes(5), "push", file);
            foreach (string line in output.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).TakeLast(12))
                job.Add("  " + line);
            if (code != 0) throw new HubError("Yak hat das Paket nicht angenommen (Code " + code + ").");

            lock (_gate)
            {
                var list = Json.Read<List<PackagePush>>(PushFile) ?? new List<PackagePush>();
                list.Add(new PackagePush(p.Id, version, Path.GetFileName(file), DateTimeOffset.Now));
                Json.Write(PushFile, list);
            }
            job.Add($"Im Package Manager: „{PackageName(p)}“ {version} – in Rhino unter Tools → Package Manager.");
            Log.Info($"Pushed {Path.GetFileName(file)}");
        });
    }

    public PackagePush? Pushed(string product, string version)
    {
        lock (_gate)
            return (Json.Read<List<PackagePush>>(PushFile) ?? new List<PackagePush>())
                .LastOrDefault(r => r.Product == product && r.Version == version);
    }

    // ── The CLI ────────────────────────────────────────────────────────────

    private static (int Code, string Output) Run(string exe, string dir, TimeSpan timeout, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            WorkingDirectory       = dir,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8
        };
        foreach (string a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new HubError("Yak ließ sich nicht starten.");
        var output = p.StandardOutput.ReadToEndAsync();
        var error  = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { p.Kill(true); } catch { }
            throw new HubError("Yak hat nicht geantwortet.");
        }
        p.WaitForExit();
        return (p.ExitCode, output.Result + (error.Result.Length > 0 ? "\n" + error.Result : ""));
    }
}

public sealed record PackageFile(string Name, long Size, string Sha256);

/// <summary>One upload to the Package Manager, in Hub\packages.json.</summary>
public sealed record PackagePush(string Product, string Version, string File, DateTimeOffset Pushed);
