using System.Text.RegularExpressions;

namespace PluginHub.Services;

/// <summary>
/// The version history, read from Git: every tag with the commits since the tag before it, and
/// which projects those commits touched - v1 to v47 are Claude's internal count for Laser
/// Create, "laser-1.0.0" and the like are releases. What came after the last tag is listed as
/// work that has no version yet. Put together from two git calls and kept until HEAD or the
/// tags change.
/// </summary>
public sealed class Versions
{
    private static readonly Regex ReleaseTag = new(@"^([a-z0-9]+)-(\d+\.\d+\.\d+)$", RegexOptions.Compiled);

    private readonly HubPaths _paths;
    private readonly Config _config;
    private readonly object _gate = new();
    private string? _stamp;
    private VersionsView? _cached;

    public Versions(HubPaths paths, Config config)
    {
        _paths  = paths;
        _config = config;
    }

    public VersionsView All()
    {
        string tags = Git.TagRefs(_paths.Root);
        string stamp = Git.Head(_paths.Root) + "|" + tags;
        lock (_gate)
        {
            if (_cached == null || _stamp != stamp)
            {
                _cached = Build(tags);
                _stamp  = stamp;
            }
            return _cached;
        }
    }

    private VersionsView Build(string tagRefs)
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

        var projects = _config.Current.Projects
            .Select(p => (p.Id, Folders: (p.Sources.Count > 0 ? p.Sources : new List<string> { p.Folder })
                .Select(f => f.Replace('\\', '/').TrimEnd('/') + "/").ToList()))
            .ToList();
        List<string> Touched(IEnumerable<string> files) => projects
            .Where(p => files.Any(f => p.Folders.Any(folder => f.StartsWith(folder, StringComparison.OrdinalIgnoreCase))))
            .Select(p => p.Id).ToList();

        var versions = new List<VersionView>();
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<GitCommit>();
        foreach (var commit in Git.History(_paths.Root))
        {
            pending.Add(commit);
            files.UnionWith(commit.Files);
            if (!tagsOf.TryGetValue(commit.Hash, out var tags)) continue;

            var touched = Touched(files);
            foreach (string tag in tags.OrderBy(t => t, StringComparer.Ordinal))
            {
                var release = ReleaseTag.Match(tag);
                bool isRelease = release.Success && projects.Any(p => p.Id == release.Groups[1].Value);
                versions.Add(new VersionView(tag, commit.Date, commit.Subject, commit.Hash[..7], isRelease,
                    isRelease ? release.Groups[2].Value : null,
                    isRelease ? new List<string> { release.Groups[1].Value } : touched, pending.Count));
            }
            files.Clear();
            pending.Clear();
        }

        versions.Reverse();   // newest first
        var untagged = pending.AsEnumerable().Reverse()
            .Select(c => new UntaggedCommit(c.Hash[..7], c.Date, c.Subject, Touched(c.Files)))
            .ToList();
        return new VersionsView(versions, untagged);
    }
}

public sealed record VersionView(string Tag, DateTimeOffset Date, string Subject, string Commit, bool Release,
    string? Version, List<string> Projects, int Commits);

public sealed record UntaggedCommit(string Hash, DateTimeOffset Date, string Subject, List<string> Projects);

/// <summary>Versions newest first; Untagged: the commits after the last tag, newest first.</summary>
public sealed record VersionsView(List<VersionView> Versions, List<UntaggedCommit> Untagged);
