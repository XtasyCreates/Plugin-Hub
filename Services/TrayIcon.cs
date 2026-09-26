using WinForms = System.Windows.Forms;

namespace PluginHub.Services;

/// <summary>
/// The hub's icon in the notification area, next to the clock. The hub has no window, and
/// started with Windows it has no page open either - without the icon it looked as if it
/// was not running. A click opens the hub in the browser; the menu opens or ends it.
///
/// Windows Forms needs a thread with a message loop of its own; everything else in the hub
/// runs on ASP.NET's threads. A test hub (PLUGINHUB_PRIVATE_DIR) shows no icon.
/// </summary>
public static class TrayIcon
{
    public static void Show(string url, PhoneAccess phone, IHostApplicationLifetime lifetime)
    {
        if (Environment.GetEnvironmentVariable("PLUGINHUB_PRIVATE_DIR") is { Length: > 0 }) return;

        var thread = new Thread(() => Run(url, phone, lifetime))
        {
            IsBackground = true,
            Name         = "Tray icon"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void Run(string url, PhoneAccess phone, IHostApplicationLifetime lifetime)
    {
        try
        {
            WinForms.Application.EnableVisualStyles();

            // Marshals the hub's stop onto this thread, where the icon must be removed
            using var marshal = new WinForms.Control();
            marshal.CreateControl();

            using var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Hub öffnen", null, (_, _) => Desktop.OpenUrl(url));
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Hub beenden", null, (_, _) =>
            {
                if (phone.Enabled && WinForms.MessageBox.Show(
                        "Dein Handy erreicht den Hub dann erst wieder nach dem nächsten Windows-Start – " +
                        "oder wenn du ihn über die Verknüpfung startest.",
                        "Plugin Hub beenden?", WinForms.MessageBoxButtons.OKCancel,
                        WinForms.MessageBoxIcon.Question) != WinForms.DialogResult.OK)
                    return;
                lifetime.StopApplication();
            });

            using var icon = new WinForms.NotifyIcon
            {
                Icon             = LoadIcon(),
                Text             = "Plugin Hub läuft",
                ContextMenuStrip = menu,
                Visible          = true
            };
            icon.MouseClick += (_, e) =>
            {
                if (e.Button == WinForms.MouseButtons.Left) Desktop.OpenUrl(url);
            };

            // However the hub ends - page, menu, idle - the icon goes with it
            lifetime.ApplicationStopping.Register(() =>
            {
                try { marshal.BeginInvoke(() => { icon.Visible = false; WinForms.Application.ExitThread(); }); }
                catch { /* the loop is gone already */ }
            });

            WinForms.Application.Run();
            icon.Visible = false;
        }
        catch (Exception ex)
        {
            // Never worth the hub: it works the same without its icon
            Log.Error("Tray icon: " + ex.Message);
        }
    }

    /// <summary>The hub's own icon, in the small size the notification area shows.</summary>
    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            if (Environment.ProcessPath is string exe &&
                System.Drawing.Icon.ExtractAssociatedIcon(exe) is System.Drawing.Icon own)
                return new System.Drawing.Icon(own, WinForms.SystemInformation.SmallIconSize);
        }
        catch { }
        return System.Drawing.SystemIcons.Application;
    }
}
