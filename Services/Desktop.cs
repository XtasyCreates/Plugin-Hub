using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PluginHub.Services;

/// <summary>The few things the hub does on the Windows desktop itself.</summary>
public static class Desktop
{
    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { Log.Error("Could not open the browser: " + ex.Message); }
    }

    /// <summary>A message box - the only way a hub without a page can say what went wrong.</summary>
    public static void ShowError(string text) => MessageBoxW(IntPtr.Zero, text, "Plugin Hub", 0x10);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}

/// <summary>A few lines to hub.log - the hub has no console window to write to.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _file;

    public static void Init(string file)
    {
        _file = file;
        try
        {
            // Start over once it passes 1 MB - it is for "what just happened", not history
            var info = new FileInfo(file);
            if (info.Exists && info.Length > 1_000_000) info.Delete();
        }
        catch { }
    }

    public static void Info(string message)  => Write("INFO ", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        if (_file == null) return;
        lock (Gate)
        {
            try { File.AppendAllText(_file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}"); }
            catch { }
        }
    }
}

/// <summary>A problem the user can act on; its message is shown on the page as it is.</summary>
public sealed class HubError : Exception
{
    public HubError(string message) : base(message) { }
}
