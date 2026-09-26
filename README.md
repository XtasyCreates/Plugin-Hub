# Plugin Hub

Die Schaltzentrale für die Rhino-Plugins in diesem Ordner: Planung (Roadmap, Entscheidungen,
Versionen, Dokumente), Aufgaben, Lizenzcodes (auch Testphasen), Kundenregister, Mails an Kunden,
Releases mit Kundenpaket, Builds, Schnellzugriff auf Dateien, Sicherung des Repositorys und eine
Liste, was gerade zu tun ist – jeder Punkt darin mit einem Knopf, der dorthin führt, wo er
erledigt wird.

## Starten

Desktop-Verknüpfung **Plugin Hub**, oder `PluginHub\bin\Release\PluginHub.exe`.
Der Hub öffnet sich im Browser unter `http://127.0.0.1:51730` – nur dieser PC erreicht ihn
(und, mit Handy-Zugang, dein Handy über Tailscale, siehe unten).
Läuft er schon, zeigt ein zweiter Start nur den laufenden. Ist drei Minuten lang keine
Seite mehr offen, beendet er sich selbst – außer der Handy-Zugang ist an.

## Wo was liegt

| Was | Wo | In Git? |
|---|---|---|
| Quelltext des Hubs | `PluginHub\` | ja |
| Projekte, Links, Paketinhalt | `Hub\projects.json` | ja |
| Einstellungen (Kontakt, Anbieter, Testdauer …) | `Hub\settings.json` | ja |
| Aufgaben (mit Ziel für den Knopf) | `Hub\tasks.json` | ja |
| Roadmap-Karten | `Hub\roadmap.json` | ja |
| Entscheidungen und offene Fragen | `Hub\decisions.json` | ja |
| Mail-Vorlagen, Paket-Anleitung | `Hub\templates\` | ja |
| Lizenzbedingungen (Entwurf, DE + EN) | `Hub\legal\` | ja |
| Kundenpakete und ihre Manifeste | `Releases\<Plugin>\<Version>\` | ja |
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

Ändert der Hub Dateien in Git (Einstellungen, Aufgaben, Planung, Vorlagen, `Activation.cs`, die
Dokumente oben im Ordner), zeigt die Übersicht „Änderungen des Hubs nicht committed“ mit einem
Knopf, der genau diese Dateien committet. Sonst committet der Hub nur beim Release.

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
- **Versionen:** aus den Git-Tags, mit den Karten, die in einer Version gebaut wurden.
- **Dokumente:** alle `.md` im Ordner und die Lizenzbedingungen – lesen und bearbeiten. Was ein
  Release archiviert hat, bleibt unverändert.

Für Claude: Wer ein Feature baut, setzt die Karte auf `gebaut`, trägt `built` (den Tag, z. B.
`v48`) und `tests` ein; wer ein gemeldetes Problem behebt, leert `problem` und setzt die Häkchen
zurück. Die Felder beschreibt `PluginHub\Services\Plan.cs`.

## Aufgaben und ihre Knöpfe

Jede Aufgabe kann ein Ziel haben (`goto` in tasks.json): eine Stelle im Hub (`route`, z. B.
`#/einstellungen?feld=contact` – „feld“ ist ein Feldname oder eine Karten-id), einen Link aus
projects.json (`open`), einen Build (`build`) oder eine Webseite (`url`). Führt der Knopf auf eine
Hub-Seite, leuchtet die Stelle auf, und oben steht die Aufgabe mit „Erledigt“. Die möglichen
Ziele listet `PluginHub\Services\Places.cs`.

## Code-Anfragen

Schickt ein Kunde über „Request a code“ (oder mit einer eigenen Mail samt Geräte-ID) eine Anfrage
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

Die Übersicht meldet, wenn das Handy den Hub nicht erreichen kann: Tailscale nicht verbunden,
Serve aus, oder der PC hat im Tailnet einen anderen Namen.

Schläft der PC, lässt er sich per Wake-on-LAN wecken – Anleitung: `Wake-on-LAN Anleitung
26_09_26.md` im Hauptordner. Ein so geweckter PC schläft ohne Tastatur und Maus nach zwei Minuten
wieder ein; jede Anfrage vom Handy setzt deshalb den Leerlauf-Zähler von Windows zurück
(`Desktop.KeepAwake`, SetThreadExecutionState). Die offene Seite fragt alle 30 Sekunden, der PC
bleibt wach, solange sie offen ist, und schläft danach wie gewohnt.

## Repository sichern

Unter „Schlüssel und Backup → Repository sichern“ trägt man die Adresse eines privaten
GitHub-Repositorys ein; „Hochladen“ schiebt Branch und Tags dorthin (`git push`). Beim ersten Mal
meldet Git sich über seinen Credential Manager bei GitHub an. Danach zeigt die Übersicht, wenn
Commits oder Tags noch nicht oben sind. Der Hub lädt nur auf Klick hoch.

## Lizenzen

Jedes Plugin hat ein eigenes Schlüsselpaar (ECDSA P-256). Der öffentliche Teil und die
Kontakt-Adresse stehen in `Activation.cs` des Plugins; der Hub schreibt die Datei. Ein Code ist
die Signatur von `PH2|<Geräte-ID>|<letzter Tag oder 0>` und sieht so aus:
`2.20270131.<Signatur>` (Test bis 31.01.2027) oder `2.0.<Signatur>` (unbegrenzt). Codes des
ersten Formats (nur die Signatur der Geräte-ID) gelten weiter. Das Format steht in
`RhinoKit\Licensing\ActivationCode.cs` – dieselbe Datei kompilieren Plugin und Hub.
Ein neues Schlüsselpaar macht alle bisherigen Codes des Plugins ungültig.

## Versionen und Releases

Die Version, die Kunden sehen, steht in `<Plugin>\Version.props`. Normale Builds heißen
`1.0.0-test`; der Release-Assistent baut mit `-p:HubRelease=true` ohne „-test“, schnürt das
Paket (nur die Dateien aus `package` in projects.json plus Anleitung, Lizenzbedingungen und
Changes.txt), committet es mit dem Tag `<id>-<Version>` (z. B. `laser-1.0.0`) und stellt danach
die nächste Patch-Version ein. Die `vNN`-Tags von Laser Create sind Claudes interne Zählung.

## Bauen

Den Hub vorher beenden (Seitenleiste → „Hub beenden“), sonst ist die .exe gesperrt:

    dotnet build .\PluginHub.csproj -c Release

Seite und Stil (`wwwroot\`) liest der Hub direkt aus diesem Ordner – dafür reicht ein
Neuladen im Browser.

## Testen, ohne echte Daten anzufassen

    set PLUGINHUB_ROOT=<Kopie von "Plugins Rhino", am besten mit .git>
    set PLUGINHUB_PRIVATE_DIR=<leerer Ordner>
    set PLUGINHUB_MAIL_PICKUP=<Ordner>      (Mails werden dort als .eml abgelegt statt gesendet)
    set PLUGINHUB_PORT=51799
    set PLUGINHUB_TAILSCALE=<Ersatz für tailscale.exe>   (antwortet auf status/serve wie die echte)
    set PLUGINHUB_AUTOSTART_FILE=<Datei>                 (statt des Autostart-Eintrags)
    PluginHub.exe --no-browser

Ein Test-Hub (mit `PLUGINHUB_PRIVATE_DIR`) trägt nie etwas in den echten Autostart ein. Eine
Anfrage „vom Handy“ simuliert man mit den Headern `X-Forwarded-Host: <pc-name>` und
`Tailscale-User-Login: <konto>`.

Wichtig für Claude: Aus den Werkzeugen gestartete Programme schreiben außerhalb des
Projektordners in eine Sandbox, die der Nutzer nicht sieht. Echte Schlüssel, Kunden und
Backups deshalb nie mit einem so gestarteten Hub anlegen – nur über den Hub des Nutzers.
