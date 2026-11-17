using System.Text;
using System.Text.RegularExpressions;

namespace PluginHub.Services;

/// <summary>
/// The one value the hub sets in the website's own settings file, js\config.js in its folder:
/// shopOpen - licenses can be bought. The pages read it themselves: with it on, the "Buy"
/// buttons work and are the ones that stand out, the free trial steps back.
///
/// Only that word is changed; the rest of the file - contact, prices, the link to the
/// payment page - stays as it is written there.
/// </summary>
public static class WebsiteConfig
{
    private static readonly Regex ShopOpen = new(@"(\bshopOpen\s*:\s*)(true|false)\b", RegexOptions.Compiled);

    /// <summary>The file in the website's folder; null without a website, or one without that file.</summary>
    private static string? ConfigFile(string? site) =>
        site != null && Path.Combine(site, "js", "config.js") is var file && File.Exists(file) ? file : null;

    /// <summary>What the website's file says; null without a website or without that line.</summary>
    public static bool? ReadShopOpen(string? site)
    {
        if (ConfigFile(site) is not { } file) return null;

        var match = ShopOpen.Match(File.ReadAllText(file, Encoding.UTF8));
        return match.Success ? match.Groups[2].Value == "true" : null;
    }

    /// <summary>
    /// Writes the switch into the website's file. A folder without a website has nothing to
    /// switch; a website whose file has no such line is said so - the pages would go on
    /// showing "available soon" while the hub says the shop is open.
    /// </summary>
    public static void SetShopOpen(string? site, bool open)
    {
        if (ConfigFile(site) is not { } file) return;

        string text = File.ReadAllText(file, Encoding.UTF8);
        if (!ShopOpen.IsMatch(text))
            throw new HubError("In js\\config.js der Website fehlt die Zeile „shopOpen: true“ oder „shopOpen: false“ – " +
                               "der Hub kann den Kauf auf der Website nicht umschalten.");

        string changed = ShopOpen.Replace(text, m => m.Groups[1].Value + (open ? "true" : "false"), 1);
        if (changed != text) Json.WriteText(file, changed);
    }
}
