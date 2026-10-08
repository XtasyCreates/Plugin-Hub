using System.Text.Json.Serialization;

namespace PluginHub.Services;

/// <summary>
/// Hub\projects.json: the projects, their files and links. Read again whenever the file
/// changes, so an edit shows up with the next page load.
/// </summary>
public sealed class Config
{
    private readonly HubPaths _paths;
    private readonly object _gate = new();
    private HubConfig _config = new();
    private DateTime _stamp;

    public Config(HubPaths paths)
    {
        _paths = paths;
    }

    public HubConfig Current
    {
        get
        {
            lock (_gate)
            {
                var stamp = File.GetLastWriteTimeUtc(_paths.ConfigFile);
                if (stamp != _stamp)
                {
                    _config = Json.Read<HubConfig>(_paths.ConfigFile) ?? new HubConfig();
                    _stamp  = stamp;
                }
                return _config;
            }
        }
    }

    public ProjectDef Project(string id) =>
        Current.Projects.FirstOrDefault(p => p.Id == id)
        ?? throw new HubError($"Unbekanntes Projekt „{id}“.");

    /// <summary>
    /// A link from projects.json by its id - the page never sends a path of its own. The one
    /// exception, "path:&lt;relative path&gt;", points at a file the hub itself named (in a
    /// warning, say) and must lie inside the root folder.
    /// </summary>
    public LinkDef Link(string id)
    {
        var config = Current;
        var link = config.Links.Concat(config.Projects.SelectMany(p => p.Links)).FirstOrDefault(l => l.Id == id);
        if (link != null) return link;

        if (id.StartsWith("path:", StringComparison.Ordinal))
        {
            string relative = id[5..];
            string full = Path.GetFullPath(Path.Combine(_paths.Root, relative));
            if (!_paths.InsideRoot(full))
                throw new HubError("Dieser Pfad liegt außerhalb von „Plugins Rhino“.");
            return new LinkDef { Id = id, Kind = Directory.Exists(full) ? "folder" : "select", Path = relative };
        }

        throw new HubError($"Unbekannter Link „{id}“.");
    }
}

public sealed class HubConfig
{
    public List<ProjectDef> Projects { get; set; } = new();
    public List<LinkDef> Links { get; set; } = new();
    public List<CleanupDef> Cleanup { get; set; } = new();
}

public sealed class ProjectDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>
    /// "plugin" (built, sold, activated), "library" (compiled into the plug-ins), "tool" (the hub
    /// itself), "website" - or "extra": a project of its own, in this folder or next to it
    /// ("../TISS Bot"). An extra project stands in the side bar and has its planning; the
    /// overview, its notices and everything about selling leave it out.
    /// </summary>
    public string Kind { get; set; } = "plugin";
    public string Icon { get; set; } = "";
    public string Folder { get; set; } = "";
    public string? Note { get; set; }
    /// <summary>A page the project runs itself - its project page in the hub offers to open it.</summary>
    public string? Url { get; set; }
    /// <summary>What the button to that page says.</summary>
    public string? UrlLabel { get; set; }
    /// <summary>
    /// The HTTPS port under this PC's Tailscale name that leads to the same page - how the
    /// phone gets there. None: the phone is not offered the page.
    /// </summary>
    public int? PhonePort { get; set; }
    public string? Project { get; set; }
    public string? Output { get; set; }
    public string? PluginGuid { get; set; }
    public string? KeyFile { get; set; }
    public string? KeyNamespace { get; set; }
    /// <summary>A command that asks for activation - named in the reply to a customer.</summary>
    public string? StartCommand { get; set; }
    /// <summary>The command that opens the activation window at any time - to enter a new code early.</summary>
    public string? ActivationCommand { get; set; }
    /// <summary>Version.props: the version customers see.</summary>
    public string? VersionFile { get; set; }
    /// <summary>The built files that go into the customer package - nothing else does.</summary>
    public List<string> Package { get; set; } = new();
    /// <summary>How the package ZIP starts: "LaserCreate" gives LaserCreate-1.0.0.zip. Also the name in Rhino's Package Manager.</summary>
    public string? PackageName { get; set; }
    /// <summary>What Rhino's Package Manager says about the plug-in - in English, like the plug-ins.</summary>
    public string? PackageDescription { get; set; }
    /// <summary>Words the Package Manager finds it by.</summary>
    public List<string> PackageKeywords { get; set; } = new();
    public string? TagPattern { get; set; }
    /// <summary>The .rui Rhino may keep a cached copy of.</summary>
    public string? Toolbar { get; set; }
    /// <summary>Folders whose files the .rhp is built from - newer ones mean it is out of date.</summary>
    public List<string> Sources { get; set; } = new();
    public List<LinkDef> Links { get; set; } = new();

    [JsonIgnore] public bool IsPlugin => Kind == "plugin";
    [JsonIgnore] public bool IsExtra => Kind == "extra";
}

public sealed class LinkDef
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>folder | file | select (show in Explorer) | vscode</summary>
    public string Kind { get; set; } = "folder";
    public string Path { get; set; } = "";
}

public sealed class CleanupDef
{
    public string Path { get; set; } = "";
    public string Note { get; set; } = "";
}
