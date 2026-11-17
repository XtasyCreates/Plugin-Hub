namespace PluginHub.Services;

/// <summary>
/// The Git repositories the projects live in. Each home folder under the root - "Plugins Rhino",
/// "Plugin Hub", "Website" - is a repository of its own, so the hub asks where a project's is
/// instead of assuming one for everything. The pages and the planning name files relative to
/// the root ("Plugin Hub/Hub/roadmap.json"); a repository knows them without its own folder in
/// front ("Hub/roadmap.json").
/// </summary>
public sealed class Repos
{
    private readonly HubPaths _paths;
    private readonly Config _config;

    public Repos(HubPaths paths, Config config)
    {
        _paths  = paths;
        _config = config;
    }

    /// <summary>Every repository that holds a project or the hub's data, in the order of projects.json.</summary>
    public List<Repo> All()
    {
        var list = new List<Repo>();
        foreach (string folder in _config.Current.Projects.Select(p => _paths.Resolve(p.Folder)).Append(_paths.DataDir))
            if (Find(folder) is { } repo && !list.Any(r => Same(r.Dir, repo.Dir)))
                list.Add(repo);
        return list;
    }

    public Repo? Of(ProjectDef project) => Find(_paths.Resolve(project.Folder));

    public Repo? ById(string? id) => All().FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The repository a file or folder lies in - the nearest folder above it with a .git, the
    /// root at most. Null when there is none: a project without Git, or a path outside the root.
    /// </summary>
    public Repo? Find(string fullPath)
    {
        for (string? dir = fullPath; dir != null && (Same(dir, _paths.Root) || _paths.InsideRoot(dir)); dir = Path.GetDirectoryName(dir))
        {
            string git = Path.Combine(dir, ".git");
            if (!Directory.Exists(git) && !File.Exists(git)) continue;

            string id = _paths.RelativeSlash(dir);
            return new Repo(id, Path.GetFileName(dir), dir, id == "." ? "" : id + "/");
        }
        return null;
    }

    /// <summary>A path relative to the root, as its repository names it; null when it lies in none.</summary>
    public (Repo Repo, string Path)? Locate(string rootRelative)
    {
        string full = _paths.Resolve(rootRelative.TrimEnd('/'));
        return Find(full) is { } repo ? (repo, InRepo(repo, full)) : null;
    }

    /// <summary>Everything not committed, in every repository, with paths relative to the root.</summary>
    public List<GitChange> Changes() => Changes(All());

    public List<GitChange> Changes(List<Repo> repos)
    {
        // One `git status` each, side by side: the overview waits for all of them
        var found = new List<GitChange>[repos.Count];
        Parallel.For(0, repos.Count, i =>
            found[i] = Git.Changes(repos[i].Dir).Select(c => c with { Path = repos[i].Prefix + c.Path }).ToList());
        return found.SelectMany(f => f).ToList();
    }

    /// <summary>
    /// True for a project of its own ("extra") whose folder is its repository - MilkDrop. Nothing
    /// else lives in there and no release decides what a commit is, so its page may offer one
    /// commit of everything. The plug-ins, the hub and the website are committed as a version.
    /// </summary>
    public bool CanCommit(ProjectDef project) =>
        project.IsExtra && Of(project) is { } repo && Same(repo.Dir, _paths.Resolve(project.Folder));

    /// <summary>
    /// Commits everything open in such a project's repository - changed, new, deleted; what
    /// .gitignore names stays out. On a click only. Returns how many changes went in.
    /// </summary>
    public int CommitProject(ProjectDef project, string? message)
    {
        if (!CanCommit(project))
            throw new HubError($"{project.Name} wird nicht von hier aus committet.");

        var repo    = Of(project)!;
        int changes = Git.Changes(repo.Dir).Count;
        if (changes == 0) return 0;

        string text = string.IsNullOrWhiteSpace(message) ? $"Stand {DateTime.Now:dd.MM.yyyy}" : message.Trim();
        Git.Exec(repo.Dir, "add", "-A");
        Git.Exec(repo.Dir, "commit", "-q", "-m", text);
        Log.Info($"Committed {project.Name}: {changes} changes, \"{text}\"");
        return changes;
    }

    /// <summary>A file or folder as Git names it inside its repository - "." for the repository's own folder.</summary>
    public static string InRepo(Repo repo, string fullPath) =>
        Path.GetRelativePath(repo.Dir, fullPath).Replace('\\', '/');

    public static bool Same(string a, string b) =>
        string.Equals(a.TrimEnd(Path.DirectorySeparatorChar), b.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Id: the repository's folder relative to the root ("Plugin Hub") - "." when the root itself
/// is the repository. Prefix: what turns a path inside it into one relative to the root
/// ("Plugin Hub/", or nothing).
/// </summary>
public sealed record Repo(string Id, string Name, string Dir, string Prefix);
