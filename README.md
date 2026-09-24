# Plugin Hub

Die Schaltzentrale für die Rhino-Plugins in diesem Ordner: Lizenzcodes erzeugen,
Kundenregister, Plugins bauen, Schnellzugriff auf Dateien und eine Liste, was gerade
zu tun ist.

## Starten

Desktop-Verknüpfung **Plugin Hub**, oder `PluginHub\bin\Release\PluginHub.exe`.
Der Hub öffnet sich im Browser unter `http://127.0.0.1:51730` – nur dieser PC erreicht ihn.
Läuft er schon, zeigt ein zweiter Start nur den laufenden. Ist drei Minuten lang keine
Seite mehr offen, beendet er sich selbst.

## Wo was liegt

| Was | Wo | In Git? |
|---|---|---|
| Quelltext des Hubs | `PluginHub\` | ja |
| Projekte, Links, Aufräum-Hinweise | `Hub\projects.json` | ja |
| Private Schlüssel (DPAPI-verschlüsselt) | `%LOCALAPPDATA%\PluginHub\keys\` | **nein** |
| Kundenregister | `%LOCALAPPDATA%\PluginHub\register.json` | **nein** |
| Backups (mit Passwort verschlüsselt) | `%LOCALAPPDATA%\PluginHub\Backups\` | **nein** |
| Protokoll | `%LOCALAPPDATA%\PluginHub\hub.log` | nein |

Die privaten Schlüssel lassen sich nur unter diesem Windows-Konto auf diesem PC lesen.
Auf einem neuen PC holt man sie mit „Backup wiederherstellen“ zurück – ohne Backup und
dessen Passwort sind sie verloren.

## Lizenzen

Jedes Plugin hat ein eigenes Schlüsselpaar (ECDSA P-256). Der öffentliche Teil steht in
`ActivationKey.cs` des Plugins und wird vom Hub geschrieben. Ein Code ist die Signatur
der Geräte-ID (`RhinoKit\Licensing\ActivationCode.cs` – dieselbe Datei kompilieren
Plugin und Hub). Ein neues Schlüsselpaar macht alle bisherigen Codes des Plugins ungültig.

## Bauen

Den Hub vorher beenden (Seitenleiste → „Hub beenden“), sonst ist die .exe gesperrt:

    dotnet build .\PluginHub.csproj -c Release

Seite und Stil (`wwwroot\`) liest der Hub direkt aus diesem Ordner – dafür reicht ein
Neuladen im Browser.

## Testen, ohne echte Daten anzufassen

    set PLUGINHUB_ROOT=<Kopie von "Plugins Rhino">
    set PLUGINHUB_PRIVATE_DIR=<leerer Ordner>
    set PLUGINHUB_PORT=51799
    PluginHub.exe --no-browser
