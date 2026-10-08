# Entwurf 1 – „Werkbank“

Design-Entwurf für den Plugin Hub vom 08.10.2026. **Nicht eingebaut** – er liegt hier als Stand zum
Zurückfallen, falls ein späterer Entwurf nicht gefällt.

## Die Idee

Farbe hat drei feste Aufgaben, alles andere ist Grafit mit feinen Linien:

- **Orange** ist, was du tun kannst (Knöpfe).
- **Die Farbe des Reiters** zeigt, wo du bist: am offenen Reiter in der Seitenleiste, am Streifen und an
  der Kachel der Seite, und an dem, was auf ihr gewählt ist.
- **Rot, Grün, Gelb** zeigen einen Zustand.

Dazu:

- Übersicht auf einem Bildschirm: Kennzahlen oben, „Zu erledigen“ und „Aufgaben“ als Listen, die
  Projekte daneben.
- Seitenleiste in drei Gruppen (Arbeit, Verkauf, Projekte); „Schlüssel und Backup“ und
  „Einstellungen“ unten.
- Zustände als Punkt mit Wort, Produktnamen als stille Etiketten, Schnellzugriff als Liste je Projekt.
- Abschnittstitel wie in den Plugin-Panels: kleine Großbuchstaben mit Linie.
- Zwei Schalter per Klasse am `body`: `all-colours` (alle Reiter-Symbole farbig) und `compact`.
  `data-theme="light|dark"` an `<html>` erzwingt Hell oder Dunkel.

Bilder: Ordner `bilder` (jetzt und Entwurf im Vergleich, weitere Seiten, Varianten, Handy).

## Was hier liegt

Die fünf Dateien, die sich gegenüber `wwwroot` unterscheiden – Stand der Seiten vom 08.10.2026
(nach v53, mit den Änderungen vom 05.10.2026):

| Datei | Was sich ändert |
|---|---|
| `css/hub.css` | das ganze Aussehen |
| `js/app.js` | Seitenleiste in Gruppen |
| `js/parts.js` | jede Zeile in „Zu erledigen“ trägt das Symbol ihres Themas |
| `js/pages/overview.js` | Übersicht mit Kennzahlen-Leiste und zwei Spalten |
| `js/pages/customers.js` | Produktnamen als Etikett statt als Zustand |

## Einbauen (falls du dich dafür entscheidest)

Die fünf Dateien über die gleichnamigen in `PluginHub\wwwroot` legen – vorher prüfen, ob sich die
Seiten seit dem 08.10.2026 geändert haben, sonst gehen neuere Änderungen an `app.js`, `parts.js`,
`overview.js` oder `customers.js` verloren. Die Seiten werden ohne Neubau des Hubs geladen.

## Deine Rückmeldung vom 08.10.2026

- Am jetzigen Design stören: zu viele Kästen, schlecht genutzter Platz, wirkt beliebig. Die Buntheit
  stört nicht.
- Richtung: diesen Entwurf festhalten, den nächsten mit mehr Charakter angehen.
- Reiter-Symbole: nur der offene Reiter farbig; die anderen zeigen ihre Farbe, wenn die Maus darüber
  ist. Dazu kleine, dezente Animationen.
- Dichte: kompakt.
