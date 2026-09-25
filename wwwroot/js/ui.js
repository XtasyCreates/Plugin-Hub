// Small things every page uses: escaping, icons, formats, dialogs, toasts, the clipboard.

export const esc = (value) =>
  String(value ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

// Outline icons on a 24 px grid, drawn with the stroke of svg.i
const ICONS = {
  grid: '<rect x="4" y="4" width="6.5" height="6.5" rx="1.5"/><rect x="13.5" y="4" width="6.5" height="6.5" rx="1.5"/><rect x="4" y="13.5" width="6.5" height="6.5" rx="1.5"/><rect x="13.5" y="13.5" width="6.5" height="6.5" rx="1.5"/>',
  package: '<path d="M12 3l8 4.5v9L12 21l-8-4.5v-9z"/><path d="M12 12l8-4.5"/><path d="M12 12v9"/><path d="M12 12L4 7.5"/><path d="M8 5.25l8 4.5"/>',
  users: '<circle cx="9" cy="8" r="3.5"/><path d="M3 20c0-3.3 2.7-6 6-6s6 2.7 6 6"/><path d="M16 4.5a3.5 3.5 0 0 1 0 7"/><path d="M21 20c0-2.6-1.6-4.9-4-5.7"/>',
  user: '<circle cx="12" cy="8" r="4"/><path d="M5 20c0-3.9 3.1-7 7-7s7 3.1 7 7"/>',
  shield: '<path d="M12 3l7 3v5c0 4.9-3.4 8.6-7 10-3.6-1.4-7-5.1-7-10V6z"/><path d="M9 12l2 2 4-4"/>',
  key: '<circle cx="8" cy="15" r="4"/><path d="M10.9 12.1L20 3"/><path d="M16.5 6.5l3 3"/><path d="M14 9l2 2"/>',
  folder: '<path d="M4 6.5A1.5 1.5 0 0 1 5.5 5H10l2 2h6.5A1.5 1.5 0 0 1 20 8.5v9a1.5 1.5 0 0 1-1.5 1.5h-13A1.5 1.5 0 0 1 4 17.5z"/>',
  file: '<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/><path d="M9 13h6"/><path d="M9 17h4"/>',
  code: '<path d="M8 8l-4 4 4 4"/><path d="M16 8l4 4-4 4"/><path d="M13.5 5l-3 14"/>',
  select: '<path d="M4 6.5A1.5 1.5 0 0 1 5.5 5H10l2 2h6.5A1.5 1.5 0 0 1 20 8.5v9a1.5 1.5 0 0 1-1.5 1.5h-13A1.5 1.5 0 0 1 4 17.5z"/><path d="M10 13h6"/><path d="M13 10l3 3-3 3"/>',
  hammer: '<path d="M14.5 4.5l5 5-2.5 2.5-5-5z"/><path d="M13.3 9.2L4.8 17.7a1.5 1.5 0 0 0 2.1 2.1l8.5-8.5"/><path d="M12 7l-1.5-1.5"/>',
  refresh: '<path d="M20 11a8 8 0 0 0-14.5-4.5L4 8"/><path d="M4 4v4h4"/><path d="M4 13a8 8 0 0 0 14.5 4.5L20 16"/><path d="M20 20v-4h-4"/>',
  copy: '<rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2"/>',
  check: '<path d="M5 12.5l4.5 4.5L19 7.5"/>',
  checkCircle: '<circle cx="12" cy="12" r="9"/><path d="M8.5 12.5l2.5 2.5 4.5-5"/>',
  alert: '<path d="M12 4L2.8 19.5h18.4z"/><path d="M12 10v4"/><path d="M12 16.8v.2"/>',
  info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v5"/><path d="M12 7.8v.2"/>',
  x: '<path d="M6 6l12 12"/><path d="M18 6L6 18"/>',
  plus: '<path d="M12 5v14"/><path d="M5 12h14"/>',
  trash: '<path d="M4 7h16"/><path d="M10 11v6"/><path d="M14 11v6"/><path d="M5.5 7l1 12a2 2 0 0 0 2 2h7a2 2 0 0 0 2-2l1-12"/><path d="M9 7V4h6v3"/>',
  pencil: '<path d="M4 20h4L19 9a2.1 2.1 0 0 0-3-3L5 17z"/><path d="M14.5 7.5l2 2"/>',
  search: '<circle cx="11" cy="11" r="6"/><path d="M20 20l-4.5-4.5"/>',
  download: '<path d="M12 4v11"/><path d="M7 10l5 5 5-5"/><path d="M5 20h14"/>',
  upload: '<path d="M12 20V9"/><path d="M7 14l5-5 5 5"/><path d="M5 4h14"/>',
  mail: '<rect x="3" y="5" width="18" height="14" rx="2"/><path d="M3.5 7l8.5 6 8.5-6"/>',
  pc: '<rect x="3" y="4" width="18" height="12" rx="1.5"/><path d="M8 20h8"/><path d="M12 16v4"/>',
  layers: '<path d="M12 3l9 5-9 5-9-5z"/><path d="M3 13l9 5 9-5"/>',
  scissors: '<circle cx="6" cy="7" r="3"/><circle cx="6" cy="17" r="3"/><path d="M8.6 8.6L20 18"/><path d="M8.6 15.4L20 6"/>',
  map: '<path d="M3 6.5l6-3 6 3 6-3v14l-6 3-6-3-6 3z"/><path d="M9 3.5v14"/><path d="M15 6.5v14"/>',
  git: '<circle cx="6" cy="6" r="2"/><circle cx="6" cy="18" r="2"/><circle cx="18" cy="8" r="2"/><path d="M6 8v8"/><path d="M18 10c0 4-4 4-8 5.5"/>',
  clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
  lock: '<rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/>',
  power: '<path d="M12 3v9"/><path d="M6.3 6.3a8 8 0 1 0 11.4 0"/>',
  arrowLeft: '<path d="M19 12H5"/><path d="M11 6l-6 6 6 6"/>',
  chevron: '<path d="M9 6l6 6-6 6"/>',
  euro: '<path d="M17 6.5A6.5 6.5 0 1 0 17 17.5"/><path d="M4 10.5h9"/><path d="M4 13.5h9"/>',
  rhino: '<path d="M4 17c0-5 3-9 8-9 3 0 5 1.5 6 3l2-3 .5 5c.2 2-.8 4-3.5 4H4z"/><path d="M8 17v3"/><path d="M15 17v3"/>',
  list: '<path d="M10 6h10"/><path d="M10 12h10"/><path d="M10 18h10"/><path d="M4 6l1.5 1.5L8 5"/><path d="M4 12l1.5 1.5L8 11"/><path d="M4 18l1.5 1.5L8 17"/>',
  tag: '<path d="M3.5 12.5V4.5a1 1 0 0 1 1-1h8l8 8a1.4 1.4 0 0 1 0 2l-7 7a1.4 1.4 0 0 1-2 0z"/><circle cx="8" cy="8" r="1.5"/>',
  sliders: '<path d="M4 6h9"/><path d="M17 6h3"/><circle cx="15" cy="6" r="2"/><path d="M4 12h3"/><path d="M11 12h9"/><circle cx="9" cy="12" r="2"/><path d="M4 18h11"/><circle cx="17" cy="18" r="2"/>',
  send: '<path d="M21 3L3 10.5l7.5 3L14 21z"/><path d="M21 3L10.5 13.5"/>',
  calendar: '<rect x="4" y="5" width="16" height="16" rx="2"/><path d="M4 10h16"/><path d="M9 3v4"/><path d="M15 3v4"/>',
  hourglass: '<path d="M6 3h12"/><path d="M6 21h12"/><path d="M7.5 3v2.5a4.5 4.5 0 0 0 2 3.7L12 11l2.5-1.8a4.5 4.5 0 0 0 2-3.7V3"/><path d="M7.5 21v-2.5a4.5 4.5 0 0 1 2-3.7L12 13l2.5 1.8a4.5 4.5 0 0 1 2 3.7V21"/>',
  dot: '<circle cx="12" cy="12" r="3"/>',
};

export const icon = (name, extra = '') =>
  `<svg class="i ${extra}" viewBox="0 0 24 24" aria-hidden="true">${ICONS[name] ?? ICONS.dot}</svg>`;

// ── Formats: German, no thousands grouping below 10 000 ───────────────────

const dateFormat = new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' });
const timeFormat = new Intl.DateTimeFormat('de-DE', { hour: '2-digit', minute: '2-digit' });

export const fmtDate = (iso) => (iso ? dateFormat.format(new Date(iso)) : '–');
export const fmtDateTime = (iso) => (iso ? `${fmtDate(iso)}, ${timeFormat.format(new Date(iso))}` : '–');

/** "heute 18:14", "gestern 09:02", otherwise date and time. */
export function fmtWhen(iso) {
  if (!iso) return '–';
  const date = new Date(iso);
  const days = Math.round((startOfDay(new Date()) - startOfDay(date)) / 86_400_000);
  if (days === 0) return `heute ${timeFormat.format(date)}`;
  if (days === 1) return `gestern ${timeFormat.format(date)}`;
  return fmtDateTime(iso);
}
const startOfDay = (d) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();

export const fmtPrice = (value) => (value == null ? '' : `${Number(value).toFixed(2).replace('.', ',')} €`);

export const plural = (n, one, many) => `${n} ${n === 1 ? one : many}`;

/**
 * A machine id as MachineId.ForUser shows it: 24 hex digits in six groups of four.
 * Mirrors MachineIds.Normalize on the server, for feedback while typing.
 */
export function checkMachineId(text) {
  const trimmed = (text ?? '').trim();
  if (!trimmed) return { ok: false, error: '' };
  const foreign = [...new Set(trimmed.replace(/[0-9a-fA-F\-\s]/g, ''))];
  if (foreign.length) return { ok: false, error: `Nur 0–9 und A–F – nicht dazu gehört: ${foreign.join(' ')}` };
  const hex = trimmed.replace(/[^0-9a-fA-F]/g, '').toUpperCase();
  if (hex.length !== 24) return { ok: false, error: `Eine Geräte-ID hat 24 Zeichen – diese hat ${hex.length}.` };
  return { ok: true, value: hex.match(/.{4}/g).join('-') };
}

// ── Feedback ───────────────────────────────────────────────────────────────

export function toast(message, kind = '') {
  const el = document.createElement('div');
  el.className = `toast ${kind}`;
  el.innerHTML = `${icon(kind === 'bad' ? 'alert' : 'check')}<span>${esc(message)}</span>`;
  document.getElementById('toasts').append(el);
  setTimeout(() => el.remove(), kind === 'bad' ? 7000 : 2600);
}

export async function copy(text, what = 'Text') {
  try {
    await navigator.clipboard.writeText(text);
    toast(`${what} kopiert`);
  } catch {
    toast('Kopieren ging nicht – markier den Text und drück Strg+C.', 'bad');
  }
}

/** Runs an action on a button, showing it busy; errors become a red toast. */
export async function busy(button, action) {
  button?.classList.add('busy');
  try {
    return await action();
  } catch (e) {
    toast(e.message, 'bad');
    return undefined;
  } finally {
    button?.classList.remove('busy');
  }
}

/**
 * A modal form. Resolves with what `submit(values)` returns (or the values), null when
 * cancelled. When `submit` throws, its message stays in the dialog and the dialog stays open.
 * `onOpen(form)` wires up anything inside it; `wide` makes room for longer texts.
 */
export function dialog({ title, body = '', confirm = 'OK', danger = false, cancel = 'Abbrechen', submit, onOpen, wide = false }) {
  return new Promise((resolve) => {
    const dlg = document.createElement('dialog');
    if (wide) dlg.classList.add('wide');
    dlg.innerHTML = `
      <form novalidate>
        <div class="dlg-body">
          <h3>${esc(title)}</h3>
          ${body}
          <div class="dlg-err" role="alert"></div>
        </div>
        <div class="dlg-foot">
          ${cancel ? `<button type="button" class="btn" data-cancel>${esc(cancel)}</button>` : ''}
          <button type="submit" class="btn ${danger ? 'danger-solid' : 'primary'}">${esc(confirm)}</button>
        </div>
      </form>`;
    document.body.append(dlg);

    const form = dlg.querySelector('form');
    const error = dlg.querySelector('.dlg-err');
    const close = (value) => {
      dlg.close();
      dlg.remove();
      resolve(value);
    };
    dlg.querySelector('[data-cancel]')?.addEventListener('click', () => close(null));
    dlg.addEventListener('cancel', (e) => {
      e.preventDefault();
      close(null);
    });
    form.addEventListener('input', () => { error.textContent = ''; });
    form.addEventListener('submit', async (e) => {
      e.preventDefault();
      const values = Object.fromEntries(new FormData(form));
      form.querySelectorAll('input[type=checkbox][name]').forEach((box) => { values[box.name] = box.checked; });
      const button = form.querySelector('[type=submit]');
      button.classList.add('busy');
      try {
        const result = submit ? await submit(values, form) : values;
        close(result ?? values);
      } catch (ex) {
        error.textContent = ex.message;
        button.classList.remove('busy');
      }
    });

    dlg.showModal();
    onOpen?.(form);
    dlg.querySelector('input:not([type=hidden]):not([readonly]),textarea,select')?.focus();
  });
}

export async function confirmDialog(title, text, { confirm = 'OK', danger = false } = {}) {
  const result = await dialog({ title, body: `<p>${text}</p>`, confirm, danger, submit: () => true });
  return result === true;
}

/** A message with a single button. */
export async function notice(title, html, button = 'Schließen') {
  await dialog({ title, body: html, confirm: button, cancel: '', submit: () => true });
}
