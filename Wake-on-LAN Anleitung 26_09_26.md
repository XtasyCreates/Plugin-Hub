# Hub vom Handy: den PC aufwecken (Wake-on-LAN)

Ziel: Der PC schläft, und du weckst ihn vom Handy aus, wenn du den Hub brauchst. Zuerst testen wir das
im Heimnetz. Ob und wie es von unterwegs geht, entscheiden wir danach.

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
3. Am PC: Start → Ein/Aus → **Energie sparen**. Nicht herunterfahren. Eine Minute warten.
4. In der App den PC wecken. Nach etwa 10 Sekunden wacht der Laptop auf: Lüfter oder Tastaturbeleuchtung
   gehen an, der Bildschirm kann aus bleiben oder den Sperrbildschirm zeigen.
5. Innerhalb von zwei Minuten auf dem Handy `https://gustavs-legion.tail03edd2.ts.net` öffnen. Nach
   10–30 Sekunden ist der Hub da, und der PC bleibt wach, solange die Seite offen ist.

Hat es geklappt, hake im Hub die Aufgabe „Wake-on-LAN testen“ ab.

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

## Von unterwegs (später)

Ein Wecksignal kommt nicht über das Internet, es muss aus dem Heimnetz kommen. Dafür braucht es ein Gerät,
das immer an ist:

- deinen Router, wenn seine App oder seine Oberfläche (<http://192.168.0.1>) „Wake on LAN“ anbietet, oder
- einen kleinen Raspberry Pi mit Tailscale (ab etwa 20 €, 2–3 W), den du vom Handy aus bittest, den PC
  zu wecken.

Das richten wir ein, sobald der Test im Heimnetz geklappt hat.

## Gut zu wissen

- **Energie sparen statt herunterfahren:** Nach einem Neustart startet der Hub erst, wenn du dich an
  Windows anmeldest.
- In den Energieeinstellungen kann der PC bei Netzbetrieb nach z. B. 30 Minuten von selbst in den
  Standby gehen. Das passt dazu.
