# Wecker – den PC von unterwegs wecken

Ein Raspberry Pi im Heimnetz (Pi 4 mit 1 GB, Raspberry Pi OS Lite 64-bit, Name `wecker`, am
LAN-Kabel der Magenta-Box) mit einer kleinen Seite: `https://wecker.tail03edd2.ts.net`. Das iPhone
öffnet sie über Tailscale. Schläft der PC, schickt ihm der Pi ein Magic Packet ins Heimnetz – über das
Internet kommt ein Wecksignal nicht an –, und sobald der Plugin Hub antwortet, geht die Seite zu ihm
weiter; solange er lädt, steht daneben ein Knopf für den TISS Bot. Ist der PC schon wach, geht es
gleich weiter. Wie das Wecken am Laptop eingestellt ist: `..\Wake-on-LAN Anleitung 26_09_26.md`.

## Was wo liegt

| Datei | Auf dem Pi | Was |
|---|---|---|
| `wecker.py` | `/opt/wecker/` | die Seite und das Wecken – nur Python-Standardbibliothek |
| `page.html`, `icon.png` | `/opt/wecker/` | die Seite und das Symbol für den Home-Bildschirm |
| `wecker.service` | `/etc/systemd/system/` | startet den Wecker mit dem Pi, ohne Rechte, die er nicht braucht |
| `wecker.example.json` | `/etc/wecker.json` | die Einstellungen – die echte Datei liegt nur auf dem Pi, sie nennt das Tailscale-Konto |
| `setup.sh` | – | richtet den Pi ein oder bringt ihn auf den neuesten Stand |
| `first-run.sh` | – | die erste Einrichtung in einem Durchgang: `setup.sh`, Tailscale, Serve, Adresse des PCs |
| `make_icon.ps1` | – | zeichnet `icon.png` (am PC) |
| `melder/` | `/opt/melder/`, `/etc/melder/` | der Melder: Code-Anfragen aufs Handy, siehe unten |
| `tiss_helfer.py`, `tiss.html` | `/opt/wecker/` | der TISS-Helfer: Kalender-Abo und Erinnerungen des TISS Bots, siehe unten |
| `tiss-helfer.service` | `/etc/systemd/system/` | sein Dienst, mit einem eigenen Ordner `/var/lib/tiss-helfer` |

## Einstellungen (`/etc/wecker.json`)

- `mac`, `broadcast`, `wakePort`: wen er weckt – den LAN-Adapter des Laptops – und wohin das Paket geht.
- `pc`: woran er sieht, dass der PC wach ist: Er verbindet sich mit dessen Tailscale-Adresse (`ip`,
  sonst `name`) auf Port 443 und fragt den Hub nach `/api/hello`. Antwort 200 oder 403: Der Hub läuft.
  Antwortet nur Tailscale (502), ist der PC wach und der Hub nicht – etwa nach einem Neustart, bevor
  sich jemand an Windows anmeldet.
- `hub`, `bot`: wohin es danach geht.
- `allow`: welche Tailscale-Konten die Seite benutzen dürfen (Tailscale Serve schickt das Konto mit).
  Über Funnel, also aus dem offenen Internet, nie.
- `wakeOnOpen`: beim Öffnen sofort wecken. `waitSeconds`: wie lange die Seite auf den PC wartet.

Ein Ort im Hub hinter `#` wird nach dem Wecken mitgenommen: `https://wecker.tail03edd2.ts.net/#/mails?feld=anfragen`
öffnet im Hub die Code-Anfragen. Die Seite nimmt nur Hub-Routen (`#/…`) an, keine andere Adresse.

## Einrichten

1. **Karte** mit dem Raspberry Pi Imager (ab Version 2.0.7 – ältere übernehmen bei aktuellen
   Raspberry-Pi-OS-Versionen die Einstellungen nicht): Raspberry Pi OS Lite (64-bit), Name `wecker`,
   Benutzer `gustav`, SSH nur mit Schlüssel – der öffentliche Teil von `~\.ssh\wecker_ed25519` am PC.
   Danach prüfen, ob die Einstellungen auf der Karte stehen (`user-data` auf dem Laufwerk `bootfs`).
2. **Pi ans LAN**, Strom an, nach ein paar Minuten ist er als `wecker.local` erreichbar.
3. **Dateien hinüber** (vom Ordner `Plugin Hub` aus), dazu die echten Einstellungen als
   `wecker/wecker.json` – Gustavs Tailscale-Konto in `allow`, sonst wie `wecker.example.json`:

       scp -i ~/.ssh/wecker_ed25519 -r wecker gustav@wecker.local:

4. **Einrichten in einem Durchgang**, in einem Terminal, in dem Gustav sein Passwort für den Pi
   eintippt – der Imager legt den Benutzer so an, dass `sudo` es verlangt:

       ssh -t -i ~/.ssh/wecker_ed25519 gustav@wecker.local "sh wecker/first-run.sh"

   `first-run.sh` ruft `setup.sh` auf: System auf den neuesten Stand, automatische Updates (Debians
   eigene Auswahl – Sicherheits- und Stable-Updates –, dazu Kernel und Firmware des Pi und Tailscale;
   Neustart um 4:30 Uhr, wenn ein Update ihn verlangt), Tailscale aus dessen eigener Paketquelle, der
   Wecker als Dienst. Danach `tailscale up` – mit dem Link, den es zeigt, meldet sich Gustav an –,
   `tailscale serve` für die Seite und die Tailscale-Adresse des PCs in `pc.ip`.
5. **Ablaufdatum aus**: im Tailscale-Admin bei `wecker` (und `gustavs-legion`) „Disable key expiry“,
   sonst fällt das Gerät nach 180 Tagen aus dem Tailnet.
6. **iPhone**: Tailscale an, `https://wecker.tail03edd2.ts.net` in Safari, Teilen → „Zum
   Home-Bildschirm“. Testen über mobile Daten, nicht über das Heim-WLAN.

So eingerichtet am 15.10.2026: Pi 4 Model B (1 GB), Raspberry Pi OS Lite 64-bit auf Debian 13
„trixie“, Tailscale 1.104.1, im Heimnetz `192.168.0.7`, im Tailnet `wecker`.

## Ändern

Die Datei hier ändern, dann `scp …` wie oben und auf dem Pi `sh wecker/setup.sh wecker` – das
tauscht nur die Dateien des Weckers und startet ihn neu. Auch das braucht `sudo` und damit Gustavs
Passwort (`ssh -t …`).

## Melder – Code-Anfragen aufs Handy

Der Hub liest Code-Anfragen nur, solange der Laptop läuft. Der Melder auf dem Pi schließt die Lücke,
ohne am Hub etwas zu ändern: Er schaut alle 2 Minuten ins Postfach `support.autocreate@gmail.com` –
nur lesend wie der Hub (EXAMINE, BODY.PEEK; nichts wird markiert, verschoben oder gelöscht) – und
schickt eine Push-Meldung über ntfy, auf denselben Push-Namen wie der TISS Bot:
„Testcode-Anfrage für Laser Create“, „Kauf-Anfrage für SW to Laser“. Darin steht nie, wer anfragt.
Ein Tippen öffnet die Wecker-Seite, sie weckt den PC und geht im Hub zu den Anfragen.

- **Erkennung wie im Hub** (`Services/Inbox.cs`): Geräte-ID im Text, keine Antwort (`Re:`/`AW:`,
  In-Reply-To); das Plugin aus dem Betreff des Aktivierungsfensters, sonst aus dem Text; Test oder
  Kauf am Häkchen „[x] a free trial“ / „[x] a full license“.
- **Erinnerung alle 12 Stunden**, eine für alle offenen Anfragen. Fällt sie zwischen 22 und 8 Uhr,
  kommt sie um 8 Uhr; neue Anfragen kommen sofort.
- **Erledigt** ist eine Anfrage, sobald im Gesendet-Ordner eine Antwort auf sie liegt (In-Reply-To
  oder References – die Code-Mail des Hubs setzt beides); eine Antwort gilt auch für dieselbe
  Anfrage desselben Geräts, die doppelt kam; ebenso, wenn die Mail in Gmail archiviert oder gelöscht
  ist. Nach 7 Tagen ohne all das kommt eine letzte Erinnerung, danach Ruhe.
- **Beim ersten Start** gilt, was schon im Postfach liegt, als bekannt – gemeldet wird nur Neues.
- **Der Pi selbst**: Meldung bei 75 °C (wieder gut unter 70 °C), unter 2 GB frei, Unterspannung,
  wenn AdGuard nicht antwortet oder der Wecker-Dienst steht, und wenn das Postfach 30 Minuten
  nicht lesbar ist (etwa ein widerrufenes App-Passwort) – und wieder, wenn es in Ordnung ist.
  Sonntags um 10 Uhr eine leise Zeile „Pi läuft …“; bleibt sie aus, stimmt mit dem Pi etwas nicht.

Auf dem Pi: `/opt/melder/melder.py` (nur Python-Standardbibliothek), Dienst `melder`, Zustand in
`/var/lib/melder/state.json` – darin nur Prüfsummen von Message-ID und Geräte-ID, Plugin, Art und
Zeiten, keine Kundendaten. Einstellungen (`melder.json`, wie `melder/melder.example.json`, mit dem
Push-Namen) und das Gmail-App-Passwort liegen in `/etc/melder/`, nur für root lesbar; systemd reicht
sie beim Start an den Dienst weiter (`LoadCredential`), der selbst ohne Konto und Rechte läuft.

**Einrichten und Ändern** (vom Ordner `Plugin Hub` aus; `melder.json` mit dem Push-Namen liegt nicht
in Git und kommt eigens dazu):

    scp -i ~/.ssh/wecker_ed25519 -o HostKeyAlias=wecker.local -r wecker gustav@100.122.48.108:
    scp -i ~/.ssh/wecker_ed25519 -o HostKeyAlias=wecker.local melder.json gustav@100.122.48.108:wecker/melder/
    ssh -t -i ~/.ssh/wecker_ed25519 -o HostKeyAlias=wecker.local gustav@100.122.48.108 "sh wecker/melder/install.sh"

`install.sh` fragt Gustavs Passwort für sudo und beim ersten Mal das App-Passwort (`passwort.py`:
jede Taste zeigt `*`, Einfügen geht, nur die 16 Buchstaben zählen), prüft
die Anmeldung (`melder.py --check`, liest keine Mail), spielt die Wecker-Seite mit ein und startet
den Dienst. Ein neues App-Passwort: `sh wecker/melder/install.sh passwort`. Nachsehen:
`journalctl -u melder` (die Mails selbst stehen dort nie). Getestet wird gegen einen nachgebauten
Gmail-Server, nie gegen das echte Postfach.

## TISS-Helfer – Kalender-Abo und Erinnerungen des TISS Bots

Der TISS Bot läuft nur am Laptop. Was er liest, stellt der Pi bereit, auch wenn der Laptop schläft:
**Bot liefert, Pi verteilt** – der Pi liest TISS nie selbst.

- **Lieferung:** Nach jedem Lesen (beim Start, alle 12 Stunden, nach einer Anmeldung, einem
  herausgenommenen Abo-Eintrag) schickt der Bot über Tailscale
  `POST https://wecker.tail03edd2.ts.net/tiss/api/lieferung`: das fertige Kalender-Abo
  (`Services/Ics.cs`, ohne eigenen Alarm), alle Prüfungstermine seiner LVAs mit Anmeldezeitraum, ob
  Gustav angemeldet ist und bis wann er sich abmelden kann, dazu den Push-Namen
  (`Services/PiLink.cs` im Bot). Der Pi nimmt sie nur von den Tailscale-Adressen des Laptops an –
  Tailscale Serve sagt, woher eine Anfrage kommt, die Adressen des Laptops fragt der Pi bei Tailscale
  nach – und nur mit dem Kopf `X-Tiss-Bot`, den keine Webseite schicken kann.
- **Kalender-Abo:** `https://wecker.tail03edd2.ts.net/tiss/kalender/<Schlüssel>.ics`, rund um die
  Uhr, ohne den Laptop zu wecken. Der Schlüssel ist der des Bots (`feedToken`); die ganze Adresse
  steht auf der Seite des Helfers und im Bot unter „Kalender“.
- **Erinnerungen über ntfy**, auf den Push-Namen des Bots:
  - *Abmeldung endet* – einen Tag vor der Frist, für Prüfungen, für die er angemeldet ist;
  - *Anmeldung öffnet* – eine Stunde vorher, in LVAs, in denen er für keine kommende Prüfung
    angemeldet ist;
  - *Neuer Prüfungstermin* – sobald einer dazukommt (eine LVA, die der Pi zum ersten Mal sieht, ist
    nur der Ausgangspunkt).

  Nachts (22–8 Uhr) ist Ruhe: „Abmeldung endet“ kommt dann um 21 Uhr davor, ein neuer Termin um
  8 Uhr; „Anmeldung öffnet“ kommt immer genau eine Stunde vorher. Ein Tippen öffnet die
  Prüfungsanmeldung der LVA in TISS. Ist Push im Bot aus, erinnert auch der Pi nicht.
- **Seite:** `https://wecker.tail03edd2.ts.net/tiss/` – letzte Lieferung, die Abo-Adresse zum
  Kopieren, die nächsten und die zuletzt gesendeten Erinnerungen, „Probe-Erinnerung senden“.

Auf dem Pi: `/opt/wecker/tiss_helfer.py` (nur Python-Standardbibliothek), Dienst `tiss-helfer` auf
`127.0.0.1:8081`, ohne eigenes Konto; sein Zustand – die letzte Lieferung und was gesendet ist –
liegt in `/var/lib/tiss-helfer/state.json`. Eigene Einstellungen braucht er nicht: `allow` und `pc`
kommen aus `/etc/wecker.json`, ein Abschnitt `"tiss": {"listen", "base", "from"}` dort ändert bei
Bedarf Port, Pfad und zusätzliche Absender.

**Einrichten und Ändern** (vom Ordner `Plugin Hub` aus):

    scp -i ~/.ssh/wecker_ed25519 -o HostKeyAlias=wecker.local wecker/setup.sh wecker/tiss_helfer.py wecker/tiss.html wecker/tiss-helfer.service gustav@100.122.48.108:wecker/
    ssh -t -i ~/.ssh/wecker_ed25519 -o HostKeyAlias=wecker.local gustav@100.122.48.108 "sh wecker/setup.sh tiss"

Dazu im Bot unter Einstellungen → „Pi“ die Adresse `https://wecker.tail03edd2.ts.net/tiss`.
Nachsehen: `journalctl -u tiss-helfer` (der Schlüssel des Abos steht dort nie). Getestet wird mit
einem Test-Bot gegen den TISS-Nachbau und einem nachgebauten ntfy, nie mit dem echten Bot.

## Sicherheit

- Die Seite hört nur auf `127.0.0.1`. Erreichbar ist sie allein über Tailscale Serve, also aus dem
  eigenen Tailnet, und dort nur für die Konten in `allow`.
- Wecken geht nur mit dem Kopf `X-Wecker`, den die Seite selbst setzt. Eine fremde Webseite kann das
  Handy deshalb nicht dazu bringen, den PC zu wecken. Zwischen zwei Weckrufen liegen mindestens drei
  Sekunden.
- Der Dienst läuft ohne eigenes Konto und ohne Schreibrechte (`DynamicUser`, `ProtectSystem=strict`).
- SSH nimmt nur Schlüssel an, kein Passwort. Den Schlüssel von Claude nimmt man ihm wieder, indem man
  seine Zeile aus `~/.ssh/authorized_keys` auf dem Pi löscht.
