using System.Text.RegularExpressions;

namespace PluginHub.Services;

/// <summary>
/// The copy of the repository outside this PC: a private repository on GitHub (or anywhere Git
/// reaches) as the remote "origin". The hub uploads only when asked - "Hochladen" - and says how
/// much is not up there yet. Signing in is Git's own business: its credential manager opens a
/// window the first time and remembers the login.
/// </summary>
public sealed class RepoBackup
{
    private const string Remote = "origin";

    private static readonly Regex HttpsUrl = new(@"^https://[A-Za-z0-9.-]+(:\d+)?(/[A-Za-z0-9._~-]+)+/?$", RegexOptions.Compiled);
    private static readonly Regex SshUrl   = new(@"^[A-Za-z0-9._-]+@[A-Za-z0-9.-]+:[A-Za-z0-9._/~-]+$", RegexOptions.Compiled);

    private readonly HubPaths _paths;
    private readonly HubState _state;
    private readonly Builds _builds;

    public RepoBackup(HubPaths paths, HubState state, Builds builds)
    {
        _paths  = paths;
        _state  = state;
        _builds = builds;
    }

    public RemoteView State()
    {
        if (!Git.IsRepo(_paths.Root)) return new RemoteView(null, "", 0, 0, null);
        string? url = Git.RemoteUrl(_paths.Root, Remote);
        string branch = Git.Branch(_paths.Root);
        var last = _state.LastPush;
        if (url == null) return new RemoteView(null, branch, 0, 0, last);

        int ahead = Git.Ahead(_paths.Root, Remote, branch);
        var pushed = last?.Url == url ? last.Tags.ToHashSet(StringComparer.Ordinal) : new HashSet<string>();
        int tags = Git.Tags(_paths.Root).Count(t => !pushed.Contains(t));
        return new RemoteView(url, branch, ahead, tags, last);
    }

    /// <summary>Sets where "Hochladen" goes. Nothing is sent yet.</summary>
    public RemoteView Connect(string? url)
    {
        string clean = url?.Trim() ?? "";
        if (clean.Length == 0) throw new HubError("Trag die Adresse des Repositorys ein – z. B. https://github.com/name/plugins-rhino.git");
        if (!HttpsUrl.IsMatch(clean) && !SshUrl.IsMatch(clean))
            throw new HubError("Das sieht nicht nach einer Repository-Adresse aus. Sie steht auf GitHub unter „Code“ und endet meist auf .git.");
        if (!Git.IsRepo(_paths.Root)) throw new HubError("„Plugins Rhino“ ist kein Git-Repository.");

        if (Git.RemoteUrl(_paths.Root, Remote) == null) Git.Exec(_paths.Root, "remote", "add", Remote, clean);
        else Git.Exec(_paths.Root, "remote", "set-url", Remote, clean);
        Log.Info($"Backup remote set to {clean}");
        return State();
    }

    /// <summary>
    /// Uploads the branch and every tag. Runs as a job the page follows in the build dock - the
    /// first time it waits for the GitHub sign-in window.
    /// </summary>
    public BuildJob Push()
    {
        string url = Git.RemoteUrl(_paths.Root, Remote) ?? throw new HubError("Trag zuerst die Adresse des Repositorys ein.");
        string branch = Git.Branch(_paths.Root);
        if (branch is "" or "HEAD") throw new HubError("Git steht auf keinem Branch – so lässt sich nichts hochladen.");

        return _builds.StartJob(new List<ProjectDef>(), "Hochladen zu " + ShortName(url), job =>
        {
            var timeout = TimeSpan.FromMinutes(10);
            job.Add($"git push {Remote} {branch}");
            job.Add("Beim ersten Mal öffnet Git ein Fenster zum Anmelden bei GitHub – es kann hinter dem Browser liegen.");
            if (Git.Stream(_paths.Root, job, timeout, "push", "-u", Remote, branch) != 0)
                throw new HubError("Das Hochladen ist fehlgeschlagen – siehe oben.");

            job.Add($"git push {Remote} --tags");
            if (Git.Stream(_paths.Root, job, timeout, "push", Remote, "--tags") != 0)
                throw new HubError("Die Versionen (Tags) ließen sich nicht hochladen – siehe oben.");

            var tags = Git.Tags(_paths.Root);
            _state.RecordPush(new PushRecord { At = DateTimeOffset.Now, Url = url, Tags = tags });
            job.AddResult(new BuildResult("git", "Git-Sicherung", true, "Hochgeladen", null));
            job.Add($"Fertig: {branch} und {tags.Count} Tags liegen jetzt auch unter {url}");
        });
    }

    /// <summary>"https://github.com/name/repo.git" -> "github.com/name/repo".</summary>
    public static string ShortName(string url)
    {
        string s = Regex.Replace(url, @"^(https://|[^@]+@)", "");
        s = s.Replace(':', '/');
        return s.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
    }
}

/// <summary>Url null: no backup repository yet. Ahead: commits not uploaded; Tags: tags not uploaded.</summary>
public sealed record RemoteView(string? Url, string Branch, int Ahead, int Tags, PushRecord? LastPush);

public sealed record RemoteRequest(string? Url);

public sealed class PushRecord
{
    public DateTimeOffset At { get; set; }
    public string Url { get; set; } = "";
    public List<string> Tags { get; set; } = new();
}
