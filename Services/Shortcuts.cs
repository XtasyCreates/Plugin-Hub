namespace PluginHub.Services;

/// <summary>
/// Shortcuts to the hub name its .exe by its full path: the one on the desktop, the one in the
/// Start menu, and the one Windows keeps for a hub pinned to the taskbar. After the hub's folder
/// was moved or renamed they point at nothing. A hub that starts looks into those places and
/// writes its own path into every shortcut to a PluginHub.exe that is no longer there - a
/// shortcut to another hub that still exists is left alone.
/// </summary>
public static class Shortcuts
{
    public static void Follow()
    {
        string? exe = Environment.ProcessPath;
        if (exe == null) return;

        foreach (string folder in Folders().Where(Directory.Exists))
            foreach (string file in Directory.GetFiles(folder, "*.lnk"))
            {
                try { Fix(file, exe); }
                catch (Exception ex) { Log.Error($"Shortcut {file} could not be read or set: {ex.Message}"); }
            }
    }

    /// <summary>
    /// Where such shortcuts lie. A test hub looks only where it is told to
    /// (PLUGINHUB_SHORTCUT_DIRS, separated by ";") - never at the real desktop or taskbar.
    /// </summary>
    private static IEnumerable<string> Folders()
    {
        if (Environment.GetEnvironmentVariable("PLUGINHUB_PRIVATE_DIR") is { Length: > 0 })
            return (Environment.GetEnvironmentVariable("PLUGINHUB_SHORTCUT_DIRS") ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(roaming, "Microsoft", "Windows", "Start Menu", "Programs"),
            Path.Combine(roaming, "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar")
        };
    }

    private static void Fix(string file, string exe)
    {
        // Windows' own way to read and write a .lnk
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell is missing.");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic link = shell.CreateShortcut(file);

        string target = link.TargetPath ?? "";
        if (!string.Equals(Path.GetFileName(target), Path.GetFileName(exe), StringComparison.OrdinalIgnoreCase)) return;
        if (string.Equals(target, exe, StringComparison.OrdinalIgnoreCase) || File.Exists(target)) return;

        link.TargetPath       = exe;
        link.WorkingDirectory = Path.GetDirectoryName(exe);
        // The icon came from the .exe itself ("<path>,0"): it moved along
        string icon = link.IconLocation ?? "";
        if (icon.StartsWith(target, StringComparison.OrdinalIgnoreCase))
            link.IconLocation = exe + icon[target.Length..];
        link.Save();
        Log.Info($"Shortcut {file} now points here: {exe} (was {target})");
    }
}
