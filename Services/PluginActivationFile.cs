using System.Text;
using System.Text.RegularExpressions;

namespace PluginHub.Services;

/// <summary>
/// The plug-in side of activation: Activation.cs in the plug-in's folder holds the public half of
/// its key pair, the contact address the request buttons write to and whether licenses can be
/// bought yet. The hub writes that file - when it makes a key pair, and when one of the two
/// settings changes - and reads it (and the built .rhp) back to check that plug-in and hub
/// still belong together.
/// </summary>
public static class PluginActivationFile
{
    // A P-256 public key as SubjectPublicKeyInfo in base64 always starts with "MFkw"
    // The plug-in's own key comes first in the file, the suite key after it
    private static readonly Regex KeyText     = new("\"(MFkw[A-Za-z0-9+/=]{60,})\"", RegexOptions.Compiled);
    private static readonly Regex SuiteText   = new("SuiteKey\\s*=\\s*\"(MFkw[A-Za-z0-9+/=]{60,})?\"", RegexOptions.Compiled);
    private static readonly Regex ContactText = new("Contact\\s*=\\s*\"([^\"]*)\"", RegexOptions.Compiled);
    private static readonly Regex ShopText    = new("ShopOpen\\s*=\\s*(true|false)", RegexOptions.Compiled);

    /// <summary>The plug-in's own key - the one its codes from before the suite were made with.</summary>
    public static string? ReadKey(HubPaths paths, ProjectDef project)
    {
        string? text = ReadText(paths, project);
        var match = text == null ? null : KeyText.Match(text);
        return match is { Success: true } ? match.Groups[1].Value : null;
    }

    /// <summary>The suite key the plug-in checks codes with products against; null when its file has none.</summary>
    public static string? ReadSuiteKey(HubPaths paths, ProjectDef project)
    {
        string? text = ReadText(paths, project);
        var match = text == null ? null : SuiteText.Match(text);
        return match is { Success: true, Groups: var g } && g[1].Success ? g[1].Value : null;
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

    /// <summary>Licenses can be bought, as the plug-in's file says it; false for a file from before the switch.</summary>
    public static bool ReadShopOpen(HubPaths paths, ProjectDef project)
    {
        string? text = ReadText(paths, project);
        var match = text == null ? null : ShopText.Match(text);
        return match is { Success: true } && match.Groups[1].Value == "true";
    }

    /// <summary>A new key pair of the plug-in's own, with the suite key as the hub has it (empty when it has none).</summary>
    public static void WriteKey(HubPaths paths, ProjectDef project, string publicKey, string? suiteKey) =>
        Write(paths, project, publicKey, suiteKey ?? "", ReadContact(paths, project), ReadShopOpen(paths, project));

    /// <summary>A new suite key - every plug-in of the suite gets it when Laser Create's key is replaced.</summary>
    public static void WriteSuiteKey(HubPaths paths, ProjectDef project, string suiteKey)
    {
        string key = ReadKey(paths, project)
            ?? throw new HubError($"{project.Name} hat noch keinen Schlüssel – leg ihn zuerst an.");
        Write(paths, project, key, suiteKey, ReadContact(paths, project), ReadShopOpen(paths, project));
    }

    /// <summary>The contact address and the shop switch, as the hub's settings have them now.</summary>
    public static void WriteSettings(HubPaths paths, ProjectDef project, string contact, bool shopOpen)
    {
        string key = ReadKey(paths, project)
            ?? throw new HubError($"{project.Name} hat noch keinen Schlüssel – leg ihn zuerst an.");
        Write(paths, project, key, ReadSuiteKey(paths, project) ?? "", contact, shopOpen);
    }

    private static void Write(HubPaths paths, ProjectDef project, string publicKey, string suiteKey, string contact, bool shopOpen)
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
            $"    /// PublicKey: the public half of {project.Name}'s own activation key - the codes from before the suite were",
            $"    /// made with it and still unlock {project.Name}. A new key pair makes every earlier code invalid.",
            "    /// SuiteKey: the public half of the suite key (Laser Create's key), which signs every code that",
            "    /// names products; the private half lives only in the hub.",
            "    /// Contact: where \"Request a free trial\" and \"Buy a license\" in the activation window write to; empty hides them.",
            "    /// ShopOpen: licenses can be bought - \"Buy a license\" is then the button that stands out, not the free trial.",
            "    /// </summary>",
            "    internal static class Activation",
            "    {",
            "        public const string PublicKey =",
            $"            \"{publicKey}\";",
            "",
            "        public const string SuiteKey =",
            $"            \"{suiteKey}\";",
            "",
            $"        public const string Contact = \"{contact}\";",
            "",
            $"        public const bool ShopOpen = {(shopOpen ? "true" : "false")};",
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
