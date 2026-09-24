namespace PluginHub.Services;

/// <summary>
/// Where everything is. The hub serves the folder that holds Hub\projects.json - found by
/// walking up from the .exe, which lives in PluginHub\bin\Release. What must stay private -
/// the activation keys, the customer register, backups - lives outside that folder, under
/// %LOCALAPPDATA%\PluginHub, so it can never end up in Git, a ZIP or a shared folder.
/// </summary>
public sealed class HubPaths
{
    public string Root { get; }
    public string PrivateDir { get; }

    public string DataDir      => Path.Combine(Root, "Hub");
    public string ConfigFile   => Path.Combine(DataDir, "projects.json");
    public string WebRoot      => Path.Combine(Root, "PluginHub", "wwwroot");
    public string KeysDir      => Path.Combine(PrivateDir, "keys");
    public string RegisterFile => Path.Combine(PrivateDir, "register.json");
    public string StateFile    => Path.Combine(PrivateDir, "state.json");
    public string RunningFile  => Path.Combine(PrivateDir, "running.json");
    public string BackupsDir   => Path.Combine(PrivateDir, "Backups");
    public string LogFile      => Path.Combine(PrivateDir, "hub.log");

    private HubPaths(string root, string privateDir)
    {
        Root       = root.TrimEnd(Path.DirectorySeparatorChar);
        PrivateDir = privateDir.TrimEnd(Path.DirectorySeparatorChar);
        Directory.CreateDirectory(KeysDir);
        Directory.CreateDirectory(BackupsDir);
    }

    public static HubPaths Discover()
    {
        string privateDir = Environment.GetEnvironmentVariable("PLUGINHUB_PRIVATE_DIR") is { Length: > 0 } p
            ? Path.GetFullPath(p)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PluginHub");

        if (Environment.GetEnvironmentVariable("PLUGINHUB_ROOT") is { Length: > 0 } r)
            return new HubPaths(Path.GetFullPath(r), privateDir);

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Hub", "projects.json")))
                return new HubPaths(dir.FullName, privateDir);

        throw new InvalidOperationException(
            "Der Hub findet seinen Ordner nicht: Hub\\projects.json liegt in keinem Ordner über " +
            AppContext.BaseDirectory);
    }

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

    /// <summary>True for paths inside the root folder (not the root itself).</summary>
    public bool InsideRoot(string fullPath) =>
        fullPath.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
