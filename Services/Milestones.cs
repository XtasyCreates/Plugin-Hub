using System.Globalization;
using System.Security.Cryptography;
using RhinoKit.Licensing;

namespace PluginHub.Services;

/// <summary>
/// Milestones: the way to the first sales as a chain of marks on the overview - the first
/// release, a plug-in in Rhino's Package Manager, the first code request from outside, the
/// first trial, the first sale, ten sales - and marks of the user's own between them.
///
/// The marks of that way stamp themselves. Each is read from what the hub keeps anyway
/// (releases, packages.json, the code requests, the register) whenever the overview asks, so
/// none of it can go stale: delete a licence and its stamp is gone again. Codes and requests
/// for this very PC never count - that is the user trying things out.
///
/// Hub\milestones.json keeps only what the user decided: his own marks and the day each was
/// reached, which marks are hidden, their order, and from when on a kind of thing counts
/// ("neu zählen": what the hub held until then was a test). Neither the file nor the page gets
/// a name, an address or a device id - a title, a day, a count.
/// </summary>
public sealed class Milestones
{
    /// <summary>
    /// One mark of the way. Kind: what it counts - marks of one kind share "neu zählen".
    /// Goal: how many it takes. Open: what is missing, said in a few words, from how many there are.
    /// </summary>
    private sealed record WayMark(string Id, string Kind, string Title, int Goal, string Route, string Rule, Func<int, string> Open);

    private static readonly WayMark[] Way =
    {
        new("release", "release", "Erstes Release", 1, "#/releases",
            "Das erste Release eines Plugins, das du im Hub veröffentlichst.",
            _ => "noch kein Release"),
        new("package", "package", "Im Rhino Package Manager", 1, "#/releases",
            "Das erste Rhino-Paket, das du zum Package Manager hochlädst.",
            _ => "noch nicht hochgeladen"),
        new("request", "request", "Erste Code-Anfrage", 1, "#/mails?feld=anfragen",
            "Die erste Code-Anfrage im Postfach, die von einem anderen Gerät als diesem PC kommt. Was du ohne Code abgelegt hast, zählt nicht.",
            _ => "noch keine Anfrage"),
        new("trial", "trial", "Erste Testperson", 1, "#/kunden",
            "Der erste Testcode für ein anderes Gerät als diesen PC.",
            _ => "noch kein Testcode"),
        new("sale", "sale", "Erster Kauf", 1, "#/kunden",
            "Die erste unbefristete Lizenz für ein anderes Gerät als diesen PC – gezählt wie in der Statistik.",
            _ => "noch kein Kauf"),
        new("sales10", "sale", "10 Käufe", 10, "#/statistik",
            "Zehn Käufe, gezählt wie „Erster Kauf“.",
            n => 10 - n == 1 ? "noch 1 Kauf" : $"noch {10 - n} Käufe"),
    };

    private const int TitleLength = 48;

    // This PC's device id, as the plug-ins show it - asking Windows for it takes a moment
    private static readonly Lazy<string> ThisPc = new(MachineId.ForUser);

    private readonly HubPaths _paths;
    private readonly Config _config;
    private readonly Register _register;
    private readonly Inbox _inbox;
    private readonly Releases _releases;
    private readonly YakPackages _yak;
    private readonly object _gate = new();

    public Milestones(HubPaths paths, Config config, Register register, Inbox inbox, Releases releases, YakPackages yak)
    {
        _paths    = paths;
        _config   = config;
        _register = register;
        _inbox    = inbox;
        _releases = releases;
        _yak      = yak;
    }

    private string DataFile => Path.Combine(_paths.DataDir, "milestones.json");

    /// <summary>All marks in the user's order, the hidden ones too - the page leaves those out of the chain.</summary>
    public MilestonesView View()
    {
        MilestoneData data;
        lock (_gate) data = Read();
        var facts = Facts(data);

        var marks = Way.Select(m => Describe(m, facts[m.Kind], data))
            .Concat(data.Own.OrderBy(o => o.Created).Select(o => Describe(o, data)))
            .ToList();
        // What the user has not placed yet keeps its turn behind what he has
        int Place(MilestoneView m) => data.Order.IndexOf(m.Id) is >= 0 and var at ? at : int.MaxValue;
        return new MilestonesView(marks.OrderBy(Place).ToList());
    }

    // ── What the user decides ────────────────────────────────────────────────

    public MilestonesView Add(MilestoneRequest request) => Change(data =>
    {
        var mark = new OwnMilestone
        {
            Id      = "m" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant(),
            Created = DateTimeOffset.Now
        };
        Apply(mark, request);
        data.Own.Add(mark);
        // Behind everything, also when the others never were put in an order
        if (data.Order.Count > 0) data.Order.Add(mark.Id);
    });

    public MilestonesView Update(string id, MilestoneRequest request) => Change(data => Apply(Own(data, id), request));

    public MilestonesView Delete(string id) => Change(data =>
    {
        data.Own.Remove(Own(data, id));
        data.Hidden.Remove(id);
        data.Order.Remove(id);
    });

    public MilestonesView Hide(string id, bool hidden) => Change(data =>
    {
        Known(data, id);
        data.Hidden.Remove(id);
        if (hidden) data.Hidden.Add(id);
    });

    /// <summary>
    /// "Neu zählen": what the hub holds of this kind until now no longer counts - it was a test.
    /// Off again, everything counts as before.
    /// </summary>
    public MilestonesView Recount(string id, bool on) => Change(data =>
    {
        var mark = Way.FirstOrDefault(m => m.Id == id)
                   ?? throw new HubError("Neu zählen lässt sich nur, was der Hub selbst zählt – einen eigenen Meilenstein stempelst du selbst.");
        if (on) data.Since[mark.Kind] = DateTimeOffset.Now;
        else data.Since.Remove(mark.Kind);
    });

    public MilestonesView Reorder(List<string>? ids) => Change(data =>
    {
        var known = Way.Select(m => m.Id).Concat(data.Own.Select(o => o.Id)).ToHashSet();
        data.Order = (ids ?? new()).Where(known.Contains).Distinct().ToList();
    });

    private static void Apply(OwnMilestone mark, MilestoneRequest request)
    {
        string title = request.Title?.Trim() ?? "";
        if (title.Length == 0) throw new HubError("Der Meilenstein braucht einen Namen.");
        if (title.Length > TitleLength)
            throw new HubError($"Der Name ist zu lang für die Maßkette – höchstens {TitleLength} Zeichen, der Rest passt in die Notiz.");

        DateOnly? reached = null;
        if (!string.IsNullOrWhiteSpace(request.Reached))
        {
            if (!DateOnly.TryParseExact(request.Reached.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                throw new HubError($"„{request.Reached}“ ist kein Datum.");
            if (day > DateOnly.FromDateTime(DateTime.Today))
                throw new HubError("Dieser Tag liegt noch vor dir – stempeln lässt sich nur, was erreicht ist.");
            reached = day;
        }

        mark.Title   = title;
        mark.Note    = request.Note?.Trim() ?? "";
        mark.Reached = reached;
    }

    private static OwnMilestone Own(MilestoneData data, string id) =>
        data.Own.FirstOrDefault(o => o.Id == id) ?? throw new HubError("Diesen Meilenstein gibt es nicht mehr.");

    private static void Known(MilestoneData data, string id)
    {
        if (Way.All(m => m.Id != id) && data.Own.All(o => o.Id != id))
            throw new HubError("Diesen Meilenstein gibt es nicht mehr.");
    }

    private MilestonesView Change(Action<MilestoneData> change)
    {
        lock (_gate)
        {
            var data = Read();
            change(data);
            Json.Write(DataFile, data);
        }
        return View();
    }

    private MilestoneData Read() => Json.Read<MilestoneData>(DataFile) ?? new MilestoneData();

    // ── What the hub knows ───────────────────────────────────────────────────

    /// <summary>When something of a kind happened, and with which plug-in.</summary>
    private sealed record Fact(DateTimeOffset When, string? What);

    /// <summary>Per kind what counts, oldest first - without this PC's own, and from "neu zählen" on.</summary>
    private Dictionary<string, List<Fact>> Facts(MilestoneData data)
    {
        var plugins = _config.Current.Projects.Where(p => p.IsPlugin).ToList();
        string? Name(string? product) => plugins.FirstOrDefault(p => p.Id == product)?.Name;
        string self = ThisPc.Value;
        bool Outside(string machineId) => !string.Equals(machineId, self, StringComparison.OrdinalIgnoreCase);

        var licenses = _register.Snapshot().Licenses;
        var byId     = licenses.ToDictionary(l => l.Id);
        License? Previous(License l) => l.Replaces != null && byId.TryGetValue(l.Replaces, out var p) ? p : null;

        var all = new Dictionary<string, IEnumerable<Fact>>
        {
            ["release"] = plugins.SelectMany(p => _releases.History(p)).Select(r => new Fact(r.Date, $"{r.Name} {r.Version}")),
            ["package"] = _yak.Pushes().Select(u => new Fact(u.Pushed, $"{Name(u.Product) ?? u.Product} {u.Version}")),
            ["request"] = _inbox.State().Requests.Where(r => r.Status != "ignoriert" && Outside(r.MachineId))
                .Select(r => new Fact(r.Received, Name(r.Product))),
            ["trial"]   = licenses.Where(l => l.Trial && Outside(l.MachineId)).Select(l => new Fact(l.Issued, Suite.Name(l, _config.Current))),
            // A sale as the statistics count it (Stats.cs): a full licence that is new, or the one a trial turned into
            ["sale"]    = licenses.Where(l => !l.Trial && Previous(l) is not { Trial: false } && Outside(l.MachineId))
                .Select(l => new Fact(l.Issued, Suite.Name(l, _config.Current))),
        };

        return all.ToDictionary(kind => kind.Key, kind => kind.Value
            .Where(f => !data.Since.TryGetValue(kind.Key, out var since) || f.When > since)
            .OrderBy(f => f.When)
            .ToList());
    }

    private static MilestoneView Describe(WayMark m, List<Fact> facts, MilestoneData data)
    {
        // The one that made it: the first, or the tenth
        var hit = facts.Count >= m.Goal ? facts[m.Goal - 1] : null;
        return new MilestoneView(m.Id, m.Title, false, data.Hidden.Contains(m.Id),
            hit == null ? null : Day(hit.When), m.Goal == 1 ? hit?.What : null,
            m.Open(Math.Min(facts.Count, m.Goal)),
            m.Goal > 1 ? Math.Min(facts.Count, m.Goal) : null, m.Goal > 1 ? m.Goal : null,
            m.Route, m.Rule, m.Kind, data.Since.TryGetValue(m.Kind, out var since) ? Day(since) : null, null);
    }

    private static MilestoneView Describe(OwnMilestone o, MilestoneData data) =>
        new(o.Id, o.Title, true, data.Hidden.Contains(o.Id),
            o.Reached?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null, "offen", null, null, null, null, null, null,
            o.Note.Length > 0 ? o.Note : null);

    private static string Day(DateTimeOffset when) => when.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>Hub\milestones.json - what the user decided about the marks.</summary>
public sealed class MilestoneData
{
    public List<OwnMilestone> Own { get; set; } = new();
    public List<string> Hidden { get; set; } = new();
    /// <summary>The marks in the user's order; empty until he moves one.</summary>
    public List<string> Order { get; set; } = new();
    /// <summary>Per kind (release, package, request, trial, sale): counted only from then on.</summary>
    public Dictionary<string, DateTimeOffset> Since { get; set; } = new();
}

public sealed class OwnMilestone
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Note { get; set; } = "";
    /// <summary>The day the user stamped it with; null while it is open.</summary>
    public DateOnly? Reached { get; set; }
    public DateTimeOffset Created { get; set; }
}

public sealed record MilestonesView(List<MilestoneView> Marks);

/// <summary>
/// One mark of the chain. Reached: the day (yyyy-MM-dd), null while open. Detail: what reached
/// it ("Laser Create 1.0.0"). Open: what is missing, in a few words. Count of Goal: a mark that
/// counts. Route: where it gets done or shows. Rule: what counts. Kind: what it counts - marks
/// of one kind are counted anew together. Since: counted only from this day on. Own marks have
/// a Note instead of Route, Rule and Kind.
/// </summary>
public sealed record MilestoneView(string Id, string Title, bool Own, bool Hidden, string? Reached, string? Detail,
    string Open, int? Count, int? Goal, string? Route, string? Rule, string? Kind, string? Since, string? Note);

/// <summary>Reached: yyyy-MM-dd, or empty while it is open.</summary>
public sealed record MilestoneRequest(string? Title, string? Note, string? Reached);

public sealed record MilestoneHiddenRequest(bool Hidden);

public sealed record MilestoneRecountRequest(bool On);

public sealed record MilestoneOrderRequest(List<string>? Ids);
