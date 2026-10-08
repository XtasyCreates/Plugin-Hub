# Entwurf 2 – „Planblatt“

Das Aussehen des Plugin Hubs seit dem 08.10.2026. **Eingebaut** – die Dateien hier sind der Stand
des Einbaus, zum Nachsehen und zum Zurückholen. Der Hub selbst liest `..\..\wwwroot`.

## Die Idee

Der Hub als Blatt aus einem Planungsbüro – passend zu Plugins, die Pläne und Modelle bauen:

- **Normschrift** (DIN, die Schrift technischer Zeichnungen – auf Windows „Bahnschrift“, am iPhone
  „DIN Alternate“) für Titel, Beschriftungen und Zahlen. Fließtext bleibt Segoe.
- **Plankopf** auf jeder Seite: Kachel und Titel, darüber der Bereich, rechts die kleine Tabelle
  eines Plans – Bereich, Blatt, Datum. Was die Seite anbietet, hat darin eine eigene Zelle.
- **Linien statt Kästen**: Abschnitte stehen unter einer Linie, Listen und Tabellen haben nur
  Haarlinien. Vor jedem Abschnittstitel eine kleine Passermarke in der Farbe des Reiters.
- **Platz**: Seiten bis 1640 Bildpunkte breit; wo es passt, zwei Spalten (die Übersicht drei).
- **Seitenleiste** passend zum Modus: hell neben dem Papier (eine Stufe dunkler, mit feiner Linie
  dazwischen), Grafit neben dem dunklen Blatt. Das Blatt hat ein feines Punktraster. In den Entwürfen
  war die Leiste auch im hellen Modus dunkel – geändert am 08.10.2026 auf deinen Wunsch.
- **Farbe**: Orange = was du tun kannst, Reiterfarbe = wo du bist, Rot/Grün/Gelb = Zustand.
- **Reiter** tragen ihre Farbe dauerhaft: jede Zeile der Leiste ist leicht in ihrer Farbe
  hinterlegt, als Streifen ohne abgerundete Ecken (11 %, unter der Maus 19 %, offen 30 % mit einer
  Marke am linken Rand). Das Symbol eines geschlossenen Reiters ist grau und zeigt seine Farbe
  unter der Maus.
- **Kompakt** ist der Normalfall.

## Bewegung

Wenig, und nur wo sie etwas sagt:

- Beim Öffnen einer Seite wird ihr Farbstreifen quer „geschnitten“ wie ein Laserdurchgang, und die
  Seite setzt sich leicht (0,3 s). Bei einem bloßen Neuzeichnen passiert nichts.
- Eine Karte auf dem Board sitzt sofort dort, wo du sie loslässt; die anderen gleiten nach
  (0,19 s). Der Hub wird nebenher informiert, danach zeichnet die Seite still neu. Lehnt der Hub
  ab, gleitet die Karte zurück.
- Eine Karte, deren Stufe im Fenster geändert wird, wandert nach dem Speichern in ihre Spalte.
- Eine abgehakte Aufgabe klappt weg.
- Zeilen und Kennzahlen hellen sich unter der Maus auf, Dialoge kommen mit einem kurzen Einblenden.
- Die Bewegung ist ein Schalter des Hubs, nicht von Windows: Auf diesem PC sind in Windows die
  „Animationseffekte“ aus (Stand 08.10.2026) – Webseiten zeigen dann üblicherweise keine Bewegung.

## Einstellungen → Aussehen

Jede Wahl wirkt sofort und gilt für das Gerät, an dem sie getroffen wird (`localStorage`, Eintrag
`hub.look`). `js\look.js` setzt dafür Klassen an `<html>`:

| Wahl | Werte | Klasse |
|---|---|---|
| Modus | Automatisch, Hell, Dunkel | `data-theme="light"` oder `"dark"` |
| Schrift | Normschrift, Segoe | `f-segoe` |
| Flächen | Linien, Karten | `cards` |
| Punktraster | An, Aus | `no-grid` |
| Tabelle im Plankopf | An, Aus | `no-plankopf` |
| Bewegung | An, Aus | `no-motion` |

## Geprüft am 08.10.2026

Im Test-Hub mit Beispieldaten: alle Seiten hell und dunkel, in PC- und Handybreite; Karte ziehen
(andere Spalte, umsortieren, ablehnen, „Getestet“ mit offenen Schritten, Ablehnung durch den Hub);
Stufe im Fenster ändern; Aufgabe abhaken und wieder öffnen; alle sechs Schalter unter „Aussehen“,
auch nach dem Neuladen. Die Stildatei wurde vor dem Einbau aufgeräumt (eine Palette statt zwei) –
sie zeichnet jede Seite Wert für Wert gleich wie die gezeigte Fassung.

Dabei behoben, was auch der Hub vorher hatte: „erledigte anzeigen“ (Aufgaben) und Speichern auf
einer Kundenseite zeichneten am Rahmen vorbei – die Kachel am Titel verschwand.

## Was hier liegt

| Datei | Was sie tut |
|---|---|
| `index.html` | setzt das gewählte Aussehen vor dem ersten Bild |
| `css/hub.css` | die Palette, die Teile des Hubs, dann – ab „Planblatt“ – ihr Aussehen |
| `js/app.js` | Seitenleiste in Gruppen, Plankopf, zwei Spalten je Seite (`SPLITS`), Bewegung beim Öffnen und beim Neuzeichnen |
| `js/look.js` | das Aussehen dieses Geräts |
| `js/motion.js` | Gleiten, Setzen, Wegklappen |
| `js/parts.js` | Symbol je Thema in „Zu erledigen“ |
| `js/pages/overview.js` | Kennzahlen-Leiste, drei Bereiche nebeneinander |
| `js/pages/plan.js` | Karten sitzen sofort am neuen Platz |
| `js/pages/tasks.js` | abgehakte Aufgabe klappt weg |
| `js/pages/settings.js` | Karte „Aussehen“ |
| `js/pages/customer.js`, `customers.js` | Neuzeichnen über den Rahmen; Produktnamen als Etikett |

Bilder: Ordner `bilder` (1–5 aus der Entwurfsphase, dort ist die Leiste auch im hellen Modus noch
dunkel; 6–9 der eingebaute Stand).
