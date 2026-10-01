using System.Security.Cryptography;

namespace PluginHub.Services;

/// <summary>
/// The planning: Hub\roadmap.json - what gets built, from the first idea to the release - and
/// Hub\decisions.json - what was decided and what is still open. Both live in Git next to the
/// projects, so Claude reads and writes the same lists: when it builds something it moves the
/// card to "gebaut" with the steps to test it, and what the user writes on a card after testing
/// is what it reads in the next session.
/// </summary>
public sealed class Plan
{
    /// <summary>The way of a card; "abgelehnt" keeps turned-down ideas so they do not come up again.</summary>
    public static readonly string[] Stages = { "idee", "entschieden", "arbeit", "gebaut", "getestet", "veroeffentlicht", "abgelehnt" };

    public static readonly Dictionary<string, string> StageNames = new()
    {
        ["idee"] = "Idee", ["entschieden"] = "Entschieden", ["arbeit"] = "In Arbeit", ["gebaut"] = "Gebaut",
        ["getestet"] = "Getestet", ["veroeffentlicht"] = "Veröffentlicht", ["abgelehnt"] = "Abgelehnt"
    };

    public static readonly string[] Kinds = { "funktion", "fehler" };

    private readonly HubPaths _paths;
    private readonly Config _config;
    private readonly object _gate = new();

    public Plan(HubPaths paths, Config config)
    {
        _paths  = paths;
        _config = config;
    }

    public string RoadmapFile   => Path.Combine(_paths.DataDir, "roadmap.json");
    public string DecisionsFile => Path.Combine(_paths.DataDir, "decisions.json");

    public List<RoadmapItem> Roadmap()
    {
        lock (_gate) return Json.Read<List<RoadmapItem>>(RoadmapFile) ?? new();
    }

    public List<DecisionItem> Decisions()
    {
        lock (_gate) return Json.Read<List<DecisionItem>>(DecisionsFile) ?? new();
    }

    // ── Roadmap ─────────────────────────────────────────────────────────────

    public RoadmapItem AddItem(RoadmapRequest request) => ChangeRoadmap(list =>
    {
        var now = DateTimeOffset.Now;
        var item = new RoadmapItem { Id = NewId("r"), Created = now, Changed = now, StageAt = now };
        Apply(item, request);
        list.Add(item);
        return item;
    });

    public RoadmapItem UpdateItem(string id, RoadmapRequest request) => ChangeRoadmap(list =>
    {
        var item = Find(list, id);
        Apply(item, request);
        item.Changed = DateTimeOffset.Now;
        return item;
    });

    /// <summary>
    /// Moves a card to a stage, before <paramref name="before"/> when that card is in the same
    /// stage, else to the end of the stage. Turning an idea down needs a reason.
    /// </summary>
    public RoadmapItem Move(string id, string stage, string? before, string? reason) => ChangeRoadmap(list =>
    {
        if (!Stages.Contains(stage)) throw new HubError($"Unbekannte Stufe „{stage}“.");
        var item = Find(list, id);
        if (!string.IsNullOrWhiteSpace(reason)) item.Reason = reason.Trim();
        if (stage == "abgelehnt" && string.IsNullOrWhiteSpace(item.Reason))
            throw new HubError("Schreib dazu, warum – dann taucht die Idee nicht noch einmal auf.");

        SetStage(item, stage);
        item.Changed = DateTimeOffset.Now;

        list.Remove(item);
        int at = before != null && before != id ? list.FindIndex(x => x.Id == before && x.Stage == stage) : -1;
        if (at < 0)
        {
            int last = list.FindLastIndex(x => x.Stage == stage);
            at = last < 0 ? list.Count : last + 1;
        }
        list.Insert(at, item);
        return item;
    });

    public RoadmapItem SetTest(string id, int index, bool done) => ChangeRoadmap(list =>
    {
        var item = Find(list, id);
        if (index < 0 || index >= item.Tests.Count) throw new HubError("Diesen Test-Schritt gibt es nicht mehr.");
        item.Tests[index].Done = done;
        item.Changed = DateTimeOffset.Now;
        return item;
    });

    public void DeleteItem(string id) => ChangeRoadmap(list =>
    {
        list.Remove(Find(list, id));
        return true;
    });

    /// <summary>
    /// A release took what was tested to customers: those cards move on to "veröffentlicht".
    /// Returns how many moved.
    /// </summary>
    public int Publish(string project, string version) => ChangeRoadmap(list =>
    {
        int moved = 0;
        foreach (var item in list.Where(x => x.Project == project && x.Stage == "getestet"))
        {
            SetStage(item, "veroeffentlicht");
            item.Release = version;
            item.Changed = DateTimeOffset.Now;
            moved++;
        }
        return moved;
    });

    private void Apply(RoadmapItem item, RoadmapRequest request)
    {
        string title = request.Title?.Trim() ?? "";
        if (title.Length == 0) throw new HubError("Der Punkt braucht einen Titel.");
        CheckProject(request.Project);
        string stage = request.Stage ?? item.Stage;
        if (!Stages.Contains(stage)) throw new HubError($"Unbekannte Stufe „{stage}“.");
        string kind = request.Kind ?? item.Kind;
        if (!Kinds.Contains(kind)) throw new HubError("Art: Funktion oder Fehler.");

        item.Title   = title;
        item.Project = string.IsNullOrEmpty(request.Project) ? null : request.Project;
        item.Note    = request.Note?.Trim() ?? "";
        item.Kind    = kind;
        item.Built   = Clean(request.Built);
        item.Release = Clean(request.Release);
        item.Problem = Clean(request.Problem);
        item.Reason  = Clean(request.Reason);
        if (request.Source != null) item.Source = Clean(request.Source);

        // Steps that stay the same keep their tick and their button; the order is the one typed
        if (request.Tests != null)
        {
            var old = item.Tests.GroupBy(t => t.Text).ToDictionary(g => g.Key, g => g.First());
            item.Tests = request.Tests.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct()
                .Select(t => old.TryGetValue(t, out var was)
                    ? new TestStep { Text = t, Done = was.Done, Goto = was.Goto }
                    : new TestStep { Text = t })
                .ToList();
        }

        if (stage == "abgelehnt" && item.Reason == null)
            throw new HubError("Schreib dazu, warum – dann taucht die Idee nicht noch einmal auf.");
        if (stage != item.Stage) SetStage(item, stage);
    }

    private static void SetStage(RoadmapItem item, string stage)
    {
        if (item.Stage == stage) return;
        item.Stage   = stage;
        item.StageAt = DateTimeOffset.Now;
        // Tested means it works now - an old finding would only mislead the next reader
        if (stage is "getestet" or "veroeffentlicht") item.Problem = null;
    }

    private T ChangeRoadmap<T>(Func<List<RoadmapItem>, T> change)
    {
        lock (_gate)
        {
            var list = Json.Read<List<RoadmapItem>>(RoadmapFile) ?? new();
            var result = change(list);
            Json.Write(RoadmapFile, list);
            return result;
        }
    }

    private static RoadmapItem Find(List<RoadmapItem> list, string id) =>
        list.FirstOrDefault(x => x.Id == id) ?? throw new HubError("Diesen Punkt gibt es nicht mehr.");

    // ── Decisions and open questions ────────────────────────────────────────

    public DecisionItem AddDecision(DecisionRequest request) => ChangeDecisions(list =>
    {
        var item = new DecisionItem { Id = NewId("d"), Created = DateTimeOffset.Now };
        Apply(item, request);
        list.Add(item);
        return item;
    });

    public DecisionItem UpdateDecision(string id, DecisionRequest request) => ChangeDecisions(list =>
    {
        var item = list.FirstOrDefault(x => x.Id == id) ?? throw new HubError("Diesen Eintrag gibt es nicht mehr.");
        Apply(item, request);
        return item;
    });

    public void DeleteDecision(string id) => ChangeDecisions(list =>
    {
        list.Remove(list.FirstOrDefault(x => x.Id == id) ?? throw new HubError("Diesen Eintrag gibt es nicht mehr."));
        return true;
    });

    private void Apply(DecisionItem item, DecisionRequest request)
    {
        string? question = Clean(request.Question);
        string? decision = Clean(request.Decision);
        if (question == null && decision == null) throw new HubError("Schreib eine Frage oder eine Entscheidung auf.");
        CheckProject(request.Project);

        string? date = Clean(request.Date);
        if (decision != null)
        {
            date ??= DateTime.Today.ToString("yyyy-MM-dd");
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out _)) throw new HubError($"„{date}“ ist kein Datum.");
        }

        item.Project  = string.IsNullOrEmpty(request.Project) ? null : request.Project;
        item.Question = question;
        item.Decision = decision;
        item.Date     = decision != null ? date : null;
        if (request.Source != null) item.Source = Clean(request.Source);
    }

    private T ChangeDecisions<T>(Func<List<DecisionItem>, T> change)
    {
        lock (_gate)
        {
            var list = Json.Read<List<DecisionItem>>(DecisionsFile) ?? new();
            var result = change(list);
            Json.Write(DecisionsFile, list);
            return result;
        }
    }

    // ── What the pages show about it ────────────────────────────────────────

    /// <summary>
    /// A project's cards counted by stage, and the one step that matters most now. Null project
    /// means the cards that belong to no project ("Allgemein").
    /// </summary>
    public PlanSummary Summary(string? project, List<RoadmapItem> roadmap, List<DecisionItem> decisions)
    {
        var mine = roadmap.Where(x => x.Project == project).ToList();
        var counts = Stages.ToDictionary(s => s, s => mine.Count(x => x.Stage == s));
        int problems = mine.Count(HasProblem);
        int questions = decisions.Count(d => d.Project == project && d.Decision == null);
        // Plug-ins and their library are tried in Rhino; the hub itself and the website in the browser
        string kind = _config.Current.Projects.FirstOrDefault(p => p.Id == project)?.Kind ?? "";
        string test = kind switch
        {
            "plugin" or "library" => "In Rhino testen",
            "tool"                => "Im Hub ausprobieren",
            "website"             => "Im Browser ansehen",
            _                     => "Testen"
        };
        return new PlanSummary(counts, problems, questions, Next(project, mine, test));
    }

    public static bool HasProblem(RoadmapItem x) => x.Problem != null && x.Stage is "arbeit" or "gebaut";

    private static NextStep? Next(string? project, List<RoadmapItem> mine, string test)
    {
        string filter = project != null ? $"projekt={project}&" : "projekt=allgemein&";
        string More(int n) => n > 1 ? $" (+{n - 1})" : "";

        var problem = mine.Where(HasProblem).ToList();
        if (problem.Count > 0)
            return new($"Problem aus deinem Test: {problem[0].Title}{More(problem.Count)}", $"#/planung?{filter}karte={problem[0].Id}", "problem");

        var built = mine.Where(x => x.Stage == "gebaut").ToList();
        if (built.Count > 0)
            return new($"{test}: {built[0].Title}{More(built.Count)}", $"#/planung?{filter}stufe=gebaut", "test");

        var working = mine.FirstOrDefault(x => x.Stage == "arbeit");
        if (working != null) return new($"In Arbeit: {working.Title}", $"#/planung?{filter}karte={working.Id}", "work");

        var decided = mine.Where(x => x.Stage == "entschieden").ToList();
        if (decided.Count > 0)
            return new($"Als Nächstes bauen: {decided[0].Title}{More(decided.Count)}", $"#/planung?{filter}karte={decided[0].Id}", "build");

        int tested = mine.Count(x => x.Stage == "getestet");
        if (tested > 0 && project != null)
            return new(tested == 1 ? "1 getestete Funktion wartet auf ein Release" : $"{tested} getestete Funktionen warten auf ein Release",
                $"#/releases?feld=release-{project}", "release");

        return null;
    }

    private void CheckProject(string? project)
    {
        if (!string.IsNullOrEmpty(project) && _config.Current.Projects.All(p => p.Id != project))
            throw new HubError($"Unbekanntes Projekt „{project}“.");
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string NewId(string prefix) =>
        prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
}

public sealed class RoadmapItem
{
    public string Id { get; set; } = "";
    /// <summary>A project id from projects.json; null for what belongs to no project.</summary>
    public string? Project { get; set; }
    public string Title { get; set; } = "";
    public string Note { get; set; } = "";
    /// <summary>idee | entschieden | arbeit | gebaut | getestet | veroeffentlicht | abgelehnt</summary>
    public string Stage { get; set; } = "idee";
    /// <summary>funktion | fehler</summary>
    public string Kind { get; set; } = "funktion";
    /// <summary>The build it came with - an internal tag like "v44".</summary>
    public string? Built { get; set; }
    /// <summary>The release that took it to customers - "1.0.0".</summary>
    public string? Release { get; set; }
    /// <summary>What to try in Rhino once it is built - Claude writes them, the user ticks them.</summary>
    public List<TestStep> Tests { get; set; } = new();
    /// <summary>What went wrong in the user's test - for Claude to read and fix.</summary>
    public string? Problem { get; set; }
    /// <summary>Why it was turned down - so it does not come up again.</summary>
    public string? Reason { get; set; }
    /// <summary>Where it came from: a document, a conversation, Claude's notes.</summary>
    public string? Source { get; set; }
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Changed { get; set; }
    /// <summary>When it reached its current stage.</summary>
    public DateTimeOffset? StageAt { get; set; }
}

public sealed class TestStep
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    /// <summary>Where the step is done, when that is in the hub - the button next to it. Set in the file, by Claude.</summary>
    public HubAction? Goto { get; set; }
}

public sealed class DecisionItem
{
    public string Id { get; set; } = "";
    public string? Project { get; set; }
    /// <summary>What was asked. Without a decision it is an open question.</summary>
    public string? Question { get; set; }
    public string? Decision { get; set; }
    /// <summary>The day it was decided, yyyy-MM-dd.</summary>
    public string? Date { get; set; }
    public string? Source { get; set; }
    public DateTimeOffset Created { get; set; }
}

/// <summary>Null Tests leaves the steps as they are; Source null leaves it too.</summary>
public sealed record RoadmapRequest(string? Title, string? Note, string? Project, string? Stage, string? Kind,
    string? Built, string? Release, List<string>? Tests, string? Problem, string? Reason, string? Source);

public sealed record MoveRequest(string Stage, string? Before, string? Reason);

public sealed record DecisionRequest(string? Project, string? Question, string? Decision, string? Date, string? Source);

public sealed record PlanSummary(Dictionary<string, int> Counts, int Problems, int OpenQuestions, NextStep? Next);

/// <summary>Kind: problem, test, work, build, release - how the page marks it.</summary>
public sealed record NextStep(string Text, string Route, string Kind);
