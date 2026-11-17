namespace PluginHub.Services;

/// <summary>
/// Where everything is. The hub lives in a folder of its own - "Plugin Hub" - beside the folders
/// of the projects it looks after: "Plugins Rhino", "Website", "TISS Bot". Its folder holds the
/// program, its pages (wwwroot) and its data (Hub\: projects.json, planning, settings, templates)
/// and is found by walking up from the .exe, which lives in bin\Release. The folder above it is
/// the root: every path in projects.json is relative to it. What must stay private - the
/// activation keys, the customer register, backups - lives outside all of that, under
/// %LOCALAPPDATA%\PluginHub, so it can never end up in Git, a ZIP or a shared folder.
/// </summary>
public sealed class HubPaths
{
    /// <summary>The folder the projects' folders lie in - and the hub's own.</summary>
    public string Root { get; }
    /// <summary>The hub's own folder.</summary>
    public string HubDir { get; }
    public string PrivateDir { get; }

    public string DataDir      => Path.Combine(HubDir, "Hub");
    public string ConfigFile   => Path.Combine(DataDir, "projects.json");
    public string WebRoot      => Path.Combine(HubDir, "wwwroot");
    public string KeysDir      => Path.Combine(PrivateDir, "keys");
    public string RegisterFile => Path.Combine(PrivateDir, "register.json");
    public string StateFile    => Path.Combine(PrivateDir, "state.json");
    public string RunningFile  => Path.Combine(PrivateDir, "running.json");
    public string BackupsDir   => Path.Combine(PrivateDir, "Backups");
    public string LogFile      => Path.Combine(PrivateDir, "hub.log");

    private HubPaths(string root, string hubDir, string privateDir)
    {
        Root       = root.TrimEnd(Path.DirectorySeparatorChar);
        HubDir     = hubDir.TrimEnd(Path.DirectorySeparatorChar);
        PrivateDir = privateDir.TrimEnd(Path.DirectorySeparatorChar);
        Directory.CreateDirectory(KeysDir);
        Directory.CreateDirectory(BackupsDir);
    }

    public static HubPaths Discover()
    {
        string privateDir = Environment.GetEnvironmentVariable("PLUGINHUB_PRIVATE_DIR") is { Length: > 0 } p
            ? Path.GetFullPath(p)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PluginHub");

        // A test hub runs against a copy of the root: its hub folder is the one in there that
        // holds the data
        if (Environment.GetEnvironmentVariable("PLUGINHUB_ROOT") is { Length: > 0 } r)
        {
            string root = Path.GetFullPath(r);
            string hub = Directory.Exists(root)
                ? Directory.GetDirectories(root).FirstOrDefault(HoldsData) ?? ""
                : "";
            if (hub.Length == 0)
                throw new InvalidOperationException(
                    "PLUGINHUB_ROOT: In keinem Ordner direkt unter " + root + " liegt Hub\\projects.json.");
            return new HubPaths(root, hub, privateDir);
        }

        // Upwards from the .exe - past the build's own folders: whatever a build may have
        // left there is not the data
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (!BuildFolder(dir) && HoldsData(dir.FullName) && dir.Parent != null)
                return new HubPaths(dir.Parent.FullName, dir.FullName, privateDir);

        throw new InvalidOperationException(
            "Der Hub findet seinen Ordner nicht: Hub\\projects.json liegt in keinem Ordner über " +
            AppContext.BaseDirectory);
    }

    private static bool HoldsData(string folder) => File.Exists(Path.Combine(folder, "Hub", "projects.json"));

    /// <summary>bin, obj and what lies directly in them (bin\Release).</summary>
    private static bool BuildFolder(DirectoryInfo dir) =>
        dir.Name is "bin" or "obj" || dir.Parent?.Name is "bin" or "obj";

    /// <summary>
    /// A path from projects.json: relative to the root, "{private}" for the private folder,
    /// %VARIABLES% expanded.
    /// </summary>
    public string Resolve(string path)
    {
        string p = Environment.ExpandEnvironmentVariables(path.Replace("{private}", PrivateDir))
            .Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(Root, p));
    }

    public string Relative(string fullPath) => Path.GetRelativePath(Root, fullPath);

    /// <summary>The same, with forward slashes - the way Git and the pages write paths.</summary>
    public string RelativeSlash(string fullPath) => Relative(fullPath).Replace('\\', '/');

    /// <summary>True for paths inside the root folder (not the root itself).</summary>
    public bool InsideRoot(string fullPath) =>
        fullPath.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
