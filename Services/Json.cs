using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PluginHub.Services;

/// <summary>The hub's files are JSON, written so a crash never leaves half a file.</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented          = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Keep umlauts readable in the files instead of ü
        Encoder                = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static T? Read<T>(string file) where T : class
    {
        if (!File.Exists(file)) return null;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(file, Encoding.UTF8), Options);
    }

    public static void Write<T>(string file, T value) =>
        WriteText(file, JsonSerializer.Serialize(value, Options));

    /// <summary>Writes next to the target and swaps it in.</summary>
    public static void WriteText(string file, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temp = file + ".tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(false));
        File.Move(temp, file, overwrite: true);
    }
}

/// <summary>Walks source folders, leaving out what builds and tools put there.</summary>
public static class Files
{
    private static readonly HashSet<string> Skipped =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", ".vscode", "node_modules" };

    public static IEnumerable<string> Walk(string folder, ISet<string>? extensions = null)
    {
        if (!Directory.Exists(folder)) yield break;

        var pending = new Stack<string>();
        pending.Push(folder);
        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            string[] files, dirs;
            try
            {
                files = Directory.GetFiles(dir);
                dirs  = Directory.GetDirectories(dir);
            }
            catch { continue; }

            foreach (string f in files)
                if (extensions == null || extensions.Contains(Path.GetExtension(f)))
                    yield return f;
            foreach (string d in dirs)
                if (!Skipped.Contains(Path.GetFileName(d)))
                    pending.Push(d);
        }
    }

    /// <summary>
    /// True when another program holds the file - Rhino 8 does with a loaded .rhp, and the
    /// build's copy onto it would then fail. (Rhino 9 WIP reads the file and lets go.)
    /// </summary>
    public static bool IsLocked(string file)
    {
        if (!File.Exists(file)) return false;
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }
}
