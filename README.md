# Plugin Hub

Die Schaltzentrale für die Rhino-Plugins im Ordner nebenan („Plugins Rhino“), die Website und
was sonst dazugehört: Planung (Roadmap, Entscheidungen, Versionen, Dokumente), Aufgaben,
Lizenzcodes (auch Testphasen), Kundenregister, Mails an Kunden, Releases mit Kundenpaket, Builds,
Schnellzugriff auf Dateien, Sicherung der Repositories und eine Liste, was gerade zu tun ist –
jeder Punkt darin mit einem Knopf, der dorthin führt, wo er erledigt wird.

## Ordner und Repositories

Seit dem 10.10.2026 liegt jedes Projekt in seinem eigenen Ordner, nebeneinander unter demselben
Ordner (bei dir `Desktop\Claude`), und jeder ist ein eigenes Git-Repository:

    Claude\
      Plugins Rhino\   Laser Create, SW to Laser, RhinoKit, Kundenpakete (Releases\)
      Plugin Hub\      dieses Programm – Quelltext, Seiten (wwwroot\) und Daten (Hub\)
      Website\         die Website für Kunden
      TISS Bot\        eigenes Projekt, ohne Git

- Der Hub findet seinen Ordner von der .exe aus aufwärts (der Ordner mit `Hub\projects.json`).
  Der Ordner **darüber** ist die Wurzel: Alle Pfade in `projects.json` sind relativ zu ihr
  (`Plugins Rhino/LaserExportPlugin`, `Website`, `Plugin Hub/Hub/legal`). Zieht alles zusammen um
  oder heißt ein Ordner anders, reicht es, `projects.json` anzupassen (`Services\HubPaths.cs`).
- Dokumente und Quelltext sucht der Hub nur in den Ordnern, die zu einem Projekt gehören – was
  sonst unter der Wurzel liegt, geht ihn nichts an (`Config.TopFolders`).
- Git fragt der Hub je Projekt: das nächste Repository über dessen Ordner (`Services\Repos.cs`).
  Offene Änderungen, letzter Commit und Version kommen aus diesem Repository; „Hub-Änderungen
  committen“ committet jede Datei dort, wo sie liegt; ein Release committet und taggt bei den
  Plugins und hält die Roadmap im Repository des Hubs fest.
- Die Geschichte von Hub und Website bis zum Umzug steht zweimal: in ihren neuen Repositories
  (mitgenommen) und im Repository der Plugins (dort ist sie entstanden). Die Versionsliste liest
  sie nur dort – `historySince` in `projects.json` nennt den letzten mitgebrachten Commit,
  `formerFolders` die alten Ordnernamen. Dieselbe Version in mehreren Repositories (`v55` in
  allen dreien) ist in der Liste eine.
- RhinoKit bleibt bei den Plugins. Der Hub kompiliert zwei Dateien daraus mit
  (`..\Plugins Rhino\RhinoKit\Licensing\…`, siehe `PluginHub.csproj`) – ohne den Ordner
  „Plugins Rhino“ daneben baut er nicht.
- `Hub\` und `design\` liegen im Projektordner, gehören aber nicht zum Programm
  (`DefaultItemExcludes` in `PluginHub.csproj`): Ein Web-Projekt würde sonst jede `.json` neben die
  .exe kopieren, und der Hub fände beim Suchen nach oben zuerst diese veraltete Kopie.

## Starten

Desktop-Verknüpfung **Plugin Hub**, oder `Plugin Hub\bin\Release\PluginHub.exe`.
Der Hub öffnet sich im Browser unter `http://127.0.0.1:51730` – nur dieser PC erreicht ihn
(und, mit Handy-Zugang, dein Handy über Tailscale, siehe unten).
Läuft er schon, zeigt ein zweiter Start nur den laufenden. Ist drei Minuten lang keine
Seite mehr offen, beendet er sich selbst – außer der Handy-Zugang ist an.

Solange er läuft, steht sein Symbol im Infobereich neben der Uhr (`Services\TrayIcon.cs`,
Windows Forms auf einem eigenen Thread): Klick öffnet den Hub, Rechtsklick „Hub öffnen“ und
„Hub beenden“. So sieht man auch einen Hub, der ohne Fenster mit Windows gestartet ist.

## Aussehen

Der Hub sieht aus wie ein Planblatt (seit 08.10.2026): Normschrift für Titel und Beschriftungen,
auf jeder Seite ein Plankopf, Linien statt Kästen, daneben die Leiste mit den Reitern – hell im
hellen Modus (eine Stufe dunkler als die Seite), Grafit im dunklen. Farbe hat drei
Aufgaben: Orange ist, was du tun kannst. Die Farbe eines Reiters zeigt, wo du bist (`--c-…` in
`hub.css`, `body[data-tab]` setzt `app.js`) – seine Zeile in der Leiste trägt sie dauerhaft, dazu
der Streifen über der Seite, die Kachel am Titel und was auf der Seite gewählt ist. Rot, Grün und
Gelb zeigen einen Zustand.

- **Einstellungen → Aussehen** schaltet Modus (Automatisch, Hell, Dunkel), Schrift, Flächen,
  Punktraster, die Tabelle im Plankopf, die Meilensteine auf der Übersicht und die Bewegung um. Das
  gilt je Gerät: `js\look.js` merkt es im Browser (`localStorage`, Eintrag `hub.look`) und setzt
  Klassen an `<html>`; `index.html` setzt sie schon vor dem ersten Bild.
- **Bewegung** (`js\motion.js`) ist ein Schalter des Hubs und hängt nicht an den Animationseffekten
  von Windows. Eine verschobene Karte oder abgehakte Aufgabe sitzt sofort am neuen Platz; der Hub
  wird nebenher informiert, danach zeichnet die Seite still neu. Was eine Seite unter `followed`
  nennt, gleitet bei einem Neuzeichnen an seinen neuen Platz.
- **Rahmen** (`js\app.js`): Plankopf (`markTab`) und zwei Spalten je Seite auf breiten Bildschirmen
  (`SPLITS`). Eine Seite zeichnet nie selbst neu, sie ruft `changed()` – sonst fehlt ihr der
  Plankopf.
- `css\hub.css` hat zwei Teile: erst die Palette und die Teile des Hubs mit ihrer Anordnung, dann –
  ab „Planblatt“ – ihr Aussehen. Wo beide etwas sagen, gilt der zweite Teil. Ein neues Teil bekommt
  Klassennamen mit eigener Vorsilbe (wie `.ms-…` bei den Meilensteinen): Kurze Namen wie `.next` sind
  schon vergeben, und `section:first-of-type` nimmt dem ersten Abschnitt einer Seite den Abstand.
- Die Entwürfe und der Stand vor dem Einbau liegen in `design\` (siehe `design\README.md`).

## Wo was liegt

| Was | Wo | In Git? |
|---|---|---|
| Quelltext des Hubs | `Plugin Hub\` | ja (Hub) |
| Projekte, Links, Paketinhalt | `Plugin Hub\Hub\projects.json` | ja (Hub) |
| Einstellungen (Kontakt, Anbieter, Testdauer …) | `Plugin Hub\Hub\settings.json` | ja (Hub) |
| Aufgaben (mit Ziel für den Knopf) | `Plugin Hub\Hub\tasks.json` | ja (Hub) |
| Roadmap-Karten | `Plugin Hub\Hub\roadmap.json` | ja (Hub) |
| Entscheidungen und offene Fragen | `Plugin Hub\Hub\decisions.json` | ja (Hub) |
| Mail-Vorlagen, Paket-Anleitung | `Plugin Hub\Hub\templates\` | ja (Hub) |
| Lizenzbedingungen (Entwurf, DE + EN) | `Plugin Hub\Hub\legal\` | ja (Hub) |
| Was im Package Manager von Rhino ist | `Plugin Hub\Hub\packages.json` | ja (Hub) |
| Meilensteine: eigene, ausgeblendete, Reihenfolge, „Neu zählen“ | `Plugin Hub\Hub\milestones.json` | ja (Hub) |
| Quelltext der Plugins, `Activation.cs`, `Version.props` | `Plugins Rhino\` | ja (Plugins) |
| Kundenpakete (ZIP und Rhino-Paket .yak) und ihre Manifeste | `Plugins Rhino\Releases\<Plugin>\<Version>\` | ja (Plugins) |
| Website für Kunden (statisch, Vorschau unter `/website/`) | `Website\` | ja (Website) |
| Private Schlüssel (DPAPI-verschlüsselt) | `%LOCALAPPDATA%\PluginHub\keys\` | **nein** |
| Kundenregister mit Mail-Protokoll | `%LOCALAPPDATA%\PluginHub\register.json` | **nein** |
| Code-Anfragen aus dem Postfach | `%LOCALAPPDATA%\PluginHub\inbox.json` | **nein** |
| Mailkonto (Passwort DPAPI-verschlüsselt) | `%LOCALAPPDATA%\PluginHub\mail.json`, `mail.secret` | **nein** |
| Handy-Zugang (Name im Tailnet, erlaubtes Konto) | `%LOCALAPPDATA%\PluginHub\phone.json` | **nein** |
| Backups (mit Passwort verschlüsselt) | `%LOCALAPPDATA%\PluginHub\Backups\` | **nein** |
| Letztes Hochladen zu GitHub | `%LOCALAPPDATA%\PluginHub\state.json` | nein |
| Protokoll | `%LOCALAPPDATA%\PluginHub\hub.log` | nein |

Die privaten Schlüssel lassen sich nur unter diesem Windows-Konto auf diesem PC lesen.
Auf einem neuen PC holt man sie mit „Backup wiederherstellen“ zurück – ohne Backup und
dessen Passwort sind sie verloren. Das Mail-Passwort ist nicht im Backup.

„In Git?“ nennt das Repository. Ändert der Hub Dateien in Git (Einstellungen, Aufgaben, Planung,
Vorlagen, `Activation.cs`, die Dokumente oben in einem Projektordner – nicht `README.md` und
`CLAUDE.md`, die Claude pflegt), zeigt die Übersicht „Änderungen des Hubs nicht committed“ mit
einem Knopf, der genau diese Dateien committet – jede in ihrem Repository. Sonst committet der
Hub nur beim Release.

## Statistik

Der Tab „Statistik“ zählt Verkäufe und Umsatz, Testphasen mit dem Anteil, der danach gekauft hat,
und die Code-Anfragen aus dem Postfach – je Monat, gefiltert nach Zeitraum (12 Monate / alles) und
Plugin. `GET /api/stats` (`Services\Stats.cs`) gibt dafür nur anonyme Zeilen aus (Monat, Plugin,
Weg, Betrag), nie Namen, Adressen oder Geräte-IDs; die Seite rechnet sie zusammen. Regeln: Ein Kauf
ist eine unbefristete Lizenz, die neu ist oder aus einer Testphase wird – ein Code, der nur einen
gekauften ersetzt, zählt nicht. Eine Testphase zählt je Gerät einmal, auch verlängert; „gekauft“,
wenn ihre Kette in einer Lizenz endet oder dieselbe Person oder dasselbe Gerät das Plugin danach kauft.

Der **Verkaufsweg** („Per Mail“ / „Online“) steht in `Hub\settings.json` (`salesChannel`) und wird in
den Einstellungen umgestellt. Jeder neue Code merkt sich ihn (`channel` in register.json; alte ohne
gelten als „Per Mail“). `siteStats` ist die Quelle der Website-Aufrufe – „off“, bis die Seite online
ist; der Tab hat den Bereich dafür schon.

**Kauf geöffnet** (`shopOpen`, dieselbe Karte in den Einstellungen): Ab dann ist „Buy“ überall der
orange Knopf und „Free trial“ der schlichte. Der Hub schreibt den Schalter an zwei Stellen –
`js\config.js` im Ordner der Website (`shopOpen`, nur dieses eine Wort; `Services/WebsiteConfig.cs`) und
`Activation.cs` beider Plugins (`ShopOpen`, wirkt mit dem nächsten Build; `PluginActivationFile`).
Auf der Website macht derselbe Schalter die Kauf-Knöpfe und das Kaufformular aktiv – erst umlegen,
wenn Preise und Zahlungsweg stehen.

## Meilensteine

Unter den Kennzahlen der Übersicht steht der Weg zum Verkauf als Maßkette (`js\milestones.js`,
`Services\Milestones.cs`, `GET /api/milestones`): je Meilenstein ein Strich mit seinem Namen, darunter
der Stempel mit dem Tag, an dem er erreicht war, und zwischen zwei erreichten die Tage dazwischen. Ein
Klick auf einen Meilenstein führt dorthin, wo er erreicht wird.

- **Sechs stempelt der Hub selbst** und liest sie bei jeder Anfrage frisch – gespeichert wird davon
  nichts, also kann nichts veralten: Erstes Release (ältestes Manifest unter `Releases\`), Im Rhino
  Package Manager (`Hub\packages.json`), Erste Code-Anfrage (inbox.json), Erste Testperson und Erster
  Kauf (register.json), 10 Käufe (der Tag des zehnten). Ein Kauf zählt wie in der Statistik. Was für
  **diesen PC** ausgestellt oder angefragt wurde (`MachineId.ForUser()`), zählt nie; eine ohne Code
  abgelegte Anfrage auch nicht.
- **Eigene** legt man unter „bearbeiten“ an und stempelt sie selbst – mit einem Tag, der nicht in der
  Zukunft liegt.
- `Hub\milestones.json` hält nur, was entschieden wurde: `own`, `hidden`, `order` und `since`. `since`
  ist „Neu zählen“ je Art (release, package, request, trial, sale): Was der Hub bis dahin dazu kannte,
  war ein Test. „Erster Kauf“ und „10 Käufe“ zählen dieselbe Art und fangen gemeinsam neu an. Namen,
  Adressen und Geräte-IDs stehen weder in der Datei noch in der Antwort an die Seite.
- Ein neu erreichter Meilenstein wird **je Gerät einmal** gemeldet (`localStorage`, Eintrag
  `hub.milestones`; der erste Blick merkt sich nur den Stand). Wo die Kette unter Einstellungen →
  Aussehen ausgeschaltet ist, fragt die Seite sie gar nicht erst ab.
- Ein weiterer automatischer Meilenstein ist eine Zeile in `Way` (`Milestones.cs`): Kennung, Art, Name,
  wie viele es braucht, wohin der Klick führt, was zählt – und, falls die Art neu ist, ihre Quelle in
  `Facts`.

## Website

Die Website für Kunden liegt im Ordner `Website\` neben dem des Hubs und ist im Hub ein eigenes
Projekt (Art `website`, Weltkugel); wo ihr Ordner liegt, sagt `folder` dieses Projekts in
`projects.json`. Der Hub gibt den Ordner unter `/website/` aus – am PC und, mit Handy-Zugang, am Handy.
„Website ansehen“ auf der Projektseite öffnet sie in einem neuen Tab. Ausgegeben werden nur
Website-Dateien (HTML, CSS, Skripte, Bilder, Schriften); die README im Ordner bleibt unsichtbar.
`/website` ohne Schrägstrich leitet relativ weiter, damit das Handy bei https bleibt
(`Services\Website.cs`). Der Plan bis zum Live-Gang: `Website\Website Plan 01_10_26.md` und die
Karten des Projekts.

## Eigene Projekte (Art `extra`)

Ein Projekt mit `"kind": "extra"` in `Hub\projects.json` gehört nicht zum Plugin-Geschäft – der
TISS Bot ist so eins. Sein Ordner liegt wie die anderen unter der Wurzel: `"folder": "TISS Bot"`.
Er hat kein Git – Verlauf und offene Änderungen zeigt der Hub dann nicht, die Projektseite sagt
„liegt in keinem Git-Repository“. Es steht in der Seitenleiste unter
„Projekte“ und hat seine Roadmap in der Planung. Die Übersicht zeigt keine Karte dafür, und weder
seine offenen Git-Änderungen noch seine Karten erzeugen dort Hinweise (`Services\Status.cs`).
`url` und `urlLabel` geben der Projektseite einen Knopf zur eigenen Seite des Projekts; mit
`phonePort` führt er am Handy zu `https://<dieser PC im Tailnet>:<Port>/`.

**Committen und hochladen**: Ein eigenes Projekt, dessen Ordner selbst das Git-Repository ist
(MilkDrop), bekommt auf seiner Projektseite unter „Stand“ einen Knopf – „Committen und hochladen“,
solange etwas offen ist, „Hochladen“, wenn nur noch Commits fehlen, sonst keinen. Der Dialog fragt nach
der Beschreibung (Vorschlag „Stand <Datum>“) und hat einen Haken „Danach zu GitHub hochladen“.
`POST /api/git/commit-project` nimmt **alles** Offene dieses Repositorys in einen Commit (`git add -A`,
`.gitignore` gilt; `Repos.CommitProject`), `POST /api/git/push/{id}` lädt nur dieses eine Repository
hoch (`RepoBackup.Push(project)`, als Job wie „Hochladen“, nur am PC). Für die Plugins, den Hub und
die Website gibt es das nicht: Dort ist ein Commit ein Versionsstand über mehrere Repositories.

**Weitergabe** (`shareScript` und `shareFolder`, beide ab der Wurzel – MilkDrop hat sie): Die
Projektseite bekommt die Karte „Weitergabe“ mit dem Knopf „Paket erstellen“. Der Hub startet dafür das
PowerShell-Skript des Projekts als Job wie einen Build (`POST /api/share/{id}`, `Services\Share.cs`;
nur am PC, und nie zwei Jobs zugleich) und zeigt seine Ausgabe unten rechts. Was in ein Paket gehört
und wie eine Version heißt, bestimmt allein das Skript: Es legt je Lauf **einen neuen Unterordner**
in `shareFolder` an und eine ZIP gleichen Namens daneben, und endet mit einem Fehlercode, wenn etwas
nicht klappt. Die Karte listet die Unterordner auf, den neuesten zuerst, mit „ZIP zeigen“ und
„Ordner“ (Explorer). Den Ordner hält das Projekt selbst aus Git heraus.

## Einstellungen

Die Seite hat Bereiche (`?bereich=<id>`): „Hub allgemein“ (`allgemein`: Anbieter und Kontakt,
E-Mail, Mail-Vorlagen, Handy-Zugang), je Plugin einen, „Website“ (Verkaufsweg) und je eigenem
Projekt einen, der nur zu dessen Seite führt. Ein Link auf ein Feld (`?feld=`) öffnet den Bereich,
in dem es steht.

Testdauer, Nachfrage und „Läuft bald ab“ gelten je Plugin: `products` in `Hub\settings.json`,
nach Projekt-id. Ein Plugin ohne Eintrag nimmt die gemeinsamen Werte daneben (`trialDays`,
`followUpDays`, `endingSoonDays`) – so wie alle, bevor es eigene gab. „Für alle Plugins übernehmen“
setzt die Werte eines Plugins bei allen und als gemeinsame. Wer einen Wert braucht, fragt
`HubSettings.For(produkt)` bzw. auf der Seite `productSettings(shared, produkt)` (`store.js`).

## Planung

Der Tab „Planung“ hat vier Bereiche, alle nach Projekt filterbar:

- **Roadmap:** ein Board mit den Stufen Idee → Entschieden → In Arbeit → Gebaut → Getestet →
  Veröffentlicht; Karten zieht man weiter. Abgelehntes liegt darunter, mit Grund. Karten in
  „Gebaut“ tragen Test-Schritte: Claude schreibt sie beim Bauen, du hakst sie in Rhino ab. Ein
  Schritt, der im Hub passiert, hat einen Knopf dorthin (`goto` am Schritt); die Seite zeigt ihn
  dann oben mit „Abgehakt“. Was
  nicht klappt, kommt ins Feld „Was beim Test nicht geklappt hat“ – Claude liest es in der
  nächsten Sitzung, oder du kopierst den Auftrag von der Karte in den Chat. Ein Release schiebt
  die getesteten Karten seines Plugins auf „Veröffentlicht“ und füllt damit die Änderungsliste vor.
- **Entscheidungen:** was entschieden ist, mit Datum, und offene Fragen. Eine beantwortete Frage
  wird zur Entscheidung, auf Wunsch gleich mit einer Roadmap-Karte.
- **Versionen:** aus den Git-Tags aller Repositories, mit den Karten, die in einer Version gebaut
  wurden.
- **Dokumente:** alle `.md` in den Projektordnern und die Lizenzbedingungen – lesen und
  bearbeiten; ihr Pfad gilt ab der Wurzel (`Website/Website Plan 01_10_26.md`). Was ein Release
  archiviert hat, bleibt unverändert.

Für Claude: Wer ein Feature baut, setzt die Karte auf `gebaut`, trägt `built` (den Tag, z. B.
`v48`) und `tests` ein; wer ein gemeldetes Problem behebt, leert `problem` und setzt die Häkchen
zurück. Die Felder beschreibt `Services\Plan.cs`.

## Aufgaben und ihre Knöpfe

Jede Aufgabe kann ein Ziel haben (`goto` in tasks.json): eine Stelle im Hub (`route`, z. B.
`#/einstellungen?feld=contact` – „feld“ ist ein Feldname oder eine Karten-id), einen Link aus
projects.json (`open`), einen Build (`build`) oder eine Webseite (`url`). Führt der Knopf auf eine
Hub-Seite, leuchtet die Stelle auf, und oben steht die Aufgabe mit „Erledigt“. Die möglichen
Ziele listet `Services\Places.cs`.

## Code-Anfragen

Schickt ein Kunde über „Request a free trial“ oder „Buy a license“ (oder mit einer eigenen Mail samt Geräte-ID) eine Anfrage
an die Kontakt-Adresse, erscheint sie im Tab **Mails** unter „Code-Anfragen“, als Hinweis in der
Übersicht, als Zahl in der Seitenleiste und im Titel des Browser-Tabs. Der Hub liest dafür das
Postfach seines Mailkontos per IMAP (`imap.gmail.com:993`, dasselbe App-Passwort wie beim
Senden) – alle zwei Minuten, solange er läuft, und auf „Jetzt prüfen“. Er liest nur: In Gmail
wird nichts markiert, verschoben oder gelöscht.

Eine Anfrage ist eine Mail mit Geräte-ID, die keine Antwort ist. Plugin, „Testphase“ oder „Kauf“
und die Version liest der Hub aus dem Text, den das Plugin schreibt. „Testcode senden …“ bzw.
„Kauf-Code senden …“ füllt das Formular damit aus, erzeugt den Code und öffnet die Mail an den
Kunden – als Antwort auf seine Anfrage, also im selben Gespräch. Die Anfragen liegen im privaten
Ordner (`inbox.json`), weil sie Adressen von Kunden enthalten.

Erledigt ist eine Anfrage erst, wenn die Mail mit dem Code wirklich rausgegangen ist: Das Erzeugen
des Codes vermerkt ihn nur an der Anfrage (`licenseId`), sie bleibt offen und zeigt „Der Code ist
erzeugt – die Mail damit ist noch nicht gesendet“ mit dem Knopf „Mail mit dem Code senden …“. Den
Wechsel auf „Erledigt“ macht der Server beim Senden (`Inbox.Answered`, aufgerufen von
`POST /api/mails/send`) – auch wenn die Mail von der Kundenseite aus gesendet wurde. Von Hand geht
es mit „Ohne Mail ablegen“; unter „Erledigt“ steht dann „Code erzeugt, ohne Mail abgelegt“.

Zum Testen ohne echtes Postfach: `PLUGINHUB_MAIL_DROP=<Ordner>` liest die `.eml`-Dateien dort,
`PLUGINHUB_IMAP=host:port` spricht ohne TLS mit einem Test-Server.

## Vom Handy aus

Einstellungen → **Handy-Zugang**. Der Weg geht über [Tailscale](https://tailscale.com): Die
kostenlose App kommt auf PC und Handy, beide mit demselben Konto. „Einschalten“ lässt
`tailscale serve` die Anfragen deines Handys an den Hub weiterreichen – unter
`https://<pc-name>.<tailnet>.ts.net`, verschlüsselt und nur für deine Geräte sichtbar. Im
Internet ist nichts offen, am Router und in der Windows-Firewall ändert sich nichts. Beim
ersten Mal fragt Tailscale im Browser nach einer Freigabe für Serve und HTTPS; danach noch
einmal „Einschalten“.

So bleibt es sicher:
- Eine weitergereichte Anfrage erkennt der Hub an `X-Forwarded-Host`, das Serve immer setzt.
  Sie gilt als Handy-Anfrage, egal was sie als Host angibt, denn den Host-Header gibt Serve
  unverändert weiter.
- Herein kommt sie nur, wenn der Handy-Zugang an ist, der PC-Name stimmt und
  `Tailscale-User-Login` genau das Konto ist, mit dem eingeschaltet wurde. Dieses Konto setzt
  Tailscale selbst, ein Gerät kann es nicht vorgeben. Funnel (öffentlich) und getaggte Geräte
  kommen nicht herein.
- Vom Handy aus gesperrt sind Schlüssel, Backups, Releases, Bauen, Hochladen zu GitHub, die
  Repository-Adresse, Dateien auf dem PC, „Hub beenden“ und der Handy-Zugang selbst. Die Seite
  blendet diese Knöpfe auf dem Handy aus (`body.phone`), der Server lehnt sie trotzdem ab.
- Solange der Handy-Zugang an ist, startet der Hub mit Windows (HKCU\…\Run, `--no-browser`) und
  beendet sich nicht nach drei Minuten. Der PC darf dann nicht schlafen, solange du ihn
  unterwegs brauchst. Für einen Build des Hubs vorher „Hub beenden“ am PC.
- Der Eintrag für den Start mit Windows nennt die .exe mit vollem Pfad. Liegt der Hub nach einem
  Umzug woanders, schreibt er beim nächsten Start seinen neuen Ort hinein
  (`PhoneAccess.FollowAutostart`) – nur bei eingeschaltetem Handy-Zugang und nur, wenn es den
  Eintrag noch gibt. Dasselbe gilt für Verknüpfungen auf dem Desktop, im Startmenü und an der
  Taskleiste, die auf eine `PluginHub.exe` zeigen, die es nicht mehr gibt (`Services\Shortcuts.cs`).

Am Handy zeigt das Board der Planung zwei Spalten nebeneinander (quer gehalten drei) und rastet
spaltenweise ein, damit sich eine Karte direkt in die Nachbarspalte ziehen lässt (`hub.css`,
`max-width: 600px`).

Nach einem Neustart: Der Hub startet mit der Anmeldung an Windows. Tailscale verbindet sich
ohne „Run unattended“ erst, wenn seine App gestartet ist – etwa eine Minute später. Der Hub
schaut 10 Sekunden nach dem Start und danach jede Minute nach (`PhoneAccess.Watch`): Reicht
Serve nicht (mehr) an seinen Port weiter, richtet er es für denselben PC und dasselbe Konto neu
ein; die Karte zeigt, wann. Die Karte zeigt auch, ob Tailscale schon beim Windows-Start
verbindet, und schaltet das auf Klick um (`tailscale set --unattended`, gelesen aus
`tailscale debug prefs` → `ForceDaemon`).

Die Übersicht meldet, wenn das Handy den Hub nicht erreichen kann: Tailscale nicht verbunden,
Serve aus, oder der PC hat im Tailnet einen anderen Namen.

Schläft der PC, lässt er sich per Wake-on-LAN wecken – Anleitung: `Wake-on-LAN Anleitung
26_09_26.md` hier im Ordner des Hubs. Ein so geweckter PC schläft ohne Tastatur und Maus nach zwei Minuten
wieder ein; jede Anfrage vom Handy setzt deshalb den Leerlauf-Zähler von Windows zurück
(`Desktop.KeepAwake`, SetThreadExecutionState). Die offene Seite fragt alle 30 Sekunden, der PC
bleibt wach, solange sie offen ist, und schläft danach wie gewohnt.

Von unterwegs weckt ihn der **Wecker**: ein Raspberry Pi am Router mit der Seite
`https://wecker.tail03edd2.ts.net` (seit 15.10.2026). Er schickt das Magic Packet ins Heimnetz und
öffnet danach den Hub. Wie er gebaut und eingerichtet ist: `wecker\README.md`.

## Repositories sichern

Unter „Schlüssel und Backup → Repositories sichern“ steht jedes Repository mit seiner Kopie: Für
jedes trägt man die Adresse eines eigenen privaten GitHub-Repositorys ein („Verbinden …“); dieselbe
Adresse für zwei Ordner lehnt der Hub ab. „Alle hochladen“ schiebt Branch und Tags aller
Repositories, die eine Adresse haben (`git push`) – schlägt eines fehl, gehen die anderen trotzdem
hoch. Beim ersten Mal meldet Git sich über seinen Credential Manager bei GitHub an. Danach zeigt
die Übersicht, wenn Commits oder Tags noch nicht oben sind, und welcher Ordner noch gar keine Kopie
hat. Der Hub lädt nur auf Klick hoch (`Services\RepoBackup.cs`; das letzte Hochladen je Adresse
steht in `state.json`).

## Lizenzen

Jedes Plugin hat ein eigenes Schlüsselpaar (ECDSA P-256). Der öffentliche Teil und die
Kontakt-Adresse stehen in `Activation.cs` des Plugins; der Hub schreibt die Datei. Ein Code ist
die Signatur von `PH2|<Geräte-ID>|<letzter Tag oder 0>` und sieht so aus:
`2.20270131.<Signatur>` (Test bis 31.01.2027) oder `2.0.<Signatur>` (unbegrenzt). Codes des
ersten Formats (nur die Signatur der Geräte-ID) gelten weiter. Das Format steht in
`Plugins Rhino\RhinoKit\Licensing\ActivationCode.cs` – dieselbe Datei kompilieren Plugin und Hub.
Ein neues Schlüsselpaar macht alle bisherigen Codes des Plugins ungültig.

## Versionen und Releases

Die Version, die Kunden sehen, steht in `<Plugin>\Version.props`. Normale Builds heißen
`1.0.0-test`; der Release-Assistent baut mit `-p:HubRelease=true` ohne „-test“, schnürt das
Paket (nur die Dateien aus `package` in projects.json plus Anleitung, Lizenzbedingungen und
Changes.txt) nach `Plugins Rhino\Releases\` (`releases` in projects.json), committet es im
Repository der Plugins mit dem Tag `<id>-<Version>` (z. B. `laser-1.0.0`) und stellt danach die
nächste Patch-Version ein. Die getesteten Karten, die dabei auf „Veröffentlicht“ wandern, committet
er im Repository des Hubs („<Plugin> <Version>: Roadmap“). Die `vNN`-Tags sind Claudes interne
Zählung – nach dem Umzug je Repository, das an einer Version beteiligt ist.

Neben dem ZIP baut jedes Release ein **Rhino-Paket** (`<name>-<Version>-rh8_nn-win.yak`) mit
Yak.exe aus Rhino 8 (`Services\YakPackages.cs`): dieselben Dateien, das Symbol
`<Plugin>\icons\package.png` (aus dem Panel-Symbol, `tools\make_package_icons.ps1`) und
ein Manifest mit Name (`packageName`), Beschreibung und Stichworten aus projects.json; Autor ist
der Anbieter aus den Einstellungen. „rh8_nn“ ist die Rhino-Version, gegen die gebaut wurde – das
Paket gilt ab ihr. In Rhinos Package Manager kommt es erst mit „Zum Package Manager hochladen“
unter Releases (`yak push`, vorher einmal „Bei Rhino anmelden“ = `yak login`); danach ist es
öffentlich. Was hochgeladen ist, steht in `Hub\packages.json`. „Probepaket bauen“ packt den
aktuellen Build nach `<Plugin>\bin\Release\package\`, ohne Release und ohne Hochladen.

## Bauen

Den Hub vorher beenden (Seitenleiste → „Hub beenden“), sonst ist die .exe gesperrt. Im Ordner
des Hubs:

    dotnet build .\PluginHub.csproj -c Release

Seite und Stil (`wwwroot\`) liest der Hub direkt aus diesem Ordner – dafür reicht ein
Neuladen im Browser.

## Testen, ohne echte Daten anzufassen

    set PLUGINHUB_ROOT=<Ordner mit Kopien von "Plugins Rhino", "Plugin Hub" und "Website", am besten jede mit .git>
    set PLUGINHUB_PRIVATE_DIR=<leerer Ordner>
    set PLUGINHUB_MAIL_PICKUP=<Ordner>      (Mails werden dort als .eml abgelegt statt gesendet)
    set PLUGINHUB_PORT=51799
    set PLUGINHUB_TAILSCALE=<Ersatz für tailscale.exe>   (antwortet auf status/serve wie die echte)
    set PLUGINHUB_AUTOSTART_FILE=<Datei>                 (statt des Autostart-Eintrags)
    set PLUGINHUB_SHORTCUT_DIRS=<Ordner;Ordner>          (statt Desktop, Startmenü und Taskleiste)
    set PLUGINHUB_YAK=<Ersatz für Yak.exe>               (baut echt, lädt nie hoch)
    set PLUGINHUB_YAK_TOKEN=<Datei>                      (statt %APPDATA%\McNeel\yak.yml)
    PluginHub.exe --no-browser

`PLUGINHUB_ROOT` ist die Wurzel; als Ordner des Hubs gilt darin der, der `Hub\projects.json`
enthält – Seiten und Daten kommen aus dieser Kopie, das Programm von dort, wo die .exe liegt.
`PLUGINHUB_TAILSCALE` immer setzen: Ohne Ersatz spräche ein Test-Hub mit eingeschaltetem
Handy-Zugang mit dem echten Tailscale und könnte dessen Weiterleitung auf seinen Port umbiegen.

Ein Test-Hub (mit `PLUGINHUB_PRIVATE_DIR`) trägt nie etwas in den echten Autostart ein, fasst
keine echte Verknüpfung an, zeigt kein Symbol neben der Uhr und öffnet keinen Explorer. Eine
Anfrage „vom Handy“ simuliert man mit den Headern `X-Forwarded-Host: <pc-name>` und
`Tailscale-User-Login: <konto>`.

Wichtig für Claude: Aus den Werkzeugen gestartete Programme schreiben unter `%LOCALAPPDATA%` in
eine Sandbox, die der Nutzer nicht sieht. Echte Schlüssel, Kunden und Backups deshalb nie mit
einem so gestarteten Hub anlegen – nur über den Hub des Nutzers. Die Regeln für die Arbeit an
diesem Ordner stehen in `CLAUDE.md`.
