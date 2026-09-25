using System.Net;
using PluginHub.Api;
using PluginHub.Services;

// Plugin Hub: a small web server that only this PC can reach (http://127.0.0.1), used in
// the browser. A second start only shows the hub that is already running, and with no
// page open for three minutes it ends itself - so it never lingers holding its .exe.
//
//   --no-browser     start without opening a page (tests)
//   PLUGINHUB_ROOT, PLUGINHUB_PRIVATE_DIR, PLUGINHUB_PORT
//                    run against a copy: a test hub touches neither the real keys nor
//                    the real register

HubPaths paths;
try
{
    paths = HubPaths.Discover();
}
catch (Exception ex)
{
    Desktop.ShowError(ex.Message);
    return;
}

Log.Init(paths.LogFile);
bool openBrowser = !args.Contains("--no-browser");

if (await HubInstance.ShowRunning(paths, openBrowser)) return;

int preferred = HubInstance.PreferredPort();
int port = HubInstance.IsFree(preferred) ? preferred : HubInstance.AnyFreePort();
string url = $"http://127.0.0.1:{port}/";

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args            = args,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath     = paths.WebRoot
});
builder.Logging.ClearProviders();
builder.WebHost.UseKestrel(k => k.Listen(IPAddress.Loopback, port));
// A request the endpoints cannot read becomes an exception, which ErrorsAsJson turns into a
// message - by default it would be an empty 400 the page could only call "error 400"
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

builder.Services.AddSingleton(paths);
builder.Services.AddSingleton<Config>();
builder.Services.AddSingleton<Settings>();
builder.Services.AddSingleton<KeyStore>();
builder.Services.AddSingleton<Register>();
builder.Services.AddSingleton<HubState>();
builder.Services.AddSingleton<Backup>();
builder.Services.AddSingleton<Licenses>();
builder.Services.AddSingleton<Mailer>();
builder.Services.AddSingleton<Documents>();
builder.Services.AddSingleton<Places>();
builder.Services.AddSingleton<Tasks>();
builder.Services.AddSingleton<Plan>();
builder.Services.AddSingleton<Versions>();
builder.Services.AddSingleton<Builds>();
builder.Services.AddSingleton<RepoBackup>();
builder.Services.AddSingleton<Releases>();
builder.Services.AddSingleton<Status>();
builder.Services.AddSingleton<IdleWatch>();

var app = builder.Build();

app.UseMiddleware<ErrorsAsJson>();
app.UseMiddleware<LocalOnly>(port);
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // The page is edited in place while the hub runs - always hand out the current file
    OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-store"
});
app.MapHubApi();

try
{
    await app.StartAsync();
}
catch (Exception ex)
{
    Log.Error("Start failed: " + ex);
    Desktop.ShowError("Der Hub konnte nicht starten: " + ex.Message);
    return;
}

HubInstance.Announce(paths, port);
Log.Info($"Started on {url} - root {paths.Root}");
app.Services.GetRequiredService<IdleWatch>().Start(app.Lifetime);
if (openBrowser) Desktop.OpenUrl(url);

await app.WaitForShutdownAsync();
HubInstance.Withdraw(paths);
Log.Info("Stopped");
