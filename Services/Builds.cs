using System.Diagnostics;
using System.Text;

namespace PluginHub.Services;

/// <summary>
/// Builds plug-ins the way Rhino needs them - dotnet build -c Release, one after the other -
/// and keeps the output for the page. A release runs through here too, as a job with more
/// steps. Refuses while Rhino holds a .rhp: the compile would run, and then the copy onto the
/// .rhp would fail.
/// </summary>
public sealed class Builds
{
    private readonly HubPaths _paths;
    private readonly Config _config;
    private readonly object _gate = new();
    private BuildJob? _current;

    public Builds(HubPaths paths, Config config)
    {
        _paths  = paths;
        _config = config;
    }

    public BuildJob? Current
    {
        get { lock (_gate) return _current; }
    }

    public bool Running
    {
        get { lock (_gate) return _current is { IsFinished: false }; }
    }

    /// <summary>Test builds: the version from Version.props with "-test".</summary>
    public BuildJob Start(IReadOnlyList<string>? ids)
    {
        var projects = (ids ?? Array.Empty<string>()).Distinct().Select(_config.Project).ToList();
        if (projects.Count == 0) throw new HubError("Wähl mindestens ein Plugin.");
        var other = projects.FirstOrDefault(p => !p.IsPlugin || p.Project == null || p.Output == null);
        if (other != null) throw new HubError($"{other.Name} lässt sich nicht bauen – es ist kein Plugin.");

        return StartJob(projects, null, job =>
        {
            foreach (var p in projects) Build(job, p, release: false);
        });
    }

    /// <summary>
    /// Runs <paramref name="work"/> as a job whose lines the page follows. An exception ends
    /// the job as failed, with its message as the last line.
    /// </summary>
    public BuildJob StartJob(List<ProjectDef> projects, string? title, Action<BuildJob> work)
    {
        BuildJob job;
        lock (_gate)
        {
            if (_current is { IsFinished: false }) throw new HubError("Es läuft schon ein Build.");

            var locked = projects.Where(p => p.Output != null && Files.IsLocked(_paths.Resolve(p.Output))).Select(p => p.Name).ToList();
            if (locked.Count > 0)
                throw new HubError($"Rhino hat {string.Join(" und ", locked)} geladen und hält die .rhp fest. " +
                                   "Schließ Rhino, dann klappt der Build.");

            job = _current = new BuildJob(projects.Select(p => p.Id).ToList(), title);
        }

        _ = Task.Run(() =>
        {
            try
            {
                work(job);
            }
            catch (Exception ex)
            {
                job.Add("Fehler: " + ex.Message);
                job.Fail();
                if (ex is not HubError) Log.Error($"Job {title}: {ex}");
            }
            finally
            {
                job.Finish();
                Log.Info($"{title ?? "Build " + string.Join(", ", job.Projects)}: {job.Status}");
            }
        });
        return job;
    }

    /// <summary>
    /// One dotnet build; a release build passes HubRelease=true, so Version.props leaves out "-test".
    /// </summary>
    public bool Build(BuildJob job, ProjectDef p, bool release)
    {
        job.Add($"── {p.Name}{(release ? " (Release)" : "")} ──");
        int code;
        try { code = Dotnet(job, _paths.Resolve(p.Project!), release); }
        catch (Exception ex)
        {
            job.Add("Fehler: " + ex.Message);
            code = -1;
        }

        string rhp = _paths.Resolve(p.Output!);
        DateTime? time = File.Exists(rhp) ? File.GetLastWriteTime(rhp) : null;
        job.AddResult(new BuildResult(p.Id, p.Name, code == 0,
            code == 0 ? "Gebaut" : $"Fehlgeschlagen (Code {code})", time));
        return code == 0;
    }

    private static int Dotnet(BuildJob job, string csproj, bool release)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
            WorkingDirectory       = Path.GetDirectoryName(csproj)!
        };
        // -nodeReuse:false: no MSBuild helpers left running after the build
        foreach (string a in new[] { "build", csproj, "-c", "Release", "-nologo", "-v:minimal", "-nodeReuse:false" })
            psi.ArgumentList.Add(a);
        if (release) psi.ArgumentList.Add("-p:HubRelease=true");
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";

        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) job.Add(e.Data); };
        p.ErrorDataReceived  += (_, e) => { if (e.Data != null) job.Add(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        if (!p.WaitForExit(600_000))
        {
            try { p.Kill(true); } catch { }
            job.Add("Abgebrochen: länger als zehn Minuten.");
            return -1;
        }
        p.WaitForExit();   // lets the last output lines arrive
        return p.ExitCode;
    }
}

public sealed class BuildJob
{
    private const int MaxLines = 5000;

    private readonly object _gate = new();
    private readonly List<string> _lines = new();
    private readonly List<BuildResult> _results = new();
    private DateTimeOffset? _finished;
    private bool _failed;

    public BuildJob(List<string> projects, string? title)
    {
        Projects = projects;
        Title    = title;
    }

    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public List<string> Projects { get; }
    /// <summary>What the dock shows while it runs - "Release Laser Create 1.0.0"; null for plain builds.</summary>
    public string? Title { get; }
    public DateTimeOffset Started { get; } = DateTimeOffset.Now;

    public bool IsFinished
    {
        get { lock (_gate) return _finished != null; }
    }

    public string Status
    {
        get { lock (_gate) return StatusUnlocked(); }
    }

    public void Add(string line)
    {
        lock (_gate)
            if (_lines.Count < MaxLines) _lines.Add(line);
    }

    public void AddResult(BuildResult result)
    {
        lock (_gate) _results.Add(result);
    }

    public void Fail()
    {
        lock (_gate) _failed = true;
    }

    public void Finish()
    {
        lock (_gate) _finished = DateTimeOffset.Now;
    }

    /// <summary>The job for the page - output lines from <paramref name="from"/> on.</summary>
    public BuildView View(int from)
    {
        lock (_gate)
            return new BuildView(Id, Projects, Title, StatusUnlocked(), Started, _finished,
                from, _lines.Skip(Math.Max(0, from)).ToList(), _lines.Count, _results.ToList());
    }

    private string StatusUnlocked() =>
        _finished == null ? "running" : !_failed && _results.All(r => r.Ok) ? "ok" : "failed";
}

public sealed record BuildResult(string Project, string Name, bool Ok, string Message, DateTime? RhpTime);

public sealed record BuildView(string Id, List<string> Projects, string? Title, string Status, DateTimeOffset Started,
    DateTimeOffset? Finished, int From, List<string> Lines, int Total, List<BuildResult> Results);
