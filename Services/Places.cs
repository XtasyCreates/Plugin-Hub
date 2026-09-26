namespace PluginHub.Services;

/// <summary>
/// Where things get done: the places a task or a notice can lead to with one click - a spot on
/// a hub page (a settings field, the release of a plug-in), a file or folder, a build, a web
/// page. The task editor offers this list; whatever a task carries is checked against the same
/// rules, so a task can only open what the hub would open anyway.
/// </summary>
public sealed class Places
{
    private readonly Config _config;
    private readonly Documents _documents;

    public Places(Config config, Documents documents)
    {
        _config    = config;
        _documents = documents;
    }

    public List<Place> All()
    {
        var config  = _config.Current;
        var plugins = config.Projects.Where(p => p.IsPlugin).ToList();
        var list = new List<Place>();
        void Add(string group, string option, string kind, string target, string label) =>
            list.Add(new Place(group, option, new HubAction(kind, target, label)));

        const string settings = "Einstellungen";
        Add(settings, "Kontakt-Adresse für Kunden", "route", "#/einstellungen?feld=contact", "Zur Kontakt-Adresse");
        Add(settings, "Anbieter und Anschrift", "route", "#/einstellungen?feld=seller", "Zum Anbieter");
        Add(settings, "Standard-Testdauer", "route", "#/einstellungen?feld=trialDays", "Zur Testdauer");
        Add(settings, "Unterschrift unter Mails", "route", "#/einstellungen?feld=signature", "Zur Unterschrift");
        Add(settings, "E-Mail-Konto", "route", "#/einstellungen?feld=mail", "Zum E-Mail-Konto");
        Add(settings, "Mail-Vorlagen", "route", "#/einstellungen?feld=templates", "Zu den Mail-Vorlagen");
        Add(settings, "Handy-Zugang", "route", "#/einstellungen?feld=handy", "Zum Handy-Zugang");

        const string sales = "Verkauf";
        Add(sales, "Lizenzcode erzeugen", "route", "#/verkauf?feld=code-card", "Code erzeugen");
        Add(sales, "Kunden", "route", "#/kunden", "Zu den Kunden");
        Add(sales, "Mails", "route", "#/mails", "Zu den Mails");
        Add(sales, "Releases", "route", "#/releases", "Zu den Releases");
        foreach (var p in plugins)
            Add(sales, $"Release {p.Name}", "route", $"#/releases?feld=release-{p.Id}", $"Zum Release {p.Name}");

        const string keys = "Schlüssel und Backup";
        Add(keys, "Schlüssel", "route", "#/schluessel?feld=keys", "Zu den Schlüsseln");
        Add(keys, "Backup", "route", "#/schluessel?feld=backup", "Zum Backup");
        Add(keys, "Repository sichern (GitHub)", "route", "#/schluessel?feld=git-remote", "Zur Git-Sicherung");

        const string plan = "Planung";
        Add(plan, "Roadmap – alle Projekte", "route", "#/planung", "Zur Roadmap");
        foreach (var p in config.Projects)
            Add(plan, $"Roadmap – {p.Name}", "route", $"#/planung?projekt={p.Id}", $"Zur Roadmap {p.Name}");
        Add(plan, "Entscheidungen und offene Fragen", "route", "#/planung/entscheidungen", "Zu den Entscheidungen");
        Add(plan, "Versionen", "route", "#/planung/versionen", "Zu den Versionen");
        foreach (var d in _documents.List().Where(d => !d.ReadOnly))
            Add(plan, $"Dokument: {d.Name}", "route", $"#/planung/dokumente?datei={Uri.EscapeDataString(d.Path)}", "Dokument öffnen");

        const string projects = "Projekte";
        foreach (var p in config.Projects)
            Add(projects, p.Name, "route", $"#/projekt/{p.Id}", $"Zu {p.Name}");

        const string build = "Bauen";
        foreach (var p in plugins)
            Add(build, $"{p.Name} bauen", "build", p.Id, "Jetzt bauen");
        if (plugins.Count > 1)
            Add(build, "Alle Plugins bauen", "build", string.Join(",", plugins.Select(p => p.Id)), "Alle bauen");

        const string files = "Dateien und Ordner";
        foreach (var l in config.Links)
            Add(files, l.Label, "open", l.Id, OpenLabel(l.Kind));
        foreach (var p in config.Projects)
            foreach (var l in p.Links)
                Add(files, $"{p.Name}: {l.Label}", "open", l.Id, OpenLabel(l.Kind));

        return list;
    }

    /// <summary>
    /// A task's target as the hub accepts it: a hub page, a link it knows, a plug-in build or a
    /// web address. Returns it cleaned up, with a label when none was given.
    /// </summary>
    public HubAction Check(HubAction action)
    {
        string kind   = action.Kind?.Trim() ?? "";
        string target = action.Target?.Trim() ?? "";
        string label  = action.Label?.Trim() ?? "";
        if (label.Length > 60) throw new HubError("Die Beschriftung des Ziels ist zu lang (höchstens 60 Zeichen).");

        switch (kind)
        {
            case "route":
                if (!target.StartsWith("#/", StringComparison.Ordinal) || target.Length > 400 || target.Any(char.IsWhiteSpace))
                    throw new HubError("Ein Ziel im Hub beginnt mit „#/“.");
                return new HubAction(kind, target, label.Length > 0 ? label : "Hingehen");

            case "open":
                _config.Link(target);   // throws for anything the hub does not know
                return new HubAction(kind, target, label.Length > 0 ? label : "Öffnen");

            case "url":
                if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
                    throw new HubError("Eine Webadresse beginnt mit https://");
                return new HubAction(kind, uri.ToString(), label.Length > 0 ? label : "Webseite öffnen");

            case "build":
                var ids = target.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (ids.Length == 0 || ids.Any(id => !_config.Current.Projects.Any(p => p.Id == id && p.IsPlugin)))
                    throw new HubError("Bauen lassen sich nur Plugins aus projects.json.");
                return new HubAction(kind, string.Join(",", ids), label.Length > 0 ? label : "Jetzt bauen");

            default:
                throw new HubError($"Unbekannte Art von Ziel „{kind}“.");
        }
    }

    private static string OpenLabel(string kind) => kind switch
    {
        "folder" => "Ordner öffnen",
        "file"   => "Datei öffnen",
        "select" => "Im Explorer zeigen",
        "vscode" => "In VS Code öffnen",
        _        => "Öffnen"
    };
}

/// <summary>Group and Option: how the task editor lists it; Action: what the click does.</summary>
public sealed record Place(string Group, string Option, HubAction Action);

/// <summary>
/// One click somewhere. Kind: route (a hub page, "#/…", "?feld=" marks the spot), open (a link id
/// or "path:…"), url (a web page), build (plug-in ids, comma separated), commit-hub, push.
/// </summary>
public sealed record HubAction(string Kind, string Target, string Label);
