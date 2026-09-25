using System.Diagnostics;
using System.Text;

namespace PluginHub.Services;

/// <summary>
/// Everything the pages show about the projects, and what needs doing. Put together fresh
/// (a few git calls and file dates, well under a second) and kept for a moment, so the
/// side bar and the page asking at once cost one round.
/// </summary>
public sealed class Status
{
    private static readonly HashSet<string> SourceTypes =
        new(StringComparer.OrdinalIgnoreCase) { ".cs", ".csproj", ".props", ".targets", ".rui", ".svg", ".resx", ".xaml" };

    // How a private key looks in text: PKCS#8 of a P-256 key in base64, or PEM. Put together
    // at run time - written out whole, this file would report itself.
    private static readonly string[] PrivateKeyMarks =
    {
        "MIGHAgEAMBMGByqGSM49" + "AgEGCCqGSM49AwEHBG0wawIBAQQg",
        "BEGIN " + "PRIVATE KEY",
        "BEGIN EC " + "PRIVATE KEY"
    };

    private static readonly Dictionary<string, int> PriorityRank = new() { ["hoch"] = 0, ["mittel"] = 1, ["niedrig"] = 2 };

    private readonly HubPaths _paths;
    private readonly Config _config;
    private readonly KeyStore _keys;
    private readonly Register _register;
    private readonly HubState _state;
    private readonly Settings _settings;
    private readonly Tasks _tasks;
    private readonly Mailer _mailer;
    private readonly Releases _releases;

    private readonly object _gate = new();
    private OverviewView? _cached;
    private DateTime _cachedAt;

    public Status(HubPaths paths, Config config, KeyStore keys, Register register, HubState state,
        Settings settings, Tasks tasks, Mailer mailer, Releases releases)
    {
        _paths    = paths;
        _config   = config;
        _keys     = keys;
        _register = register;
        _state    = state;
        _settings = settings;
        _tasks    = tasks;
        _mailer   = mailer;
        _releases = releases;
    }

    public void Invalidate()
    {
        lock (_gate) _cached = null;
    }

    public OverviewView Overview()
    {
        lock (_gate)
        {
            if (_cached == null || DateTime.UtcNow - _cachedAt > TimeSpan.FromSeconds(1.5))
            {
                _cached   = Build();
                _cachedAt = DateTime.UtcNow;
            }
            return _cached;
        }
    }

    public ProjectDetailView ProjectDetail(string id)
    {
        var project  = _config.Project(id);
        var overview = Overview();
        var status   = overview.Projects.First(p => p.Id == project.Id);
        var log      = Git.Log(_paths.Root, project.Folder, 12);
        return new ProjectDetailView(status, log, overview.Health.Where(h => h.Project == project.Id).ToList());
    }

    /// <summary>The register with where each code stands today.</summary>
    public RegisterView RegisterView()
    {
        var data = _register.Snapshot();
        var today = DateOnly.FromDateTime(DateTime.Today);
        int soon = _settings.Shared.EndingSoonDays;
        var fingerprints = _config.Current.Projects.ToDictionary(p => p.Id, p => _keys.Info(p.Id)?.Fingerprint);
        return new RegisterView(data.Customers,
            data.Licenses.Select(l => new LicenseView(l, LicenseStatus.Of(l, fingerprints.GetValueOrDefault(l.Product), today, soon))).ToList(),
            data.Mails);
    }

    private OverviewView Build()
    {
        var config  = _config.Current;
        bool git    = Git.IsRepo(_paths.Root);
        var changes = git ? Git.Changes(_paths.Root) : new List<GitChange>();
        var rhino   = RhinoInfo.Running();

        var keys     = config.Projects.Where(p => p.IsPlugin && p.KeyFile != null).Select(KeyStatus).ToList();
        var backup   = BackupStatus();
        var projects = config.Projects.Select(p => ProjectStatus(p, git, changes)).ToList();
        var register = RegisterView();
        var due      = SafeDue();
        var tasks    = _tasks.All();

        var health = Health(config, projects, rhino, keys, backup, git, changes, register, due.Count)
            .OrderBy(h => h.Level == "problem" ? 0 : 1)
            .ToList();

        var openTasks = tasks.Where(t => !t.Done)
            .OrderBy(t => PriorityRank.GetValueOrDefault(t.Priority, 3)).ThenBy(t => t.Created).ToList();

        return new OverviewView(
            _paths.Root, _paths.PrivateDir, DateTimeOffset.Now, _settings.Shared.Signature,
            rhino, projects, health, keys, backup,
            new RegisterSummary(register.Customers.Count, register.Licenses.Count,
                register.Licenses.Count(l => !l.License.Paid && l.License.Price is > 0),
                register.Licenses.Count(l => l.License.Trial && l.Status.State is "valid" or "ending")),
            new MailSummary(_settings.MailReady, due.Count),
            new TaskSummary(openTasks.Count, openTasks.Count(t => t.Priority == "hoch"), openTasks.Take(5).ToList()),
            config.Links.Select(l => new LinkView(l.Id, l.Label, l.Kind)).ToList());
    }

    private List<DueMail> SafeDue()
    {
        try { return _mailer.Due(); }
        catch (HubError) { return new(); }   // a missing template must not take the overview down
    }

    private ProjectStatus ProjectStatus(ProjectDef p, bool git, List<GitChange> changes)
    {
        string prefix = p.Folder.Replace('\\', '/').TrimEnd('/') + "/";
        int changed = changes.Count(c => c.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !_releases.IsHubFile(c.Path));

        string? version = git && p.TagPattern != null ? Git.LatestTag(_paths.Root, p.TagPattern) : null;
        Commit? last    = git ? Git.Log(_paths.Root, p.Folder, 1).FirstOrDefault() : null;

        BuildInfo? build = null;
        var registrations = new List<Registration>();
        string? released = null;
        if (p.IsPlugin && p.Output != null)
        {
            string rhp = _paths.Resolve(p.Output);
            build = BuildInfo(p, rhp);
            registrations = RhinoInfo.Registrations(p, rhp);
            released = _releases.History(p).FirstOrDefault()?.Version;
        }

        return new ProjectStatus(p.Id, p.Name, p.Kind, p.Icon, p.Folder, p.Note, p.StartCommand, p.ActivationCommand,
            version, released, changed, last, build, registrations, RhinoInfo.ToolbarCache(p),
            p.Links.Select(l => new LinkView(l.Id, l.Label, l.Kind)).ToList());
    }

    private BuildInfo BuildInfo(ProjectDef p, string rhp)
    {
        bool exists = File.Exists(rhp);
        DateTime? rhpTime = exists ? File.GetLastWriteTime(rhp) : null;

        DateTime? newest = null;
        string? newestFile = null;
        var folders = p.Sources.Count > 0 ? p.Sources : new List<string> { p.Folder };
        foreach (string file in folders.SelectMany(f => Files.Walk(_paths.Resolve(f), SourceTypes)))
        {
            var time = File.GetLastWriteTime(file);
            if (newest == null || time > newest)
            {
                newest     = time;
                newestFile = _paths.Relative(file);
            }
        }

        // A second of slack: the build's copy and the file system round times differently
        bool outdated = exists && newest != null && newest.Value > rhpTime!.Value.AddSeconds(1);
        string? productVersion = exists ? FileVersionInfo.GetVersionInfo(rhp).ProductVersion : null;
        return new BuildInfo(_paths.Relative(rhp), exists, rhpTime, newest, newestFile, outdated, productVersion,
            exists && Files.IsLocked(rhp));
    }

    private KeyStatus KeyStatus(ProjectDef p)
    {
        var info = _keys.Info(p.Id);
        string? inSource = PluginActivationFile.ReadKey(_paths, p);
        return new KeyStatus(p.Id, p.Name, info != null, info?.Fingerprint, info?.Created, p.KeyFile,
            inSource != null, inSource != null ? KeyStore.Fingerprint(inSource) : null,
            info != null && inSource == info.PublicKey,
            info != null && PluginActivationFile.InBuild(_paths, p, info.PublicKey));
    }

    private BackupView BackupStatus()
    {
        var last = _state.LastBackup;
        return new BackupView(last,
            last != null && last.Keys != _keys.Fingerprints(),
            last != null && last.Register != _register.Hash());
    }

    private List<HealthItem> Health(HubConfig config, List<ProjectStatus> projects, List<RhinoProcess> rhino,
        List<KeyStatus> keys, BackupView backup, bool git, List<GitChange> changes, RegisterView register, int dueMails)
    {
        var items = new List<HealthItem>();
        var toKeys = new HealthAction("route", "#/schluessel", "Zu den Schlüsseln");

        // Private keys lying around in plain text
        foreach (string file in PlainTextKeys())
            items.Add(new("problem", "secrets", "Privater Schlüssel im Klartext",
                $"{file} enthält einen privaten Schlüssel. Private Schlüssel gehören nur verschlüsselt in den Hub.",
                null, new("open", "path:" + file, "Im Explorer zeigen")));

        // Keys: hub, plug-in source and built .rhp must agree
        foreach (var k in keys)
        {
            if (!k.InHub)
                items.Add(new("problem", "keys", $"{k.Name}: kein Schlüssel im Hub",
                    "Ohne Schlüssel kann der Hub für dieses Plugin keine Codes erzeugen.", k.Product,
                    new("route", "#/schluessel", "Schlüssel anlegen")));
            else if (!k.InSource)
                items.Add(new("problem", "keys", $"{k.Name}: Schlüssel fehlt im Plugin",
                    $"{k.KeyFile} fehlt. Das Plugin lässt sich so nicht bauen.", k.Product, toKeys));
            else if (!k.SourceMatches)
                items.Add(new("problem", "keys", $"{k.Name}: Plugin und Hub haben verschiedene Schlüssel",
                    $"Im Plugin steht {k.SourceFingerprint}, im Hub {k.Fingerprint}. Codes aus dem Hub würden abgelehnt.",
                    k.Product, toKeys));
            else if (!k.InBuild)
                items.Add(new("problem", "keys", $"{k.Name}: Die .rhp kennt den Schlüssel noch nicht",
                    "Codes aus dem Hub funktionieren erst mit einem neu gebauten Plugin.", k.Product,
                    new("build", k.Product, "Jetzt bauen")));
        }

        // Backup
        if (keys.Any(k => k.InHub))
        {
            var toBackup = new HealthAction("route", "#/schluessel", "Backup erstellen");
            if (backup.Last == null)
                items.Add(new("problem", "backup", "Noch kein Backup deiner Schlüssel",
                    "Geht dieser PC kaputt, kannst du ohne Backup keine Codes mehr erzeugen – auch nicht für bestehende Kunden.",
                    null, toBackup));
            else if (backup.KeysChanged)
                items.Add(new("problem", "backup", "Das Backup ist veraltet",
                    "Seit dem letzten Backup haben sich die Schlüssel geändert.", null, toBackup));
            else if (backup.RegisterChanged)
                items.Add(new("hint", "backup", "Kundenregister seit dem letzten Backup geändert",
                    "Die Schlüssel sind gesichert, die neuesten Einträge im Register noch nicht.", null, toBackup));
        }

        // Customers: follow-ups and trials running out
        if (dueMails > 0)
            items.Add(new(_settings.MailReady ? "hint" : "problem", "mail",
                dueMails == 1 ? "1 Nachfrage-Mail ist fällig" : $"{dueMails} Nachfrage-Mails sind fällig",
                _settings.MailReady ? "Testphasen enden bald – der Hub hat die Mails vorbereitet."
                                    : "Der Mailversand ist noch nicht eingerichtet (Einstellungen → E-Mail).",
                null, new("route", "#/mails", "Zu den Mails")));
        int ending = register.Licenses.Count(l => l.Status.State == "ending");
        if (ending > 0)
            items.Add(new("hint", "customers", ending == 1 ? "1 Code läuft bald ab" : $"{ending} Codes laufen bald ab",
                "Siehe Kunden – dort kannst du verlängern.", null, new("route", "#/kunden", "Zu den Kunden")));

        // Builds
        foreach (var p in projects.Where(p => p.Build != null))
        {
            var b = p.Build!;
            if (!b.Exists)
                items.Add(new("problem", "build", $"{p.Name}: noch nie gebaut", null, p.Id,
                    new("build", p.Id, "Jetzt bauen")));
            else if (b.Outdated)
                items.Add(new("hint", "build", $"{p.Name}: Der Quellcode ist neuer als die .rhp",
                    $"Zuletzt geändert: {b.NewestFile}", p.Id, new("build", p.Id, "Jetzt bauen")));

            if (b.Locked)
            {
                string holder = rhino.Count > 0 ? string.Join(", ", rhino.Select(r => r.Label).Distinct()) : "Ein Programm";
                items.Add(new("hint", "rhino", $"{holder} hält {p.Name} fest",
                    "Bauen geht erst, wenn Rhino geschlossen ist.", p.Id, null));
            }
        }

        // Which file each Rhino loads
        foreach (var p in projects)
            foreach (var r in p.Rhino)
            {
                if (r.State == "other")
                    items.Add(new("problem", "rhino", $"Rhino {r.Rhino} lädt {p.Name} aus einem anderen Ordner",
                        $"{r.File} – nicht der Release-Build. Änderungen kommen dort nicht an.", p.Id, null));
                else if (r.State == "missing")
                    items.Add(new("hint", "rhino", $"{p.Name} ist in Rhino {r.Rhino} nicht installiert",
                        $"Zum Testen die .rhp in ein Rhino-{r.Rhino}-Fenster ziehen.", p.Id,
                        p.Build != null ? new("open", "path:" + p.Build.Rhp, "Im Explorer zeigen") : null));
            }

        // Git
        if (!git)
        {
            items.Add(new("problem", "git", "Der Ordner ist kein Git-Repository",
                "Ohne Git lassen sich alte Stände nicht wiederherstellen.", null, null));
        }
        else
        {
            var hubFiles = changes.Where(c => _releases.IsHubFile(c.Path)).ToList();
            if (hubFiles.Count > 0)
                items.Add(new("hint", "git", "Änderungen des Hubs nicht committed",
                    string.Join(", ", hubFiles.Select(c => c.Path.TrimEnd('/')).Take(4)) + (hubFiles.Count > 4 ? " …" : ""),
                    null, new("commit-hub", "", "Jetzt committen")));

            foreach (var p in projects.Where(p => p.Changes > 0))
                items.Add(new("hint", "git", $"{p.Name}: {Count(p.Changes, "Änderung", "Änderungen")} nicht committed",
                    null, p.Id, null));

            var known = projects.Select(p => p.Folder.Replace('\\', '/').TrimEnd('/') + "/").ToList();
            foreach (var c in changes.Where(c => c.State == "??" && !_releases.IsHubFile(c.Path)
                                                 && !known.Any(k => c.Path.StartsWith(k, StringComparison.OrdinalIgnoreCase))))
                items.Add(new("hint", "git", $"Nicht in Git: {c.Path.TrimEnd('/')}", null, null,
                    new("open", "path:" + c.Path.TrimEnd('/'), "Im Explorer zeigen")));
        }

        // Leftovers that can go
        foreach (var c in config.Cleanup.Where(c => Directory.Exists(_paths.Resolve(c.Path)) || File.Exists(_paths.Resolve(c.Path))))
            items.Add(new("hint", "cleanup", $"Aufräumen: {c.Path}", c.Note, null,
                new("open", "path:" + c.Path, "Im Explorer zeigen")));

        return items;
    }

    private IEnumerable<string> PlainTextKeys()
    {
        foreach (string file in Files.Walk(_paths.Root, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs" }))
        {
            string text;
            try { text = File.ReadAllText(file, Encoding.UTF8); }
            catch (IOException) { continue; }
            if (PrivateKeyMarks.Any(m => text.Contains(m, StringComparison.Ordinal)))
                yield return _paths.Relative(file);
        }
    }

    private static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";
}

public sealed record OverviewView(
    string Root, string PrivateDir, DateTimeOffset Now, string Signature,
    List<RhinoProcess> Rhino,
    List<ProjectStatus> Projects,
    List<HealthItem> Health,
    List<KeyStatus> Keys,
    BackupView Backup,
    RegisterSummary Register,
    MailSummary Mail,
    TaskSummary Tasks,
    List<LinkView> Links);

public sealed record ProjectStatus(
    string Id, string Name, string Kind, string Icon, string Folder, string? Note, string? StartCommand,
    string? ActivationCommand, string? Version, string? Released, int Changes, Commit? LastCommit,
    BuildInfo? Build, List<Registration> Rhino, List<string> ToolbarCache, List<LinkView> Links);

public sealed record ProjectDetailView(ProjectStatus Project, List<Commit> Log, List<HealthItem> Health);

public sealed record BuildInfo(string Rhp, bool Exists, DateTime? RhpTime, DateTime? NewestSource, string? NewestFile,
    bool Outdated, string? ProductVersion, bool Locked);

public sealed record KeyStatus(string Product, string Name, bool InHub, string? Fingerprint, DateTimeOffset? Created,
    string? KeyFile, bool InSource, string? SourceFingerprint, bool SourceMatches, bool InBuild);

public sealed record BackupView(BackupRecord? Last, bool KeysChanged, bool RegisterChanged);

public sealed record RegisterSummary(int Customers, int Licenses, int Unpaid, int Trials);

public sealed record MailSummary(bool Ready, int Due);

public sealed record TaskSummary(int Open, int High, List<TaskItem> Top);

public sealed record RegisterView(List<Customer> Customers, List<LicenseView> Licenses, List<MailRecord> Mails);

public sealed record LinkView(string Id, string Label, string Kind);

/// <summary>Level: "problem" or "hint". Action kinds: route (a page), open (a link id), build (a project), commit-hub.</summary>
public sealed record HealthItem(string Level, string Area, string Title, string? Detail, string? Project, HealthAction? Action);

public sealed record HealthAction(string Kind, string Target, string Label);
