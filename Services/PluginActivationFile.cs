using System.Text;
using System.Text.RegularExpressions;

namespace PluginHub.Services;

/// <summary>
/// The plug-in side of activation: Activation.cs in the plug-in's folder holds the public half of
/// its key pair and the contact address the request buttons write to. The hub writes that file - when it
/// makes a key pair, and when the contact changes - and reads it (and the built .rhp) back to
/// check that plug-in and hub still belong together.
/// </summary>
public static class PluginActivationFile
{
    // A P-256 public key as SubjectPublicKeyInfo in base64 always starts with "MFkw"
    private static readonly Regex KeyText     = new("\"(MFkw[A-Za-z0-9+/=]{60,})\"", RegexOptions.Compiled);
    private static readonly Regex ContactText = new("Contact\\s*=\\s*\"([^\"]*)\"", RegexOptions.Compiled);

    public static string? ReadKey(HubPaths paths, ProjectDef project)
    {
        string? text = ReadText(paths, project);
        var match = text == null ? null : KeyText.Match(text);
        return match is { Success: true } ? match.Groups[1].Value : null;
    }

    public static string ReadContact(HubPaths paths, ProjectDef project)
    {
        string? text = ReadText(paths, project);
        var match = text == null ? null : ContactText.Match(text);
        return match is { Success: true } ? match.Groups[1].Value : "";
    }

    /// <summary>
    /// True when the built .rhp carries this text. A C# string constant is stored as UTF-16 in
    /// the assembly, so it can be found in the file as it is.
    /// </summary>
    public static bool InBuild(HubPaths paths, ProjectDef project, string text)
    {
        if (project.Output == null || text.Length == 0) return false;
        string rhp = paths.Resolve(project.Output);
        if (!File.Exists(rhp)) return false;
        try
        {
            return File.ReadAllBytes(rhp).AsSpan().IndexOf(Encoding.Unicode.GetBytes(text)) >= 0;
        }
        catch (IOException) { return false; }
    }

    public static void WriteKey(HubPaths paths, ProjectDef project, string publicKey) =>
        Write(paths, project, publicKey, ReadContact(paths, project));

    public static void WriteContact(HubPaths paths, ProjectDef project, string contact)
    {
        string key = ReadKey(paths, project)
            ?? throw new HubError($"{project.Name} hat noch keinen Schlüssel – leg ihn zuerst an.");
        Write(paths, project, key, contact);
    }

    private static void Write(HubPaths paths, ProjectDef project, string publicKey, string contact)
    {
        if (project.KeyFile == null || project.KeyNamespace == null)
            throw new HubError($"In projects.json fehlt für {project.Name} „keyFile“ oder „keyNamespace“.");
        if (contact.Contains('"') || contact.Contains('\\'))
            throw new HubError("Die Kontakt-Adresse darf keine Anführungszeichen enthalten.");

        string text = string.Join("\r\n",
            $"namespace {project.KeyNamespace}",
            "{",
            "    /// <summary>",
            "    /// Written by the Plugin Hub - change it there, not here.",
            $"    /// PublicKey: the public half of {project.Name}'s activation key; every code is checked against",
            "    /// it, the private half lives only in the hub. A new key pair makes every earlier code invalid.",
            "    /// Contact: where \"Request a free trial\" and \"Buy a license\" in the activation window write to; empty hides them.",
            "    /// </summary>",
            "    internal static class Activation",
            "    {",
            "        public const string PublicKey =",
            $"            \"{publicKey}\";",
            "",
            $"        public const string Contact = \"{contact}\";",
            "    }",
            "}",
            "");
        Json.WriteText(paths.Resolve(project.KeyFile), text);
    }

    private static string? ReadText(HubPaths paths, ProjectDef project)
    {
        if (project.KeyFile == null) return null;
        string file = paths.Resolve(project.KeyFile);
        return File.Exists(file) ? File.ReadAllText(file, Encoding.UTF8) : null;
    }
}
