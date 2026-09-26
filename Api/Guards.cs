using PluginHub.Services;

namespace PluginHub.Api;

/// <summary>
/// Keeps the hub to this PC and this page - it holds the private keys - and, while phone
/// access is on, to the user's own phone:
///  - A request Tailscale Serve handed on carries X-Forwarded-Host (Serve always sets it) and is
///    a phone request, whatever else it says: its Host header is the sender's own and proves
///    nothing. It passes only when <see cref="PhoneAccess.Admits"/> says so.
///  - Any other request must name 127.0.0.1 or localhost on the hub's port as its host, so a web
///    page that points its own domain name at 127.0.0.1 (DNS rebinding) gets nothing.
///  - Every changing request must carry X-Plugin-Hub. A page from elsewhere cannot add that
///    header without a CORS preflight, and this server never answers one.
///  - From the phone, what works on this PC's keys, backups, builds, releases, repository and
///    files is refused (<see cref="PcOnly"/>).
/// </summary>
public sealed class LocalOnly
{
    /// <summary>Set on requests from the phone - <see cref="FromPhone"/> reads it.</summary>
    private const string PhoneKey = "hub.phone";

    // What Serve (or any proxy) adds - a browser on this PC never sends these
    private static readonly string[] ProxyHeaders =
    {
        "X-Forwarded-Host", "X-Forwarded-For", "X-Forwarded-Proto", "Forwarded",
        "Tailscale-User-Login", "Tailscale-Funnel-Request", "Tailscale-Headers-Info"
    };

    // Changing requests the phone may not make, and what it hears instead
    private static readonly (string Prefix, string Message)[] PcOnly =
    {
        ("/api/builds",   "Bauen geht nur am PC."),
        ("/api/releases", "Releases gehen nur am PC."),
        ("/api/keys",     "Schlüssel gehen nur am PC."),
        ("/api/backup",   "Backups gehen nur am PC."),
        ("/api/git/push", "Hochladen zu GitHub geht nur am PC."),
        ("/api/git/remote", "Das Repository richtest du am PC ein."),
        ("/api/open",     "Das öffnet sich nur am PC."),
        ("/api/rhino",    "Das geht nur am PC."),
        ("/api/phone",    "Den Handy-Zugang schaltest du am PC ein und aus."),
        ("/api/quit",     "Beenden geht nur am PC – sonst erreichst du den Hub vom Handy aus nicht mehr.")
    };

    private readonly RequestDelegate _next;
    private readonly IdleWatch _idle;
    private readonly PhoneAccess _phone;
    private readonly HashSet<string> _hosts;
    private readonly HashSet<string> _origins;

    public LocalOnly(RequestDelegate next, int port, IdleWatch idle, PhoneAccess phone)
    {
        _next    = next;
        _idle    = idle;
        _phone   = phone;
        _hosts   = new(StringComparer.OrdinalIgnoreCase) { $"127.0.0.1:{port}", $"localhost:{port}" };
        _origins = new(StringComparer.OrdinalIgnoreCase) { $"http://127.0.0.1:{port}", $"http://localhost:{port}" };
    }

    /// <summary>True for a request that came from the phone through Tailscale.</summary>
    public static bool FromPhone(HttpContext context) => context.Items.ContainsKey(PhoneKey);

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        bool phone  = ProxyHeaders.Any(request.Headers.ContainsKey);

        if (phone)
        {
            bool funnel = request.Headers.ContainsKey("Tailscale-Funnel-Request");
            if (!_phone.Admits(request.Headers["X-Forwarded-Host"], request.Headers["Tailscale-User-Login"], funnel))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            context.Items[PhoneKey] = true;
            Desktop.KeepAwake();      // a PC woken from the phone stays up while the phone uses it
        }
        else if (!_hosts.Contains(request.Host.Value))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        // The hub is never shown inside another page - no one can lay it under a fake button
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";

        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            string origin = request.Headers.Origin.ToString();
            bool fromHere = origin.Length == 0 ||
                            (phone ? string.Equals(origin, _phone.Origin, StringComparison.OrdinalIgnoreCase)
                                   : _origins.Contains(origin));
            if (request.Headers["X-Plugin-Hub"] != "1" || !fromHere)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (phone)
                foreach (var (prefix, message) in PcOnly)
                    if (request.Path.StartsWithSegments(prefix)) throw new HubError(message);
        }

        if (request.Path.StartsWithSegments("/api")) _idle.Touch();
        await _next(context);
    }
}

/// <summary>Turns failures into {"error": "..."} that the page shows as they are.</summary>
public sealed class ErrorsAsJson
{
    private readonly RequestDelegate _next;

    public ErrorsAsJson(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (HubError e)
        {
            await Write(context, StatusCodes.Status400BadRequest, e.Message);
        }
        catch (BadHttpRequestException)
        {
            await Write(context, StatusCodes.Status400BadRequest, "Die Anfrage war unvollständig.");
        }
        catch (Exception e)
        {
            Log.Error($"{context.Request.Method} {context.Request.Path}: {e}");
            await Write(context, StatusCodes.Status500InternalServerError, "Unerwarteter Fehler: " + e.Message);
        }
    }

    private static Task Write(HttpContext context, int status, string message)
    {
        if (context.Response.HasStarted) return Task.CompletedTask;
        context.Response.Clear();
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { error = message });
    }
}
