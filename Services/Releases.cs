using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PluginHub.Services;

/// <summary>
/// Releases. The version customers see lives in each plug-in's Version.props; a release builds
/// with it (no "-test"), packs the customer package - the built files from projects.json plus
/// guide, licence terms and changes, nothing else - into Releases\&lt;plug-in&gt;\&lt;version&gt;\, commits
/// that with a tag "&lt;id&gt;-&lt;version&gt;", and moves Version.props on to the next version. The roadmap
/// cards that were tested go along: they move to "veröffentlicht" in the same commit.
/// </summary>
public sealed class Releases
{
    private static readonly Regex VersionText  = new(@"<VersionPrefix>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</VersionPrefix>", RegexOptions.Compiled);
    private static readonly Regex VersionShape = new(@"^[0-9]+\.[0-9]+\.[0-9]+$", RegexOptions.Compiled);
    private static readonly Regex InternalTag  = new(@"^v\d+\s*[-–]\s*", RegexOptions.Compiled);
    private static readonly Regex Placeholder  = new(@"\{(\w+)\}", RegexOptions.Compiled);

    /// <summary>The to-do whose "done" means the licence terms were checked.</summary>
    public const string ReviewTask = "Lizenzbedingungen prüfen lassen";

    private readonly HubPaths _paths;
    private readonly Config _config;
    private readonly Settings _settings;
    private readonly Tasks _tasks;
    private readonly KeyStore _keys;
    private readonly Builds _builds;
    private readonly Plan _plan;

    public Releases(HubPaths paths, Config config, Settings settings, Tasks tasks, KeyStore keys, Builds builds, Plan plan)
    {
        _plan     = plan;
        _paths    = paths;
        _config   = config;
        _settings = settings;
        _tasks    = tasks;
        _keys     = keys;
        _builds   = builds;
    }

    public string? CurrentVersion(ProjectDef p)
    {
        if (p.VersionFile == null) return null;
        string file = _paths.Resolve(p.VersionFile);
        if (!File.Exists(file)) return null;
        var match = VersionText.Match(File.ReadAllText(file));
        return match.Success ? match.Groups[1].Value : null;
    }

    public List<ReleaseRecord> History(ProjectDef p)
    {
        string dir = Path.Combine(_paths.Root, "Releases", p.Name);
        if (!Directory.Exists(dir)) return new();
        return Directory.GetDirectories(dir)
            .Select(d => Json.Read<ReleaseRecord>(Path.Combine(d, "manifest.json")))
            .Where(r => r != null)
            .Select(r => r!)
            .OrderByDescending(r => ParseVersion(r.Version))
            .ToList();
    }

    public ReleaseState State(ProjectDef p)
    {
        var history = History(p);
        var last = history.FirstOrDefault();
        var since = Changes(p, last);
        string? build = null;
        if (p.Output != null && File.Exists(_paths.Resolve(p.Output)))
            build = FileVersionInfo.GetVersionInfo(_paths.Resolve(p.Output)).ProductVersion;
        return new ReleaseState(p.Id, p.Name, CurrentVersion(p), build, last, since, history);
    }

    public ReleasePlan Plan(ProjectDef p)
    {
        var state = State(p);
        string version = state.Version ?? "1.0.0";
        // What was tested reads better for customers than commit messages - those only fill in
        var tested = _plan.Roadmap().Where(x => x.Project == p.Id && x.Stage == "getestet").Select(x => x.Title).ToList();
        string changes = state.Last == null ? "Erste Version."
            : string.Join("\n", (tested.Count > 0 ? tested : state.Since).Select(s => "- " + s));
        return new ReleasePlan(p.Id, p.Name, version, state.Last?.Version, changes, Checks(p, version, state.Last));
    }

    public BuildJob Start(ProjectDef p, ReleaseRequest request)
    {
        if (!p.IsPlugin || p.VersionFile == null || p.Project == null || p.Output == null)
            throw new HubError($"{p.Name} lässt sich nicht veröffentlichen – es ist kein Plugin.");

        string version = request.Version?.Trim() ?? "";
        var last = History(p).FirstOrDefault();
        var blocking = Checks(p, version, last).Where(c => c.Blocking && !c.Ok).ToList();
        if (blocking.Count > 0) throw new HubError(string.Join(" ", blocking.Select(c => c.Detail ?? c.Label)));
        string changes = request.Changes?.Trim() ?? "";
        if (changes.Length == 0) throw new HubError("Schreib auf, was sich geändert hat – das steht im Kundenpaket.");

        return _builds.StartJob(new List<ProjectDef> { p }, $"Release {p.Name} {version}",
            job => Run(job, p, version, changes, request.Confirmed ?? new()));
    }

    private void Run(BuildJob job, ProjectDef p, string version, string changes, Dictionary<string, bool> confirmed)
    {
        string versionFile = _paths.Resolve(p.VersionFile!);
        string previous    = File.ReadAllText(versionFile);
        string folder      = Path.Combine(_paths.Root, "Releases", p.Name, version);
        string tag         = Tag(p, version);
        string baseCommit  = Git.Head(_paths.Root);
        string roadmapFile = _plan.RoadmapFile;
        string? roadmap    = File.Exists(roadmapFile) ? File.ReadAllText(roadmapFile) : null;
        int published      = 0;
        bool committed     = false;

        try
        {
            job.Add($"Version {version} in {p.VersionFile} eintragen");
            WriteVersion(versionFile, previous, version);

            if (!_builds.Build(job, p, release: true)) throw new HubError("Der Build ist fehlgeschlagen – siehe oben.");

            string built = FileVersionInfo.GetVersionInfo(_paths.Resolve(p.Output!)).ProductVersion ?? "";
            if (built != version) throw new HubError($"Die .rhp meldet Version „{built}“ statt {version}.");
            job.Add($"Die .rhp meldet Version {built}");

            job.Add("Kundenpaket schnüren");
            var record = Package(p, version, changes, folder, baseCommit, confirmed);
            job.Add($"{record.Zip}: {record.ZipSize / 1024} KB, {record.Files.Count} Dateien");

            // What was tested went out with this version
            published = _plan.Publish(p.Id, version);
            if (published > 0)
                job.Add(published == 1 ? "Roadmap: 1 getesteter Punkt steht jetzt auf „Veröffentlicht“"
                                       : $"Roadmap: {published} getestete Punkte stehen jetzt auf „Veröffentlicht“");

            job.Add($"Git: Commit und Tag {tag}");
            string relFolder = _paths.Relative(folder).Replace('\\', '/');
            string relVersion = p.VersionFile!.Replace('\\', '/');
            var files = new List<string> { relVersion, relFolder };
            if (published > 0) files.Add(_paths.Relative(roadmapFile).Replace('\\', '/'));
            Git.Exec(_paths.Root, new[] { "add", "--" }.Concat(files).ToArray());
            Git.Exec(_paths.Root, new[] { "commit", "-q", "-m", $"{p.Name} {version}", "-m",
                "Released with the Plugin Hub:\n\n" + changes, "--" }.Concat(files).ToArray());
            Git.Exec(_paths.Root, "tag", tag);
            committed = true;

            string next = NextPatch(version);
            job.Add($"Nächste Version: {next} (Testbuilds heißen ab jetzt {next}-test)");
            WriteVersion(versionFile, File.ReadAllText(versionFile), next);
            Git.Exec(_paths.Root, "commit", "-q", "-m", $"{p.Name}: next version {next}", "--", relVersion);

            job.Add($"Fertig: {_paths.Relative(Path.Combine(folder, record.Zip))}");
        }
        catch
        {
            // Nothing committed yet: leave the folder as it was before the release
            if (!committed)
            {
                File.WriteAllText(versionFile, previous);
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
                if (published > 0 && roadmap != null) File.WriteAllText(roadmapFile, roadmap);
                job.Add("Zurückgesetzt: Version.props, Release-Ordner und Roadmap wie vorher.");
            }
            throw;
        }
    }

    /// <summary>
    /// Files the hub itself writes - settings, tasks and templates under Hub\, and each plug-in's
    /// Activation.cs, and the documents at the top of the folder it edits - that are not committed yet.
    /// </summary>
    public List<string> DirtyHubFiles() =>
        Git.Changes(_paths.Root).Select(c => c.Path).Where(IsHubFile).ToList();

    /// <summary>Commits the hub's own files, on request - the only commit the hub makes outside a release.</summary>
    public int CommitHubFiles()
    {
        var files = DirtyHubFiles();
        if (files.Count == 0) return 0;
        Git.Exec(_paths.Root, new[] { "add", "--" }.Concat(files).ToArray());
        Git.Exec(_paths.Root, new[] { "commit", "-q", "-m", "Plugin Hub: Einstellungen, Aufgaben und Planung", "--" }
            .Concat(files).ToArray());
        Log.Info($"Committed hub files: {string.Join(", ", files)}");
        return files.Count;
    }

    public bool IsHubFile(string path)
    {
        string p = path.Replace('\\', '/');
        return p.StartsWith("Hub/", StringComparison.OrdinalIgnoreCase)
            || (!p.Contains('/') && p.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            || _config.Current.Projects.Any(x => x.KeyFile != null && string.Equals(x.KeyFile.Replace('\\', '/'), p, StringComparison.OrdinalIgnoreCase));
    }

    private List<ReleaseCheck> Checks(ProjectDef p, string version, ReleaseRecord? last)
    {
        var shared = _settings.Shared;
        var checks = new List<ReleaseCheck>();

        // What the build is made of must be committed; the hub's own files it can commit itself
        var folders = (p.Sources.Count > 0 ? p.Sources : new List<string> { p.Folder }).Select(f => f.TrimEnd('/') + "/");
        var dirty = Git.Changes(_paths.Root).Select(c => c.Path).ToList();
        var work = dirty.Where(d => !IsHubFile(d) && folders.Any(f => d.StartsWith(f, StringComparison.OrdinalIgnoreCase))).ToList();
        bool hubDirty = dirty.Any(IsHubFile);
        checks.Add(new("clean", "Alles committed", work.Count == 0 && !hubDirty, false, true,
            work.Count > 0
                ? $"Nicht committete Arbeit am Plugin: {string.Join(", ", work.Take(3))}{(work.Count > 3 ? " …" : "")}. Lass sie committen, bevor du veröffentlichst."
                : hubDirty ? "Geänderte Hub-Dateien (Einstellungen, Aufgaben, Planung) – „Hub-Änderungen committen“ erledigt das." : null));

        string? versionProblem = !VersionShape.IsMatch(version) ? $"„{version}“ ist keine Version wie 1.2.0."
            : Git.TagExists(_paths.Root, Tag(p, version)) ? $"Version {version} gibt es schon."
            : last != null && ParseVersion(version) <= ParseVersion(last.Version) ? $"Die Version muss über {last.Version} liegen."
            : null;
        checks.Add(new("version", "Version ist neu", versionProblem == null, false, true, versionProblem));

        var key = _keys.Info(p.Id);
        bool keyOk = key != null && PluginActivationFile.ReadKey(_paths, p) == key.PublicKey;
        checks.Add(new("key", "Schlüssel im Hub passt zum Plugin", keyOk, false, true,
            keyOk ? null : "Hub und Plugin haben nicht denselben Schlüssel (Schlüssel und Backup)."));

        bool seller = shared.Seller.Length > 0 && shared.SellerAddress.Length > 0;
        checks.Add(new("seller", "Anbieter eingetragen", seller, false, true,
            seller ? null : "Trag unter Einstellungen den Anbieter mit Anschrift ein – er steht in den Lizenzbedingungen."));

        bool reviewed = _tasks.All().Any(t => t.Title == ReviewTask && t.Done);
        checks.Add(new("legal", "Lizenzbedingungen geprüft", reviewed, false, false,
            reviewed ? null : "Die Aufgabe „Lizenzbedingungen prüfen lassen“ ist noch offen – das Paket enthält den ungeprüften Entwurf."));

        bool contact = shared.Contact.Length > 0;
        checks.Add(new("contact", "Kontakt-Adresse eingetragen", contact, false, false,
            contact ? null : "Ohne Kontakt-Adresse fehlt im Plugin „Request a code“ und in der Anleitung die Adresse."));

        var untested = _plan.Roadmap().Where(x => x.Project == p.Id && x.Stage == "gebaut").Select(x => x.Title).ToList();
        checks.Add(new("roadmap", "Alles Gebaute ist getestet", untested.Count == 0, false, false,
            untested.Count == 0 ? null
                : $"Noch nicht getestet: {string.Join(", ", untested.Take(3))}{(untested.Count > 3 ? $" und {untested.Count - 3} weitere" : "")} (Planung → Gebaut)."));

        checks.Add(new("rhino8", "In Rhino 8 getestet", false, true, false, null));
        checks.Add(new("rhino9", "In Rhino 9 getestet", false, true, false, null));
        checks.Add(new("activation", "Aktivierung auf einem anderen PC getestet", false, true, false, null));
        return checks;
    }

    private ReleaseRecord Package(ProjectDef p, string version, string changes, string folder, string baseCommit,
        Dictionary<string, bool> confirmed)
    {
        Directory.CreateDirectory(folder);
        var shared = _settings.Shared;
        string zipName = $"{p.PackageName ?? p.Name.Replace(" ", "")}-{version}.zip";
        string zipPath = Path.Combine(folder, zipName);
        string date    = DateTime.Now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        var built = p.Package.Select(_paths.Resolve).ToList();
        var missing = built.Where(f => !File.Exists(f)).ToList();
        if (missing.Count > 0) throw new HubError("Für das Paket fehlt: " + string.Join(", ", missing.Select(_paths.Relative)));

        var values = new Dictionary<string, string>
        {
            ["product"]           = p.Name,
            ["version"]           = version,
            ["date"]              = date,
            ["year"]              = DateTime.Now.Year.ToString(CultureInfo.InvariantCulture),
            ["rhp"]               = Path.GetFileName(built.FirstOrDefault(f => f.EndsWith(".rhp", StringComparison.OrdinalIgnoreCase)) ?? ""),
            ["command"]           = p.StartCommand ?? "",
            ["activationCommand"] = p.ActivationCommand ?? "",
            ["contact"]           = shared.Contact,
            ["seller"]            = shared.Seller,
            ["sellerAddress"]     = shared.SellerAddress,
        };

        var docs = new List<(string Name, string Text)>
        {
            ("Anleitung.html",        Fill(Template("templates/package/guide.de.html"), values, html: true)),
            ("Guide.html",            Fill(Template("templates/package/guide.en.html"), values, html: true)),
            ("Lizenzbedingungen.txt", Fill(Template("legal/Lizenzbedingungen.txt"), values, html: false)),
            ("License.txt",           Fill(Template("legal/License.txt"), values, html: false)),
            ("Changes.txt",           $"{p.Name} {version} – {date}\r\n\r\n{changes.Replace("\r\n", "\n").Replace("\n", "\r\n")}\r\n")
        };

        var files = new List<PackagedFile>();
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (string file in built)
            {
                zip.CreateEntryFromFile(file, $"{p.Name}/{Path.GetFileName(file)}", CompressionLevel.Optimal);
                files.Add(new PackagedFile(Path.GetFileName(file), new FileInfo(file).Length, Sha256(File.ReadAllBytes(file))));
            }
            foreach (var (name, text) in docs)
            {
                byte[] bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
                var entry = zip.CreateEntry($"{p.Name}/{name}", CompressionLevel.Optimal);
                using (var stream = entry.Open()) stream.Write(bytes);
                files.Add(new PackagedFile(name, bytes.Length, Sha256(bytes)));
            }
        }

        var record = new ReleaseRecord
        {
            Product    = p.Id,
            Name       = p.Name,
            Version    = version,
            Tag        = Tag(p, version),
            Date       = DateTimeOffset.Now,
            BaseCommit = baseCommit,
            Zip        = zipName,
            ZipSize    = new FileInfo(zipPath).Length,
            ZipSha256  = Sha256(File.ReadAllBytes(zipPath)),
            Files      = files,
            Checks     = confirmed,
            Changes    = changes
        };
        Json.Write(Path.Combine(folder, "manifest.json"), record);
        Json.WriteText(Path.Combine(folder, "CHANGES.md"), $"# {p.Name} {version}\n\n{changes}\n");
        return record;
    }

    private List<string> Changes(ProjectDef p, ReleaseRecord? last)
    {
        var folders = p.Sources.Count > 0 ? p.Sources : new List<string> { p.Folder };
        var own = new Regex($@"^{Regex.Escape(p.Name)}(:| \d)");
        return Git.SubjectsSince(_paths.Root, last?.Tag, folders)
            .Where(s => !own.IsMatch(s))                  // the release's own commits
            .Select(s => InternalTag.Replace(s, ""))      // "v45 - ..." is Claude's numbering
            .ToList();
    }

    private string Template(string relative)
    {
        string file = Path.Combine(_paths.DataDir, relative);
        if (!File.Exists(file)) throw new HubError($"Die Vorlage Hub\\{relative.Replace('/', '\\')} fehlt.");
        return File.ReadAllText(file, Encoding.UTF8);
    }

    private static string Fill(string text, Dictionary<string, string> values, bool html) =>
        Placeholder.Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var v)
            ? html ? WebUtility.HtmlEncode(v).Replace("\n", "<br>") : v
            : m.Value);

    public static string Tag(ProjectDef p, string version) => $"{p.Id}-{version}";

    private static void WriteVersion(string file, string text, string version)
    {
        if (!VersionText.IsMatch(text)) throw new HubError($"In {file} steht keine <VersionPrefix>.");
        Json.WriteText(file, VersionText.Replace(text, $"<VersionPrefix>{version}</VersionPrefix>", 1));
    }

    private static string NextPatch(string version)
    {
        var v = ParseVersion(version);
        return $"{v.Major}.{v.Minor}.{v.Build + 1}";
    }

    private static Version ParseVersion(string text) =>
        Version.TryParse(text, out var v) && v.Build >= 0 ? v : new Version(0, 0, 0);

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

/// <summary>manifest.json of a release: what went to customers, from which commit, with which checks.</summary>
public sealed class ReleaseRecord
{
    public string Product { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Tag { get; set; } = "";
    public DateTimeOffset Date { get; set; }
    public string BaseCommit { get; set; } = "";
    public string Zip { get; set; } = "";
    public long ZipSize { get; set; }
    public string ZipSha256 { get; set; } = "";
    public List<PackagedFile> Files { get; set; } = new();
    public Dictionary<string, bool> Checks { get; set; } = new();
    public string Changes { get; set; } = "";
}

public sealed record PackagedFile(string Name, long Size, string Sha256);

/// <summary>Manual checks are ticked on the page; blocking ones stop the release while not ok.</summary>
public sealed record ReleaseCheck(string Id, string Label, bool Ok, bool Manual, bool Blocking, string? Detail);

public sealed record ReleasePlan(string Product, string Name, string Version, string? LastVersion, string Changes, List<ReleaseCheck> Checks);

public sealed record ReleaseRequest(string? Version, string? Changes, Dictionary<string, bool>? Confirmed);

public sealed record ReleaseState(string Product, string Name, string? Version, string? BuildVersion, ReleaseRecord? Last,
    List<string> Since, List<ReleaseRecord> History);
