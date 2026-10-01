using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace PluginHub.Services;

/// <summary>
/// The customers' website (the Website folder next to the plug-ins) as a preview under
/// /website/ - on the PC and, with phone access on, on the phone. It only reads, and only what
/// belongs to the site (pages, styles, scripts, images, fonts); notes such as the README stay
/// unserved.
/// </summary>
public static class Website
{
    public const string Prefix = "/website";

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html",      [".css"]  = "text/css",        [".js"]   = "text/javascript",
        [".svg"]  = "image/svg+xml",  [".png"]  = "image/png",       [".jpg"]  = "image/jpeg",
        [".jpeg"] = "image/jpeg",     [".webp"] = "image/webp",      [".gif"]  = "image/gif",
        [".ico"]  = "image/x-icon",   [".woff2"] = "font/woff2",     [".txt"]  = "text/plain",
        [".pdf"]  = "application/pdf", [".mp4"] = "video/mp4"
    };

    public static void Serve(WebApplication app, HubPaths paths)
    {
        string folder = Path.Combine(paths.Root, "Website");
        if (!Directory.Exists(folder)) return;

        // "/website" without the slash gets a relative redirect - the built-in one is absolute
        // and would send the phone to http://, while it reaches the hub only over https
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.Equals(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Redirect(Prefix + "/");
                return;
            }
            await next();
        });

        var files = new PhysicalFileProvider(folder);
        app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = files,
            RequestPath  = Prefix,
            RedirectToAppendTrailingSlash = false
        });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            RequestPath  = Prefix,
            ContentTypeProvider = new FileExtensionContentTypeProvider(Types),
            // Edited while the hub runs - always hand out the current file
            OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-store"
        });
    }
}
