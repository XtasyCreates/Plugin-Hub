using System.Text;
using System.Text.RegularExpressions;

namespace PluginHub.Services;

/// <summary>
/// The plug-in side of a key pair: ActivationKey.cs in the plug-in's folder holds the public
/// half as a constant. The hub writes that file when it makes a key pair, and reads it (and
/// the built .rhp) back to check that plug-in and hub still belong together.
/// </summary>
public static class PluginKeyFile
{
    // A P-256 public key as SubjectPublicKeyInfo in base64 always starts with "MFkw"
    private static readonly Regex KeyText = new("\"(MFkw[A-Za-z0-9+/=]{60,})\"", RegexOptions.Compiled);

    public static string? Read(HubPaths paths, ProjectDef project)
    {
        if (project.KeyFile == null) return null;
        string file = paths.Resolve(project.KeyFile);
        if (!File.Exists(file)) return null;
        var match = KeyText.Match(File.ReadAllText(file, Encoding.UTF8));
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// True when the built .rhp carries this public key. A C# string constant is stored as
    /// UTF-16 in the assembly, so it can be found in the file as it is.
    /// </summary>
    public static bool InBuild(HubPaths paths, ProjectDef project, string publicKey)
    {
        if (project.Output == null) return false;
        string rhp = paths.Resolve(project.Output);
        if (!File.Exists(rhp)) return false;
        try
        {
            byte[] file = File.ReadAllBytes(rhp);
            return file.AsSpan().IndexOf(Encoding.Unicode.GetBytes(publicKey)) >= 0;
        }
        catch (IOException) { return false; }
    }

    public static void Write(HubPaths paths, ProjectDef project, string publicKey)
    {
        if (project.KeyFile == null || project.KeyNamespace == null)
            throw new HubError($"In projects.json fehlt für {project.Name} „keyFile“ oder „keyNamespace“.");

        string text = string.Join("\r\n",
            $"namespace {project.KeyNamespace}",
            "{",
            "    /// <summary>",
            $"    /// The public half of {project.Name}'s activation key: every code for {project.Name}",
            "    /// is checked against it. Written by the Plugin Hub when it made the key pair - the",
            "    /// private half lives only there. A new key pair makes every code issued before it",
            "    /// invalid, so change this only through the hub.",
            "    /// </summary>",
            "    internal static class ActivationKey",
            "    {",
            "        public const string Public =",
            $"            \"{publicKey}\";",
            "    }",
            "}",
            "");
        Json.WriteText(paths.Resolve(project.KeyFile), text);
    }
}
