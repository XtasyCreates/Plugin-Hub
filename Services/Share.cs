using System.Diagnostics;
using System.Text;

namespace PluginHub.Services;

/// <summary>
/// Copies of a project made to hand on. A script of the project's own puts everything another
/// PC needs into a new subfolder of its share folder - one per version - and a ZIP of it
/// beside that. The hub starts the script as a job and lists what is there; what goes into a
/// copy, and what a version is called, is the project's business.
/// </summary>
public sealed class Share
{
    private readonly HubPaths _paths;
    private readonly Config _config;
    private readonly Builds _builds;

    public Share(HubPaths paths, Config config, Builds builds)
    {
        _paths  = paths;
        _config = config;
        _builds = builds;
    }

    public BuildJob Start(string id)
    {
        var p = _config.Project(id);
        if (p.ShareScript == null || p.ShareFolder == null)
            throw new HubError($"Für {p.Name} ist kein Paket zum Weitergeben eingerichtet.");
        string script = _paths.Resolve(p.ShareScript);
        if (!File.Exists(script))
            throw new HubError($"Das Skript {p.ShareScript} gibt es nicht.");

        return _builds.StartJob(new List<ProjectDef> { p }, $"Paket {p.Name}", job =>
        {
            var before = Names(p);
            int code   = Run(job, script);
            // The script names the version - the one that was not there before is it
            string? made = State(_paths, p)?.Packages.Select(x => x.Name).FirstOrDefault(n => !before.Contains(n));
            bool ok = code == 0 && made != null;
            job.AddResult(new BuildResult(p.Id, made ?? p.Name, ok,
                ok ? "Erstellt" : code == 0 ? "Das Skript lief durch, aber es liegt kein neues Paket da" : $"Fehlgeschlagen (Code {code})", null));
        });
    }

    /// <summary>The copies there are, the newest first; null for a project that has none to hand on.</summary>
    public static ShareView? State(HubPaths paths, ProjectDef p)
    {
        if (p.ShareScript == null || p.ShareFolder == null) return null;

        string folder = paths.Resolve(p.ShareFolder);
        string shown  = p.ShareFolder.Replace('\\', '/').TrimEnd('/');
        var packages  = new List<SharePackage>();
        if (Directory.Exists(folder))
            foreach (var dir in new DirectoryInfo(folder).GetDirectories().OrderByDescending(d => d.CreationTimeUtc))
            {
                var zip = new FileInfo(dir.FullName + ".zip");
                packages.Add(new SharePackage(dir.Name, dir.CreationTime, $"{shown}/{dir.Name}",
                    zip.Exists ? $"{shown}/{zip.Name}" : null, zip.Exists ? zip.Length : null));
            }
        return new ShareView(shown, packages);
    }

    private HashSet<string> Names(ProjectDef p) =>
        (State(_paths, p)?.Packages.Select(x => x.Name) ?? Enumerable.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Windows' own PowerShell, for this one script: no profile, and the policy lifted for this run only.</summary>
    private static int Run(BuildJob job, string script)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
            WorkingDirectory       = Path.GetDirectoryName(script)!
        };
        foreach (string a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script })
            psi.ArgumentList.Add(a);
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) job.Add(e.Data); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data != null) job.Add(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(900_000))
        {
            try { process.Kill(true); } catch { }
            job.Add("Abgebrochen: länger als eine Viertelstunde.");
            return -1;
        }
        process.WaitForExit();   // lets the last output lines arrive
        return process.ExitCode;
    }
}

/// <summary><paramref name="Folder"/> and the paths of the packages are relative to the root, as "path:" links take them.</summary>
public sealed record ShareView(string Folder, List<SharePackage> Packages);

public sealed record SharePackage(string Name, DateTime Made, string Path, string? Zip, long? ZipBytes);
