using PluginHub.Services;

namespace PluginHub.Api;

/// <summary>
/// Keeps the hub to this PC and this page - it holds the private keys:
///  - the Host header must be 127.0.0.1 or localhost on the hub's port, so a web page that
///    points its own domain name at 127.0.0.1 (DNS rebinding) gets nothing;
///  - every changing request must carry X-Plugin-Hub. A page from elsewhere cannot add that
///    header without a CORS preflight, and this server never answers one.
/// </summary>
public sealed class LocalOnly
{
    private readonly RequestDelegate _next;
    private readonly IdleWatch _idle;
    private readonly HashSet<string> _hosts;
    private readonly HashSet<string> _origins;

    public LocalOnly(RequestDelegate next, int port, IdleWatch idle)
    {
        _next    = next;
        _idle    = idle;
        _hosts   = new(StringComparer.OrdinalIgnoreCase) { $"127.0.0.1:{port}", $"localhost:{port}" };
        _origins = new(StringComparer.OrdinalIgnoreCase) { $"http://127.0.0.1:{port}", $"http://localhost:{port}" };
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (!_hosts.Contains(request.Host.Value))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            string origin = request.Headers.Origin.ToString();
            if (request.Headers["X-Plugin-Hub"] != "1" || (origin.Length > 0 && !_origins.Contains(origin)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
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
