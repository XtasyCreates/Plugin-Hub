using System.Text.RegularExpressions;

namespace PluginHub.Services;

/// <summary>
/// The version history, read from Git: every tag with the commits since the tag before it, and
/// which projects those commits touched - v1 to v47 are Claude's internal count for Laser
/// Create, "laser-1.0.0" and the like are releases. What came after the last tag is listed as
/// work that has no version yet.
///
/// Plug-ins, hub and website each have a repository of their own. A version that was tagged in
/// several of them - "v55" in all three - is one version here, with the projects of each. A
/// repository that was split off another one brought its past along; that past is read where
/// it happened (see <see cref="ProjectDef.HistorySince"/>), so nothing is counted twice. Put
/// together from two git calls per repository and kept until a HEAD or the tags change.
/// </summary>
public sealed class Versions
{
    private static readonly Regex ReleaseTag = new(@"^([a-z0-9]+)-(\d+\.\d+\.\d+)$", RegexOptions.Compiled);

    private readonly HubPaths _paths;
    private readonly Config _config;
    private readonly Repos _repos;
    private readonly object _gate = new();
    private string? _stamp;
    private VersionsView? _cached;

    public Versions(HubPaths paths, Config config, Repos repos)
    {
        _paths  = paths;
        _config = config;
        _repos  = repos;
    }

    public VersionsView All()
    {
        var repos = _repos.All().Select(r => (Repo: r, Tags: Git.TagRefs(r.Dir), Head: Git.Head(r.Dir))).ToList();
        string stamp = string.Join("\n", repos.Select(r => $"{r.Repo.Dir}|{r.Head}|{r.Tags}"))
                       + "\n" + File.GetLastWriteTimeUtc(_paths.ConfigFile).Ticks;
        lock (_gate)
        {
            if (_cached == null || _stamp != stamp)
            {
                _cached = Build(repos.Select(r => (r.Repo, r.Tags)).ToList());
                _stamp  = stamp;
            }
            return _cached;
        }
    }

    private VersionsView Build(List<(Repo Repo, string Tags)> repos)
    {
        var config = _config.Current;

        // A project's commits: those in its folders - and in the ones it had before it moved
        var projects = config.Projects
            .Select(p => (p.Id, Folders: (p.Sources.Count > 0 ? p.Sources : new List<string> { p.Folder })
                .Concat(p.FormerFolders)
                .Select(f => f.Replace('\\', '/').TrimEnd('/') + "/").ToList()))
            .ToList();
        List<string> Touched(IEnumerable<string> files) => projects
            .Where(p => files.Any(f => p.Folders.Any(folder => f.StartsWith(folder, StringComparison.OrdinalIgnoreCase))))
            .Select(p => p.Id).ToList();

        var versions = new List<VersionView>();
        var untagged = new List<UntaggedCommit>();
        foreach (var (repo, tagRefs) in repos)
        {
            // commit -> its tags (an annotated tag names its commit in the third column)
            var tagsOf = new Dictionary<string, List<string>>();
            foreach (string line in tagRefs.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.TrimEnd('\r').Split(' ');
                if (parts.Length < 2) continue;
                string commit = parts.Length > 2 && parts[2].Length > 0 ? parts[2] : parts[1];
                if (!tagsOf.TryGetValue(commit, out var list)) tagsOf[commit] = list = new();
                list.Add(parts[0]);
            }

            // The past a split-off repository brought along is told by the one it came from
            var history = Git.History(repo.Dir);
            string? since = config.Projects
                .Where(p => p.HistorySince is { Length: >= 7 } && _repos.Of(p) is { } own && Repos.Same(own.Dir, repo.Dir))
                .Select(p => p.HistorySince).FirstOrDefault();
            int start = since == null ? 0 : history.FindIndex(c => c.Hash.StartsWith(since, StringComparison.OrdinalIgnoreCase)) + 1;

            var mine = new List<VersionView>();
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new List<GitCommit>();
            foreach (var commit in history.Skip(start))
            {
                pending.Add(commit);
                files.UnionWith(commit.Files.Select(f => repo.Prefix + f));
                if (!tagsOf.TryGetValue(commit.Hash, out var tags)) continue;

                var touched = Touched(files);
                foreach (string tag in tags.OrderBy(t => t, StringComparer.Ordinal))
                {
                    var release = ReleaseTag.Match(tag);
                    bool isRelease = release.Success && projects.Any(p => p.Id == release.Groups[1].Value);
                    mine.Add(new VersionView(tag, commit.Date, commit.Subject, commit.Hash[..7], isRelease,
                        isRelease ? release.Groups[2].Value : null,
                        isRelease ? new List<string> { release.Groups[1].Value } : touched, pending.Count));
                }
                files.Clear();
                pending.Clear();
            }

            mine.Reverse();   // newest first
            versions.AddRange(mine);
            untagged.AddRange(pending.AsEnumerable().Reverse().Select(c =>
                new UntaggedCommit(c.Hash[..7], c.Date, c.Subject, Touched(c.Files.Select(f => repo.Prefix + f)))));
        }

        // One repository: its own order, as Git tells it. Several: by date, and the same tag in
        // more than one of them is one version - the newest commit names it, the projects and
        // commits of all of them count.
        if (repos.Count > 1)
        {
            versions = versions
                .GroupBy(v => v.Tag, StringComparer.Ordinal)
                .Select(g =>
                {
                    var first = g.OrderByDescending(v => v.Date).First();
                    return g.Count() == 1 ? first : first with
                    {
                        Projects = projects.Select(p => p.Id).Where(id => g.Any(v => v.Projects.Contains(id))).ToList(),
                        Commits  = g.Sum(v => v.Commits)
                    };
                })
                .OrderByDescending(v => v.Date)
                .ToList();
            untagged = untagged.OrderByDescending(c => c.Date).ToList();
        }

        return new VersionsView(versions, untagged);
    }
}

public sealed record VersionView(string Tag, DateTimeOffset Date, string Subject, string Commit, bool Release,
    string? Version, List<string> Projects, int Commits);

public sealed record UntaggedCommit(string Hash, DateTimeOffset Date, string Subject, List<string> Projects);

/// <summary>Versions newest first; Untagged: the commits after the last tag, newest first.</summary>
public sealed record VersionsView(List<VersionView> Versions, List<UntaggedCommit> Untagged);
