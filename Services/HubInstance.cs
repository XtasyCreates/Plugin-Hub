using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;

namespace PluginHub.Services;

/// <summary>One hub per folder: the running one leaves its port in running.json.</summary>
public static class HubInstance
{
    public static int PreferredPort() =>
        int.TryParse(Environment.GetEnvironmentVariable("PLUGINHUB_PORT"), out int port) && port > 0 ? port : 51730;

    /// <summary>True when a hub for this folder already runs - it is shown instead of starting another.</summary>
    public static async Task<bool> ShowRunning(HubPaths paths, bool openBrowser)
    {
        var running = Json.Read<RunningHub>(paths.RunningFile);
        if (running == null) return false;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var hello = await http.GetFromJsonAsync<Hello>($"http://127.0.0.1:{running.Port}/api/hello");
            if (hello?.App != "PluginHub" || !string.Equals(hello.Root, paths.Root, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        catch { return false; }

        if (openBrowser) Desktop.OpenUrl($"http://127.0.0.1:{running.Port}/");
        return true;
    }

    public static void Announce(HubPaths paths, int port) =>
        Json.Write(paths.RunningFile, new RunningHub { Port = port, Pid = Environment.ProcessId });

    public static void Withdraw(HubPaths paths)
    {
        try { File.Delete(paths.RunningFile); } catch { }
    }

    public static bool IsFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException) { return false; }
    }

    public static int AnyFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed record Hello(string App, string Root);
}

public sealed class RunningHub
{
    public int Port { get; set; }
    public int Pid { get; set; }
}

/// <summary>
/// Ends the hub once no page has checked in for three minutes - it should not linger in the
/// background holding its .exe (a rebuild of the hub would fail). A running build keeps it
/// alive.
/// </summary>
public sealed class IdleWatch
{
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(3);

    private readonly Builds _builds;
    private long _lastTicks = DateTime.UtcNow.Ticks;

    public IdleWatch(Builds builds)
    {
        _builds = builds;
    }

    public void Touch() => Interlocked.Exchange(ref _lastTicks, DateTime.UtcNow.Ticks);

    public void Start(IHostApplicationLifetime lifetime)
    {
        Touch();
        _ = Task.Run(async () =>
        {
            var stopping = lifetime.ApplicationStopping;
            while (!stopping.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(15), stopping); }
                catch (TaskCanceledException) { return; }

                if (_builds.Running)
                {
                    Touch();
                    continue;
                }
                var last = new DateTime(Interlocked.Read(ref _lastTicks), DateTimeKind.Utc);
                if (DateTime.UtcNow - last > Grace)
                {
                    Log.Info("No page open for three minutes - stopping");
                    lifetime.StopApplication();
                    return;
                }
            }
        });
    }
}
