# Plugin Hub – Arbeitsregeln für Claude

Dieser Ordner ist der Plugin Hub: ein lokales Programm (C#, ASP.NET Core, Port 51730, nur
127.0.0.1), mit dem Gustav seine Rhino-Plugins plant, baut, verkauft und sichert. Wie er gebaut
ist, steht in `README.md` – zuerst lesen. Hier steht nur, was in keinem Quelltext steht: die Regeln.

## Wo was liegt

    Desktop\Claude\
      Plugins Rhino\   Laser Create, SW to Laser, RhinoKit – eigenes Git-Repository
      Plugin Hub\      dieser Ordner – eigenes Git-Repository; Daten in Hub\ (Planung, Einstellungen)
      Website\         Website „AutoCreate“ – eigenes Git-Repository
      TISS Bot\        eigenes Projekt, ohne Git

Der Hub braucht „Plugins Rhino“ daneben: Er kompiliert zwei Dateien aus `RhinoKit\Licensing` mit,
und alle Pfade in `Hub\projects.json` gelten ab dem Ordner über diesem.

## Was nie passieren darf

- **Den echten Hub nie aus einem Werkzeug starten** (Bash, PowerShell, Run-Knopf). Programme, die
  aus Claudes Werkzeugen starten, schreiben unter `%LOCALAPPDATA%` in eine eigene Sandbox – ein so
  gestarteter Hub hätte andere Schlüssel und ein anderes Kundenregister als Gustavs. Den Hub
  startet nur Gustav, über die Desktop-Verknüpfung.
- **Echte Schlüssel, Kunden, Codes und Backups nur über Gustavs laufenden Hub**: HTTP an
  `http://127.0.0.1:51730` mit den Headern `X-Plugin-Hub: 1` und `Origin: http://127.0.0.1:51730`.
  Private Schlüssel nie lesen, ausgeben oder in Git oder Klartext ablegen.
- **Tests nur in einer Kopie**: `PLUGINHUB_ROOT`, `PLUGINHUB_PRIVATE_DIR`, `PLUGINHUB_PORT`, dazu
  immer `PLUGINHUB_MAIL_PICKUP` (Mails als Datei) und `PLUGINHUB_TAILSCALE` (Ersatz – sonst kann ein
  Test-Hub die echte Weiterleitung ans Handy umbiegen). Nie eine echte Mail senden, nie in ein
  echtes Postfach einloggen. Passwörter trägt Gustav selbst ein.
- **Nichts hochladen**: nicht zu GitHub (das ist Gustavs Klick auf „Hochladen“), nicht in Rhinos
  Package Manager. Nicht committen, wenn er es nicht verlangt.
- **Keine System- oder Sicherheitseinstellungen ändern.** Den Autostart-Eintrag schreibt der Hub
  selbst, wenn Gustav ihn startet.

## Wie hier gearbeitet wird

- Fragen immer über den Auswahl-Dialog, nie als nummerierte Liste im Text. Neue Funktionen und
  Einstellungen erst vorschlagen und bestätigen lassen, dann bauen.
- Vor einem Build an Ort und Stelle muss der Hub beendet sein (`POST /api/quit`) – das unterbricht
  auch den Handy-Zugang, also nur wenn nötig, und Gustav sagen, dass er neu starten muss. Zum
  Ausprobieren woandershin bauen (`dotnet build -c Release -o <Scratch-Ordner>\`) und von dort als
  Test-Hub starten. Rhino und der Hub laden immer den Release-Build.
- Die Seiten in `wwwroot\` liest der laufende Hub direkt: Wer sie ändert, ändert Gustavs Hub
  sofort – passt die Änderung nicht zum laufenden Programm, erst in einer Kopie entwickeln.
- Planung pflegen (`Hub\roadmap.json`, `decisions.json`, `tasks.json`), am besten über die API des
  laufenden Hubs: Gebautes kommt als Karte auf „gebaut“ mit `built` und Test-Schritten, die sagen,
  **wo** jeder Schritt passiert; zu Beginn nach `problem` an Karten suchen; jede Aufgabe bekommt ein
  `goto`. Die Dateien direkt nur bei beendetem Hub bearbeiten, vorher eine Kopie ziehen.
- Aussehen ist Geschmackssache: erst als Entwurf zeigen (`design\`), dann fragen. Eine Seite
  zeichnet nie selbst neu, sie ruft `changed()`.
- Quelltext nie mit PowerShells `Get-Content`/`Set-Content` ändern – das zerstört Umlaute. Die
  Oberfläche ist deutsch, Kommentare im Quelltext sind englisch.
- Drei Repositories: Ein Stand wie „v55“ wird in jedem Repository committet und getaggt, das daran
  beteiligt ist. `Hub\`-Daten und die Dokumente committet der Hub selbst („Hub-Änderungen
  committen“).

## Mehr

Claudes ausführliche Notizen aus der gemeinsamen Arbeit liegen beim Projekt „Plugins Rhino“:
`C:\Users\gusta\.claude\projects\C--Users-gusta-Desktop-Claude-Plugins-Rhino\memory\`
(`plugin-hub-project.md`, `hub-roadmap-workflow.md`, `hub-design-drafts.md`, `tool-sandbox-writes.md`).
