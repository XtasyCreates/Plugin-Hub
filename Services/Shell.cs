using System.Diagnostics;
using Microsoft.Win32;

namespace PluginHub.Services;

/// <summary>Opens folders and files the way Explorer would, or in VS Code.</summary>
public static class Shell
{
    public static void Open(HubPaths paths, LinkDef link)
    {
        string target = paths.Resolve(link.Path);
        switch (link.Kind)
        {
            case "folder":
                if (!Directory.Exists(target)) throw Missing(target);
                Start("explorer.exe", Quote(target));
                break;

            case "select":
                if (File.Exists(target)) Start("explorer.exe", "/select," + Quote(target));
                else if (Directory.Exists(Path.GetDirectoryName(target))) Start("explorer.exe", Quote(Path.GetDirectoryName(target)!));
                else throw Missing(target);
                break;

            case "file":
                if (!File.Exists(target)) throw Missing(target);
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
                break;

            case "vscode":
                if (!Directory.Exists(target) && !File.Exists(target)) throw Missing(target);
                Start(VsCode(), Quote(target));
                break;

            default:
                throw new HubError($"Unbekannte Link-Art „{link.Kind}“.");
        }
    }

    private static string VsCode()
    {
        string[] candidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe")
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new HubError("VS Code ist nicht installiert – Code.exe wurde nicht gefunden.");
    }

    private static void Start(string exe, string arguments) =>
        Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false })?.Dispose();

    private static string Quote(string path) => "\"" + path + "\"";

    private static HubError Missing(string path) => new($"Das gibt es (noch) nicht: {path}");
}

/// <summary>What Rhino knows about the plug-ins: running copies, registrations, cached toolbars.</summary>
public static class RhinoInfo
{
    private static readonly string[] Versions = { "8.0", "9.0" };

    public static List<RhinoProcess> Running()
    {
        var list = new List<RhinoProcess>();
        foreach (var p in Process.GetProcessesByName("Rhino"))
        {
            string path = "";
            try { path = p.MainModule?.FileName ?? ""; } catch { }
            list.Add(new RhinoProcess(p.Id, path, Label(path)));
            p.Dispose();
        }
        return list;
    }

    // "C:\Program Files\Rhino 9 WIP\System\Rhino.exe" -> "Rhino 9 WIP"
    private static string Label(string exe)
    {
        string? install = Path.GetDirectoryName(Path.GetDirectoryName(exe));
        return string.IsNullOrEmpty(install) ? "Rhino" : Path.GetFileName(install);
    }

    /// <summary>
    /// Which file each Rhino version loads for this plug-in. Only versions that have run for
    /// this Windows user are listed.
    /// </summary>
    public static List<Registration> Registrations(ProjectDef project, string expectedRhp)
    {
        var list = new List<Registration>();
        if (project.PluginGuid == null) return list;

        foreach (string version in Versions)
        {
            using var rhino = Registry.CurrentUser.OpenSubKey($@"Software\McNeel\Rhinoceros\{version}");
            if (rhino == null) continue;

            using var key = rhino.OpenSubKey($@"Plug-Ins\{project.PluginGuid.ToLowerInvariant()}\PlugIn");
            string? file = key?.GetValue("FileName") as string;
            string state = file == null ? "missing"
                : string.Equals(Path.GetFullPath(file), expectedRhp, StringComparison.OrdinalIgnoreCase) ? "release"
                : "other";
            list.Add(new Registration(version.Split('.')[0], state, file));
        }
        return list;
    }

    /// <summary>
    /// Rhino's own copies of the plug-in's toolbar file - it may keep showing old icons from
    /// there after the .rui changed.
    /// </summary>
    public static List<string> ToolbarCache(ProjectDef project)
    {
        var list = new List<string>();
        if (project.Toolbar == null) return list;
        foreach (string version in Versions)
        {
            string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "McNeel", "Rhinoceros", version, "UI", "Plug-ins", project.Toolbar);
            if (File.Exists(file)) list.Add(file);
        }
        return list;
    }

    public static int ClearToolbarCache(ProjectDef project)
    {
        if (Running().Count > 0)
            throw new HubError("Rhino läuft und schreibt den Cache beim Beenden neu. Schließ Rhino zuerst.");
        var files = ToolbarCache(project);
        foreach (string file in files) File.Delete(file);
        return files.Count;
    }
}

public sealed record RhinoProcess(int Pid, string Path, string Label);

/// <summary>State: "release" (loads the Release build), "other" (another file), "missing".</summary>
public sealed record Registration(string Rhino, string State, string? File);
