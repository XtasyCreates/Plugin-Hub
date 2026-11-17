# Hub vom Handy: den PC aufwecken (Wake-on-LAN)

Ziel: Der PC schläft, und du weckst ihn vom Handy aus, wenn du den Hub brauchst. Im Heimnetz hat das
geklappt (Aufgabe „Wake-on-LAN testen“ erledigt). Von unterwegs weckt ihn ein Raspberry Pi – siehe
„Von unterwegs: der Raspberry Pi als Wecker“ weiter unten.

## Was schon passt

- Dein Legion kennt den klassischen Standby (S3), und Windows erlaubt dem LAN-Adapter (Realtek PCIe GbE)
  schon, den PC zu wecken.
- Der Hub läuft über den Standby weiter; Tailscale verbindet sich nach dem Aufwachen von selbst.
- Ein über das Netz geweckter PC schläft nach etwa zwei Minuten wieder ein, wenn niemand Tastatur oder
  Maus berührt. Solange die Hub-Seite auf dem Handy offen ist, hält der Hub ihn deshalb wach. Schließt du
  sie, schläft der PC wie gewohnt wieder ein.

Die Daten für die Weck-App:

| Feld | Wert |
|---|---|
| MAC-Adresse | `C8:53:09:57:5E:D3` |
| IP-Adresse | `192.168.0.222` |
| Broadcast-Adresse | `192.168.0.255` |
| Port | `9` |

## Test im Heimnetz (etwa 10 Minuten)

1. Den Laptop ans Netzteil und ans LAN-Kabel hängen. Über WLAN lässt er sich nicht wecken.
2. Auf dem Handy eine Wake-on-LAN-App installieren, z. B. „Wake On Lan“ (Android) oder „Mocha WOL“
   (iPhone), und den PC mit den Daten oben anlegen. Das Handy muss dabei im Heim-WLAN sein.
3. Am PC: Start → Ein/Aus → **Energie sparen**. Nicht herunterfahren. Eine Minute warten und dabei Maus
   und Tastatur nicht berühren – sie dürfen den PC ebenfalls wecken, und dann sagt der Test nichts.
4. In der App den PC wecken. Nach etwa 10 Sekunden wacht der Laptop auf: Lüfter oder Tastaturbeleuchtung
   gehen an, der Bildschirm kann aus bleiben oder den Sperrbildschirm zeigen.
5. Innerhalb von zwei Minuten auf dem Handy `https://gustavs-legion.tail03edd2.ts.net` öffnen. Nach
   10–30 Sekunden ist der Hub da, und der PC bleibt wach, solange die Seite offen ist.
6. Das Gleiche noch einmal nach einer Nacht: abends Energie sparen, morgens wecken. Am Netzteil legt
   Windows den schlafenden Laptop nach etwa zwei Stunden in den Ruhezustand – bist du unterwegs, ist das
   der Normalfall. Dein LAN-Adapter meldet, dass er auch daraus wecken kann; ob es wirklich klappt, zeigt
   nur dieser Test. Das Aufwachen dauert dann etwas länger.

Haben beide Tests geklappt, hake im Hub die Aufgabe „Wake-on-LAN testen“ ab.

## Wenn der PC nicht aufwacht

Der Reihe nach probieren und danach jeweils die Schritte 3 und 4 wiederholen:

1. Geräte-Manager → Netzwerkadapter → „Realtek PCIe GbE Family Controller“ → Eigenschaften →
   **Energieverwaltung**: beide Häkchen setzen – „Gerät kann den Computer aus dem Standbymodus
   aktivieren“ und „Nur Magic Packet kann Computer aus dem Standbymodus aktivieren“.
2. Im selben Fenster, Reiter **Erweitert**: „Power Saving Mode“ und „Energy-Efficient Ethernet
   (LAN-Energiesparen, EEE)“ auf *Deaktiviert* stellen. Beide können die Leitung im Standby abschalten.
3. In der App statt der Broadcast-Adresse die IP `192.168.0.222` eintragen.
4. Im BIOS (beim Einschalten F2) unter *Configuration* nach „Wake on LAN“ suchen und einschalten, falls
   es die Option gibt. Bei Legion-Laptops fehlt sie oft.

Klappt es danach immer noch nicht, kann dein Laptop Wake-on-LAN vermutlich nicht. Dann bleiben der
Heimserver oder der durchlaufende PC.

## Von unterwegs: der Raspberry Pi als Wecker

Ein Wecksignal kommt nicht über das Internet, es muss aus dem Heimnetz kommen. Dein Router, die Magenta
Home Box Fiber, kann das nicht. Deshalb übernimmt es ein Raspberry Pi am Router: Er ist immer an (etwa
3 W), hängt mit Tailscale in deinem Tailnet und hat eine kleine Seite, `https://wecker.tail03edd2.ts.net`.
Öffnest du sie auf dem iPhone, weckt er den PC, und sobald der Plugin Hub antwortet, öffnet sich der Hub.
Solange er lädt, steht daneben ein Knopf für den TISS Bot. Wie der Wecker gebaut ist: `wecker\README.md`.

### Dein Teil: die Speicherkarte

1. **Raspberry Pi Imager** von raspberrypi.com/software laden und installieren – Version 2.0.7 oder
   neuer. Ältere übernehmen bei aktuellen Raspberry-Pi-OS-Versionen die Einstellungen nicht.
2. Die microSD-Karte in den PC stecken und im Imager wählen:
   - Gerät: **Raspberry Pi 4**
   - Betriebssystem: **Raspberry Pi OS (other) → Raspberry Pi OS Lite (64-bit)** – ohne Desktop
   - Speicher: die microSD-Karte (genau hinsehen – sie wird gelöscht)
3. Bei den Einstellungen (Anpassen):
   - Hostname: `wecker`
   - Ort: Wien – das setzt Zeitzone und Tastatur
   - Benutzer: `gustav` und ein Passwort deiner Wahl. Das brauchst du nur, falls du dich je mit
     Bildschirm und Tastatur direkt am Pi anmeldest.
   - WLAN: leer lassen – der Pi hängt am LAN-Kabel
   - Fernzugriff: **SSH einschalten**, **nur mit öffentlichem Schlüssel**, und diese eine Zeile
     einfügen (das ist der öffentliche Teil eines Schlüssels, den nur dein PC hat):

         ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIChrrr1YmU/kH5brbxxseeNBJzAqp2bKsJK7hk6ASuW+ wecker-einrichtung@gustavs-legion

   - Raspberry Pi Connect: aus
4. Schreiben lassen. Danach die Karte **noch einmal in den PC stecken** und Claude Bescheid sagen:
   Claude prüft, ob die Einstellungen wirklich auf der Karte stehen.
5. Karte in den Pi, **LAN-Kabel** vom Pi in die Magenta-Box, zuletzt das **Netzteil**. Der erste Start
   dauert ein paar Minuten; er startet dabei einmal von selbst neu.

### Claudes Teil: über das Heimnetz einrichten

Claude kopiert den Wecker per SSH auf den Pi und startet die Einrichtung in deinem Terminal: Sie bringt
das System auf den neuesten Stand, schaltet die automatischen Sicherheits-Updates ein (wenn eines einen
Neustart verlangt, um 4:30 Uhr) und installiert Tailscale und den Wecker. Drei Dinge dabei sind deine:

- **Dein Pi-Passwort** einmal eintippen, wenn das Terminal danach fragt – der Imager legt deinen
  Benutzer so an, dass Admin-Befehle es verlangen.
- **Tailscale anmelden:** Im Terminal erscheint ein Link – den öffnen und mit deinem Tailscale-Konto
  anmelden.
- **Ablaufdatum abschalten:** auf login.tailscale.com unter „Machines“ bei `wecker` → „…“ →
  **Disable key expiry**. Sonst fällt der Pi nach 180 Tagen aus deinem Tailnet. Dasselbe am besten
  auch bei `gustavs-legion` – sonst erreicht dein Handy irgendwann auch den Hub nicht mehr.

### Auf dem iPhone

1. Tailscale an, in Safari `https://wecker.tail03edd2.ts.net` öffnen und mit Teilen →
   **Zum Home-Bildschirm** als Symbol ablegen.
2. **Test:** am PC Start → Ein/Aus → Energie sparen. Auf dem iPhone das WLAN ausschalten (damit es
   wirklich „von unterwegs“ ist), das Symbol antippen: „PC wird geweckt …“, nach 10–30 Sekunden
   „Der PC ist wach“, dann öffnet sich der Hub.

## Gut zu wissen

- **Energie sparen statt herunterfahren:** Aus dem ausgeschalteten Zustand kann dein LAN-Adapter den PC
  nicht wecken, nur aus Standby und Ruhezustand. Und nach einem Neustart startet der Hub erst, wenn du
  dich an Windows anmeldest.
- In den Energieeinstellungen kann der PC bei Netzbetrieb nach z. B. 30 Minuten von selbst in den
  Standby gehen. Das passt dazu.
