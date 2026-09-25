using System.Security.Cryptography;

namespace PluginHub.Services;

/// <summary>
/// The to-do list: Hub\tasks.json, in Git next to the projects, so Claude reads the same list
/// in every session. Each task has a priority - hoch, mittel, niedrig - may belong to a project,
/// and names the place where it gets done, which the page turns into a one-click button.
/// </summary>
public sealed class Tasks
{
    public static readonly string[] Priorities = { "hoch", "mittel", "niedrig" };

    private readonly HubPaths _paths;
    private readonly Config _config;
    private readonly Places _places;
    private readonly object _gate = new();

    public Tasks(HubPaths paths, Config config, Places places)
    {
        _paths  = paths;
        _config = config;
        _places = places;
    }

    private string TasksFile => Path.Combine(_paths.DataDir, "tasks.json");

    public List<TaskItem> All()
    {
        lock (_gate) return Json.Read<List<TaskItem>>(TasksFile) ?? new List<TaskItem>();
    }

    public TaskItem Add(TaskRequest request) => Change(list =>
    {
        var task = new TaskItem
        {
            Id      = "t" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant(),
            Created = DateTimeOffset.Now
        };
        Apply(task, request);
        list.Add(task);
        return task;
    });

    public TaskItem Update(string id, TaskRequest request) => Change(list =>
    {
        var task = Find(list, id);
        Apply(task, request);
        return task;
    });

    public TaskItem SetDone(string id, bool done) => Change(list =>
    {
        var task = Find(list, id);
        task.Done   = done;
        task.DoneAt = done ? DateTimeOffset.Now : null;
        return task;
    });

    public void Delete(string id) => Change(list =>
    {
        list.Remove(Find(list, id));
        return true;
    });

    private void Apply(TaskItem task, TaskRequest request)
    {
        string title = request.Title?.Trim() ?? "";
        if (title.Length == 0) throw new HubError("Die Aufgabe braucht einen Titel.");
        if (!Priorities.Contains(request.Priority)) throw new HubError("Wichtigkeit: hoch, mittel oder niedrig.");
        if (!string.IsNullOrEmpty(request.Project) && _config.Current.Projects.All(p => p.Id != request.Project))
            throw new HubError($"Unbekanntes Projekt „{request.Project}“.");

        task.Title    = title;
        task.Note     = request.Note?.Trim() ?? "";
        task.Priority = request.Priority;
        task.Project  = string.IsNullOrEmpty(request.Project) ? null : request.Project;
        task.Goto     = request.Goto is { Kind.Length: > 0 } ? _places.Check(request.Goto) : null;
    }

    private T Change<T>(Func<List<TaskItem>, T> change)
    {
        lock (_gate)
        {
            var list = Json.Read<List<TaskItem>>(TasksFile) ?? new List<TaskItem>();
            var result = change(list);
            Json.Write(TasksFile, list);
            return result;
        }
    }

    private static TaskItem Find(List<TaskItem> list, string id) =>
        list.FirstOrDefault(t => t.Id == id) ?? throw new HubError("Diese Aufgabe gibt es nicht mehr.");
}

public sealed class TaskItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Note { get; set; } = "";
    /// <summary>hoch | mittel | niedrig</summary>
    public string Priority { get; set; } = "mittel";
    public string? Project { get; set; }
    /// <summary>Where it gets done - the button next to the task.</summary>
    public HubAction? Goto { get; set; }
    public bool Done { get; set; }
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
}

public sealed record TaskRequest(string? Title, string? Note, string Priority, string? Project, HubAction? Goto);
