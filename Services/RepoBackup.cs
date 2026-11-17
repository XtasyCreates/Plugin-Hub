using System.Text.RegularExpressions;

namespace PluginHub.Services;

/// <summary>
/// The copies of the repositories outside this PC: for each of them a private repository on
/// GitHub (or anywhere Git reaches) as its remote "origin". The hub uploads only when asked -
/// "Hochladen", which takes every repository that has such an address - and says how much is
/// not up there yet. Signing in is Git's own business: its credential manager opens a window
/// the first time and remembers the login.
/// </summary>
public sealed class RepoBackup
{
    private const string Remote = "origin";

    private static readonly Regex HttpsUrl = new(@"^https://[A-Za-z0-9.-]+(:\d+)?(/[A-Za-z0-9._~-]+)+/?$", RegexOptions.Compiled);
    private static readonly Regex SshUrl   = new(@"^[A-Za-z0-9._-]+@[A-Za-z0-9.-]+:[A-Za-z0-9._/~-]+$", RegexOptions.Compiled);

    private readonly Repos _repos;
    private readonly HubState _state;
    private readonly Builds _builds;

    public RepoBackup(Repos repos, HubState state, Builds builds)
    {
        _repos  = repos;
        _state  = state;
        _builds = builds;
    }

    /// <summary>Every repository with where its copy is and how far that copy is behind.</summary>
    public List<RemoteView> State() => State(_repos.All());

    public List<RemoteView> State(List<Repo> repos)
    {
        var views = new RemoteView[repos.Count];
        Parallel.For(0, repos.Count, i => views[i] = State(repos[i]));
        return views.ToList();
    }

    private RemoteView State(Repo repo)
    {
        string? url = Git.RemoteUrl(repo.Dir, Remote);
        string branch = Git.Branch(repo.Dir);
        if (url == null) return new RemoteView(repo.Id, repo.Name, null, branch, 0, 0, null);

        var last = _state.PushTo(url);
        int ahead = Git.Ahead(repo.Dir, Remote, branch);
        var pushed = last?.Tags.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>();
        int tags = Git.Tags(repo.Dir).Count(t => !pushed.Contains(t));
        return new RemoteView(repo.Id, repo.Name, url, branch, ahead, tags, last);
    }

    /// <summary>Sets where "Hochladen" sends a repository. Nothing is sent yet.</summary>
    public List<RemoteView> Connect(string? repoId, string? url)
    {
        var repo = _repos.ById(repoId) ?? throw new HubError("Dieses Repository gibt es nicht (mehr).");
        string clean = url?.Trim() ?? "";
        if (clean.Length == 0) throw new HubError("Trag die Adresse des Repositorys ein – z. B. https://github.com/name/plugin-hub.git");
        if (!HttpsUrl.IsMatch(clean) && !SshUrl.IsMatch(clean))
            throw new HubError("Das sieht nicht nach einer Repository-Adresse aus. Sie steht auf GitHub unter „Code“ und endet meist auf .git.");

        // Two folders sent to one address would overwrite each other up there
        var taken = _repos.All().FirstOrDefault(r => !Repos.Same(r.Dir, repo.Dir)
            && string.Equals(Git.RemoteUrl(r.Dir, Remote), clean, StringComparison.OrdinalIgnoreCase));
        if (taken != null)
            throw new HubError($"Diese Adresse gehört schon zu „{taken.Name}“. Jeder Ordner braucht sein eigenes Repository auf GitHub.");

        if (Git.RemoteUrl(repo.Dir, Remote) == null) Git.Exec(repo.Dir, "remote", "add", Remote, clean);
        else Git.Exec(repo.Dir, "remote", "set-url", Remote, clean);
        Log.Info($"Backup remote of {repo.Name} set to {clean}");
        return State();
    }

    /// <summary>
    /// Uploads branch and tags of every repository that has an address. Runs as a job the page
    /// follows in the build dock - the first time it waits for the GitHub sign-in window. One
    /// that fails does not keep the others from going up.
    /// </summary>
    public BuildJob Push() => Push(_repos.All(), "Trag zuerst die Adresse eines Repositorys ein.");

    /// <summary>
    /// The same for the repository of one project alone - from the page of a project that is
    /// committed there (<see cref="Repos.CanCommit"/>).
    /// </summary>
    public BuildJob Push(ProjectDef project)
    {
        if (!_repos.CanCommit(project))
            throw new HubError($"{project.Name} wird nicht einzeln hochgeladen – nimm „Hochladen“ für alle Repositories.");
        return Push(new List<Repo> { _repos.Of(project)! },
            $"Für {project.Name} ist noch keine Adresse auf GitHub eingetragen.");
    }

    private BuildJob Push(List<Repo> repos, string none)
    {
        var targets = repos
            .Select(r => (Repo: r, Url: Git.RemoteUrl(r.Dir, Remote)))
            .Where(t => t.Url != null)
            .ToList();
        if (targets.Count == 0) throw new HubError(none);

        string title = targets.Count == 1 ? "Hochladen zu " + ShortName(targets[0].Url!) : $"Hochladen: {targets.Count} Repositories";
        return _builds.StartJob(new List<ProjectDef>(), title, job =>
        {
            job.Add("Beim ersten Mal öffnet Git ein Fenster zum Anmelden bei GitHub – es kann hinter dem Browser liegen.");
            var failed = new List<string>();
            foreach (var (repo, url) in targets)
            {
                job.Add($"── {repo.Name} → {ShortName(url!)} ──");
                try
                {
                    PushOne(job, repo, url!);
                    job.AddResult(new BuildResult(repo.Id, repo.Name, true, "Hochgeladen", null));
                }
                catch (HubError ex)
                {
                    job.Add("Fehler: " + ex.Message);
                    job.AddResult(new BuildResult(repo.Id, repo.Name, false, "nicht hochgeladen", null));
                    failed.Add(repo.Name);
                }
            }
            if (failed.Count > 0)
                throw new HubError($"Nicht hochgeladen: {string.Join(", ", failed)} – siehe oben.");
        });
    }

    private void PushOne(BuildJob job, Repo repo, string url)
    {
        string branch = Git.Branch(repo.Dir);
        if (branch is "" or "HEAD") throw new HubError("Git steht auf keinem Branch – so lässt sich nichts hochladen.");

        var timeout = TimeSpan.FromMinutes(10);
        job.Add($"git push {Remote} {branch}");
        if (Git.Stream(repo.Dir, job, timeout, "push", "-u", Remote, branch) != 0)
            throw new HubError("Das Hochladen ist fehlgeschlagen.");

        job.Add($"git push {Remote} --tags");
        if (Git.Stream(repo.Dir, job, timeout, "push", Remote, "--tags") != 0)
            throw new HubError("Die Versionen (Tags) ließen sich nicht hochladen.");

        var tags = Git.Tags(repo.Dir);
        _state.RecordPush(new PushRecord { At = DateTimeOffset.Now, Url = url, Tags = tags });
        job.Add($"Fertig: {branch} und {tags.Count} Tags liegen jetzt auch unter {url}");
    }

    /// <summary>"https://github.com/name/repo.git" -> "github.com/name/repo".</summary>
    public static string ShortName(string url)
    {
        string s = Regex.Replace(url, @"^(https://|[^@]+@)", "");
        s = s.Replace(':', '/');
        return s.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
    }
}

/// <summary>
/// One repository and its copy. Repo: its folder relative to the root. Url null: no copy yet.
/// Ahead: commits not uploaded; Tags: tags not uploaded; LastPush: the last upload to this address.
/// </summary>
public sealed record RemoteView(string Repo, string Name, string? Url, string Branch, int Ahead, int Tags, PushRecord? LastPush);

public sealed record RemoteRequest(string? Repo, string? Url);

public sealed class PushRecord
{
    public DateTimeOffset At { get; set; }
    public string Url { get; set; } = "";
    public List<string> Tags { get; set; } = new();
}
