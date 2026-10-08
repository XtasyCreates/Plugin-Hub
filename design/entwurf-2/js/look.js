// How the hub looks on this device: mode, lettering, surfaces, the dot grid, the table in the
// title block, movement. Kept in this browser - the phone may look different from the PC - and
// put on before the first paint by the few lines in index.html, which read the same entry.

const KEY = 'hub.look';

/**
 * Each choice: its name on the settings page, its values (the first is the rule), and the
 * class a value puts on <html>. The mode sets data-theme instead.
 */
export const LOOK = {
  theme: { label: 'Modus', values: [['auto', 'Automatisch'], ['light', 'Hell'], ['dark', 'Dunkel']] },
  lettering: { label: 'Schrift', values: [['din', 'Normschrift'], ['segoe', 'Segoe']], cls: { segoe: 'f-segoe' } },
  surfaces: { label: 'Flächen', values: [['lines', 'Linien'], ['cards', 'Karten']], cls: { cards: 'cards' } },
  grid: { label: 'Punktraster', values: [['on', 'An'], ['off', 'Aus']], cls: { off: 'no-grid' } },
  block: { label: 'Tabelle im Plankopf', values: [['on', 'An'], ['off', 'Aus']], cls: { off: 'no-plankopf' } },
  motion: { label: 'Bewegung', values: [['on', 'An'], ['off', 'Aus']], cls: { off: 'no-motion' },
    hint: 'Der Hub entscheidet das selbst – auch wenn in Windows die Animationseffekte aus sind.' },
};

export function getLook() {
  let stored = {};
  try {
    stored = JSON.parse(localStorage.getItem(KEY) ?? '{}') ?? {};
  } catch {
    // a private window, or an entry something else wrote - the rule applies
  }
  return Object.fromEntries(Object.entries(LOOK).map(([key, choice]) =>
    [key, choice.values.some(([v]) => v === stored[key]) ? stored[key] : choice.values[0][0]]));
}

const classesOf = (look) => Object.entries(LOOK).map(([key, choice]) => choice.cls?.[look[key]]).filter(Boolean);

export function setLook(key, value) {
  const look = { ...getLook(), [key]: value };
  try {
    // with the classes spelled out: index.html puts them on before this file is there
    localStorage.setItem(KEY, JSON.stringify({ ...look, classes: classesOf(look).join(' ') }));
  } catch {
    // it still holds until the page is closed
  }
  applyLook(look);
}

export function applyLook(look = getLook()) {
  const root = document.documentElement;
  if (look.theme === 'auto') delete root.dataset.theme;
  else root.dataset.theme = look.theme;
  const on = classesOf(look);
  for (const choice of Object.values(LOOK)) {
    for (const name of Object.values(choice.cls ?? {})) root.classList.toggle(name, on.includes(name));
  }
}

/** Movement is the hub's own switch - Windows' "animation effects" are often off for other reasons. */
export const moving = () => !document.documentElement.classList.contains('no-motion');
