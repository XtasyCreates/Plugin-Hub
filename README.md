# Plugin Hub

Die Schaltzentrale für die Rhino-Plugins in diesem Ordner: Aufgaben, Lizenzcodes (auch
Testphasen), Kundenregister, Mails an Kunden, Releases mit Kundenpaket, Builds, Schnellzugriff
auf Dateien und eine Liste, was gerade zu tun ist.

## Starten

Desktop-Verknüpfung **Plugin Hub**, oder `PluginHub\bin\Release\PluginHub.exe`.
Der Hub öffnet sich im Browser unter `http://127.0.0.1:51730` – nur dieser PC erreicht ihn.
Läuft er schon, zeigt ein zweiter Start nur den laufenden. Ist drei Minuten lang keine
Seite mehr offen, beendet er sich selbst.

## Wo was liegt

| Was | Wo | In Git? |
|---|---|---|
| Quelltext des Hubs | `PluginHub\` | ja |
| Projekte, Links, Paketinhalt | `Hub\projects.json` | ja |
| Einstellungen (Kontakt, Anbieter, Testdauer …) | `Hub\settings.json` | ja |
| Aufgaben | `Hub\tasks.json` | ja |
| Mail-Vorlagen, Paket-Anleitung | `Hub\templates\` | ja |
| Lizenzbedingungen (Entwurf, DE + EN) | `Hub\legal\` | ja |
| Kundenpakete und ihre Manifeste | `Releases\<Plugin>\<Version>\` | ja |
| Private Schlüssel (DPAPI-verschlüsselt) | `%LOCALAPPDATA%\PluginHub\keys\` | **nein** |
| Kundenregister mit Mail-Protokoll | `%LOCALAPPDATA%\PluginHub\register.json` | **nein** |
| Mailkonto (Passwort DPAPI-verschlüsselt) | `%LOCALAPPDATA%\PluginHub\mail.json`, `mail.secret` | **nein** |
| Backups (mit Passwort verschlüsselt) | `%LOCALAPPDATA%\PluginHub\Backups\` | **nein** |
| Protokoll | `%LOCALAPPDATA%\PluginHub\hub.log` | nein |

Die privaten Schlüssel lassen sich nur unter diesem Windows-Konto auf diesem PC lesen.
Auf einem neuen PC holt man sie mit „Backup wiederherstellen“ zurück – ohne Backup und
dessen Passwort sind sie verloren. Das Mail-Passwort ist nicht im Backup.

Ändert der Hub Dateien in Git (Einstellungen, Aufgaben, Vorlagen, `Activation.cs`), zeigt die
Übersicht „Änderungen des Hubs nicht committed“ mit einem Knopf, der genau diese Dateien
committet. Sonst committet der Hub nur beim Release.

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
    PluginHub.exe --no-browser

Wichtig für Claude: Aus den Werkzeugen gestartete Programme schreiben außerhalb des
Projektordners in eine Sandbox, die der Nutzer nicht sieht. Echte Schlüssel, Kunden und
Backups deshalb nie mit einem so gestarteten Hub anlegen – nur über den Hub des Nutzers.
