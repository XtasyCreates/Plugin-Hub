using System.Text;

namespace PluginHub.Services;

/// <summary>
/// The documents the hub shows and edits: every Markdown file in the root folder - concepts,
/// progress notes, the hub's README - and the licence drafts in Hub\legal. What a release
/// archived under Releases\ is shown but never changed. The page names a document by its path,
/// and the hub only answers for paths that are on this list.
/// </summary>
public sealed class Documents
{
    private static readonly HashSet<string> Markdown = new(StringComparer.OrdinalIgnoreCase) { ".md" };
    private static readonly HashSet<string> Text = new(StringComparer.OrdinalIgnoreCase) { ".txt" };
    private const long MaxSize = 2_000_000;

    private readonly HubPaths _paths;
    private readonly Config _config;

    public Documents(HubPaths paths, Config config)
    {
        _paths  = paths;
        _config = config;
    }

    public List<DocInfo> List()
    {
        var config = _config.Current;
        var skipped = config.Cleanup.Select(c => _paths.Resolve(c.Path)).ToList();

        var files = Files.Walk(_paths.Root, Markdown)
            .Concat(Files.Walk(Path.Combine(_paths.DataDir, "legal"), Text))
            .Where(f => !skipped.Any(s => f.StartsWith(s + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !Relative(f).StartsWith(".claude/", StringComparison.OrdinalIgnoreCase));

        return files.Select(Info)
            .Where(d => d.Size <= MaxSize)
            .OrderBy(d => d.ReadOnly)
            .ThenBy(d => d.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public DocContent Read(string path)
    {
        var info = Find(path);
        string full = _paths.Resolve(info.Path);
        return new DocContent(info, File.ReadAllText(full, Encoding.UTF8), Stamp(full));
    }

    /// <summary>
    /// Saves a document the page had loaded at <paramref name="stamp"/>. Changed on disk since -
    /// in VS Code, by Claude - it refuses rather than overwrite that.
    /// </summary>
    public DocContent Write(string path, string text, long stamp)
    {
        var info = Find(path);
        if (info.ReadOnly) throw new HubError("Dieses Dokument gehört zu einem Release und bleibt, wie es verschickt wurde.");
        string full = _paths.Resolve(info.Path);
        if (Stamp(full) != stamp)
            throw new HubError("Das Dokument wurde inzwischen woanders geändert. Lade es neu – deine Änderung ist noch im Feld, kopier sie vorher.");

        // Keep the file's line ends: the documents were written with CRLF or LF, and a switch
        // would show every line as changed in Git
        string current = File.ReadAllText(full, Encoding.UTF8);
        string normalized = text.Replace("\r\n", "\n");
        if (current.Contains("\r\n")) normalized = normalized.Replace("\n", "\r\n");
        var bom = File.ReadAllBytes(full).AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF });

        string temp = full + ".tmp";
        File.WriteAllText(temp, normalized, new UTF8Encoding(bom));
        File.Move(temp, full, overwrite: true);
        Log.Info($"Document saved: {info.Path}");
        return Read(info.Path);
    }

    private DocInfo Find(string path)
    {
        string wanted = (path ?? "").Replace('\\', '/');
        return List().FirstOrDefault(d => string.Equals(d.Path, wanted, StringComparison.OrdinalIgnoreCase))
            ?? throw new HubError($"Das Dokument „{path}“ gibt es nicht (mehr).");
    }

    private DocInfo Info(string full)
    {
        string relative = Relative(full);
        var file = new FileInfo(full);
        return new DocInfo(relative, Path.GetFileNameWithoutExtension(full), ProjectOf(relative),
            Markdown.Contains(file.Extension) ? "md" : "txt",
            relative.StartsWith("Releases/", StringComparison.OrdinalIgnoreCase),
            file.LastWriteTime, file.Length);
    }

    /// <summary>The project whose folder holds it, or that links to it in projects.json.</summary>
    private string? ProjectOf(string relative)
    {
        foreach (var p in _config.Current.Projects)
        {
            string folder = p.Folder.Replace('\\', '/').TrimEnd('/') + "/";
            if (relative.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return p.Id;
            if (p.Links.Any(l => string.Equals(l.Path.Replace('\\', '/'), relative, StringComparison.OrdinalIgnoreCase))) return p.Id;
            if (relative.StartsWith($"Releases/{p.Name}/", StringComparison.OrdinalIgnoreCase)) return p.Id;
        }
        return null;
    }

    private string Relative(string full) => _paths.Relative(full).Replace('\\', '/');

    private static long Stamp(string full) => File.GetLastWriteTimeUtc(full).Ticks;
}

/// <summary>Kind: md or txt. ReadOnly: archived with a release.</summary>
public sealed record DocInfo(string Path, string Name, string? Project, string Kind, bool ReadOnly, DateTime Changed, long Size);

/// <summary>Stamp: the file's time when read - the save sends it back to catch changes made elsewhere.</summary>
public sealed record DocContent(DocInfo Info, string Text, long Stamp);

public sealed record DocSaveRequest(string Path, string Text, long Stamp);
