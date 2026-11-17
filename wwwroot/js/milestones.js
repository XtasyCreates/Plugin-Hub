// The milestones on the overview: the way to the first sales as a chain of dimensions - a tick
// per mark, under a reached one its stamp with the day, and between two reached marks the days
// that lay between them. The hub reads the marks of the way itself (Services/Milestones.cs); the
// user's own he stamps here. "bearbeiten" opens the list: order, hiding, own marks, counting
// anew. Whether the chain shows at all is a choice per device (Einstellungen → Aussehen).

import { api, del, post, put } from './api.js';
import { confirmDialog, dialog, esc, icon, toast } from './ui.js';
import { changed } from './store.js';
import { glide } from './motion.js';
import { isoDate } from './parts.js';

// Which marks this device has seen reached - a new one is announced once per device
const SEEN = 'hub.milestones';

const shown = () => !document.documentElement.classList.contains('no-milestones');

// The marks as the page shows them - what a click on one looks up
let current = [];

/** The marks, or why they could not be read; null where the chain is switched off. */
export async function loadMilestones() {
  if (!shown()) return null;
  try {
    return { marks: (await api('/api/milestones')).marks };
  } catch (e) {
    // New pages on a hub started before them: it has to start once more
    return { error: /\b404\b/.test(e.message)
      ? 'Der laufende Hub kennt die Meilensteine noch nicht – beende ihn („Hub beenden“) und starte ihn über die Desktop-Verknüpfung neu.'
      : `Die Meilensteine ließen sich nicht lesen: ${e.message}` };
  }
}

// A day comes as 2026-09-02 and is taken apart as written - no clock, no time zone
const fmtDay = (iso) => iso.split('-').reverse().join('.');
const dayNumber = (iso) => {
  const [y, m, d] = iso.split('-').map(Number);
  return Date.UTC(y, m - 1, d) / 86_400_000;
};

/** What a chain of dimensions writes between two ticks - here the days between two stamps. */
function between(a, b) {
  const days = Math.abs(dayNumber(b) - dayNumber(a));
  return days === 0 ? 'selber Tag' : days === 1 ? '1 Tag' : `${days} Tage`;
}

/** The chain for the overview - nothing where it is switched off, a line of text when it could not be read. */
export function milestoneStrip(ms) {
  if (!ms) return '';
  if (ms.error) return `<section class="ms-section"><h2>Meilensteine</h2><p class="faint">${esc(ms.error)}</p></section>`;

  current = ms.marks;
  const marks = ms.marks.filter((m) => !m.hidden);
  const done = marks.filter((m) => m.reached).length;
  const next = marks.find((m) => !m.reached);
  return `
    <section class="ms-section" data-place="meilensteine">
      <h2>Meilensteine <span class="count">${marks.length ? `${done} von ${marks.length}` : 'alle ausgeblendet'}</span>
        <button type="button" class="ms-edit" data-ms-edit>bearbeiten</button></h2>
      ${marks.length ? `<ol class="ms-chain">${marks.map((m, i) => mark(m, marks[i - 1], marks[i + 1], m === next)).join('')}</ol>` : ''}
    </section>`;
}

function mark(m, before, after, isNext) {
  // Half the line towards each neighbour - solid where both ends are reached, and a short end
  // beyond the outermost ticks, as a dimension line has
  const half = (other) => (other ? (m.reached && other.reached ? 'solid' : '') : `stub ${m.reached ? 'solid' : ''}`);
  const gap = m.reached && after?.reached ? between(m.reached, after.reached) : '';
  const say = m.reached
    ? `${m.title} – erreicht am ${fmtDay(m.reached)}${m.detail ? ` · ${m.detail}` : ''}`
    : `${m.title} – ${m.goal ? `${m.count} von ${m.goal}` : m.open}`;
  const tip = [say, m.own ? m.note : m.rule].filter(Boolean).join('\n');
  const cover = m.own
    ? `<button type="button" class="cover" data-ms-own="${esc(m.id)}" aria-label="${esc(say)} – stempeln oder ändern" title="${esc(tip)}"></button>`
    : `<a class="cover" href="${esc(m.route)}" aria-label="${esc(say)}" title="${esc(tip)}"></a>`;
  return `
    <li class="ms ${m.reached ? 'ms-done' : isNext ? 'ms-next' : ''}" data-ms="${esc(m.id)}">
      ${cover}
      <span class="ms-title">${esc(m.title)}</span>
      <span class="ms-rule" aria-hidden="true"><i class="l ${half(before)}"></i><i class="r ${half(after)}"></i><b class="ms-tick"></b>${gap ? `<span class="ms-gap">${esc(gap)}</span>` : ''}</span>
      <span class="ms-below">${m.reached ? `<span class="ms-stamp">${esc(fmtDay(m.reached))}</span>` : esc(m.open)}</span>
    </li>`;
}

/**
 * After the overview is drawn. A chain wider than the page shows its next mark - or stays where
 * the reader had it (`scrolledTo`, from before the redraw). A mark reached since this device
 * last saw the chain gets its stamp pressed on and is announced, once.
 */
export function milestonesArrived(main, ms, scrolledTo = null) {
  if (!ms?.marks) return;
  const chain = main.querySelector('.ms-chain');
  if (chain && chain.scrollWidth > chain.clientWidth) {
    const aim = chain.querySelector('.ms-next') ?? chain.lastElementChild;
    chain.scrollLeft = scrolledTo ?? aim.offsetLeft - (chain.clientWidth - aim.offsetWidth) / 2;
  }
  for (const m of fresh(ms.marks)) {
    main.querySelector(`[data-ms="${CSS.escape(m.id)}"]`)?.classList.add('ms-fresh');
    toast(`Meilenstein erreicht: ${m.title}`);
  }
}

/** The visible marks reached since this device last looked. The first look only takes note. */
function fresh(marks) {
  const reached = marks.filter((m) => m.reached).map((m) => m.id);
  let seen;
  try {
    seen = JSON.parse(localStorage.getItem(SEEN) ?? 'null');
    localStorage.setItem(SEEN, JSON.stringify(reached));
  } catch {
    return [];   // a private window: nothing to remember, so nothing to announce
  }
  return Array.isArray(seen) ? marks.filter((m) => m.reached && !m.hidden && !seen.includes(m.id)) : [];
}

/** The clicks of the chain: "bearbeiten" opens the list, an own mark its form. True when it was one of them. */
export async function milestoneClick(e) {
  if (e.target.closest('[data-ms-edit]')) {
    await manage();
    return true;
  }
  const own = e.target.closest('[data-ms-own]');
  if (!own) return false;
  const m = current.find((x) => x.id === own.dataset.msOwn);
  if (m && (await ownDialog(m))) changed();
  return true;
}

// ── An own mark ────────────────────────────────────────────────────────────

/**
 * Its name, a note, and the day it was reached - empty while it is open. Resolves with the
 * marks as they are after saving, null when cancelled.
 */
function ownDialog(m = null) {
  const today = isoDate(new Date());
  return dialog({
    title: m ? 'Eigener Meilenstein' : 'Neuer Meilenstein',
    confirm: m ? 'Speichern' : 'Anlegen',
    body: `
      <label class="field"><span>Name</span><input type="text" name="title" maxlength="48" value="${esc(m?.title ?? '')}" placeholder="z. B. Gewerbe angemeldet"></label>
      <label class="field"><span>Notiz <em>optional</em></span><input type="text" name="note" value="${esc(m?.note ?? '')}"></label>
      <div class="field">
        <span>Erreicht am <em>leer, solange er offen ist</em></span>
        <div class="inline wrap">
          <input type="date" name="reached" value="${esc(m?.reached ?? '')}" max="${today}">
          <button type="button" class="btn small" data-day="${today}">Heute stempeln</button>
          <button type="button" class="btn small ghost" data-day="">Stempel entfernen</button>
        </div>
      </div>`,
    submit: (v) => {
      const body = { title: v.title, note: v.note, reached: v.reached || null };
      return m ? put(`/api/milestones/${m.id}`, body) : post('/api/milestones', body);
    },
    onOpen: (form) => form.addEventListener('click', (e) => {
      const day = e.target.closest('[data-day]');
      if (day) form.elements.reached.value = day.dataset.day;
    }),
  });
}

// ── The list behind "bearbeiten" ───────────────────────────────────────────

async function manage() {
  let marks;
  try {
    marks = (await api('/api/milestones')).marks;
  } catch (e) {
    toast(e.message, 'bad');
    return;
  }

  await dialog({
    title: 'Meilensteine',
    wide: true,
    confirm: 'Fertig',
    cancel: '',
    body: `
      <p>Den Weg zum Verkauf stempelt der Hub selbst – aus Releases, Postfach und Kundenregister. Codes und Anfragen
        für diesen PC zählen dabei nicht. Eigene Meilensteine stempelst du.</p>
      <div class="ms-list" data-ms-list></div>
      <div><button type="button" class="btn" data-ms-new>${icon('plus')} Eigener Meilenstein</button></div>`,
    submit: () => true,
    onOpen: (form) => {
      const list = form.querySelector('[data-ms-list]');
      // The first row cannot go further up, the last not further down
      const edges = () => [...list.children].forEach((r, i, rows) => {
        r.querySelector('[data-ms-up]').disabled = i === 0;
        r.querySelector('[data-ms-down]').disabled = i === rows.length - 1;
      });
      // `back` (a row's id and one of its buttons): where the keyboard was - the rows are new ones
      const draw = (back = null) => {
        list.innerHTML = marks.map(row).join('');
        edges();
        if (back) list.querySelector(`[data-id="${CSS.escape(back[0])}"] ${back[1]}`)?.focus();
      };
      // Asks the hub and shows what it answers; what went wrong is said, and the list is the hub's again
      const ask = async (request, back) => {
        try {
          marks = (await request).marks;
        } catch (e) {
          toast(e.message, 'bad');
          marks = (await api('/api/milestones').catch(() => ({ marks }))).marks;
        }
        draw(back);
      };
      const take = (saved, back) => {
        if (!saved?.marks) return;
        marks = saved.marks;
        draw(back);
      };
      draw();

      form.addEventListener('click', async (e) => {
        if (e.target.closest('[data-ms-new]')) return take(await ownDialog());
        const el = e.target.closest('.ms-row');
        const button = e.target.closest('button');
        if (!el || !button) return undefined;
        const id = el.dataset.id;
        const m = marks.find((x) => x.id === id);

        if (button.matches('[data-ms-up],[data-ms-down]')) {
          // The row sits at its new place at once; the hub hears of it on the side. Its neighbour
          // is the one taken out and put back - so the button just pressed keeps the focus.
          const up = button.matches('[data-ms-up]');
          const other = up ? el.previousElementSibling : el.nextElementSibling;
          if (!other) return undefined;
          glide(() => list.children, () => (up ? el.after(other) : el.before(other)));
          edges();
          if (button.disabled) el.querySelector('[data-ms-up]:enabled, [data-ms-down]:enabled')?.focus();
          const ids = [...list.children].map((r) => r.dataset.id);
          marks.sort((a, b) => ids.indexOf(a.id) - ids.indexOf(b.id));
          return post('/api/milestones/order', { ids }).catch((ex) => ask(Promise.reject(ex)));
        }
        if (button.matches('[data-ms-hide]')) return ask(post(`/api/milestones/${id}/hidden`, { hidden: !m.hidden }), [id, '[data-ms-hide]']);
        if (button.matches('[data-ms-own-edit]')) return take(await ownDialog(m), [id, '[data-ms-own-edit]']);
        if (button.matches('[data-ms-delete]')) {
          const sure = await confirmDialog(`„${m.title}“ löschen?`, 'Der Meilenstein verschwindet aus der Maßkette, mit seinem Stempel.',
            { confirm: 'Löschen', danger: true });
          return sure ? ask(del(`/api/milestones/${id}`)) : undefined;
        }
        if (button.matches('[data-ms-recount]')) {
          // Marks that count the same thing start over together - "Erster Kauf" and "10 Käufe"
          const too = marks.filter((x) => x.kind === m.kind && x.id !== id).map((x) => `„${esc(x.title)}“`);
          const sure = m.since || await confirmDialog(`„${m.title}“ neu zählen?`,
            `Was der Hub dazu bis jetzt kennt, zählt dann nicht mehr – es war ein Test. Gezählt wird, was ab jetzt dazukommt.
             ${too.length ? `Das gilt auch für ${too.join(' und ')}.` : ''} „Alles zählen“ nimmt es zurück.`,
            { confirm: 'Neu zählen' });
          return sure ? ask(post(`/api/milestones/${id}/recount`, { on: !m.since }), [id, '[data-ms-recount]']) : undefined;
        }
        return undefined;
      });
    },
  });
  changed();
}

function row(m) {
  const state = m.reached
    ? `<span class="badge ok">${icon('check')} ${esc(fmtDay(m.reached))}</span>`
    : `<span class="badge">${esc(m.goal ? `${m.count} von ${m.goal}` : m.own ? 'offen' : m.open)}</span>`;
  const about = [m.own ? m.note : m.rule, m.reached && m.detail && `Erreicht mit ${m.detail}.`,
    m.since && `Gezählt wird erst seit dem ${fmtDay(m.since)}.`].filter(Boolean).join(' ');
  const act = m.own
    ? `<button type="button" class="btn small" data-ms-own-edit>${icon('pencil')} ${m.reached ? 'Ändern' : 'Stempeln'}</button>
       <button type="button" class="btn small ghost icon-only" data-ms-delete aria-label="„${esc(m.title)}“ löschen" title="Löschen">${icon('trash')}</button>`
    : m.since ? '<button type="button" class="btn small" data-ms-recount>Alles zählen</button>'
    : m.reached || m.count ? '<button type="button" class="btn small ghost" data-ms-recount>Neu zählen</button>' : '';
  return `
    <div class="ms-row ${m.hidden ? 'off' : ''}" data-id="${esc(m.id)}">
      <div class="ms-move">
        <button type="button" class="btn ghost small icon-only" data-ms-up aria-label="„${esc(m.title)}“ nach vorn">${icon('arrowUp')}</button>
        <button type="button" class="btn ghost small icon-only" data-ms-down aria-label="„${esc(m.title)}“ nach hinten">${icon('arrowDown')}</button>
      </div>
      <div class="ms-what">
        <div class="t">${esc(m.title)} ${state}${m.own ? ' <span class="badge">eigener</span>' : ''}${m.hidden ? ' <span class="badge">ausgeblendet</span>' : ''}</div>
        ${about ? `<div class="d">${esc(about)}</div>` : ''}
      </div>
      <div class="actions">
        ${act}
        <button type="button" class="btn small ghost" data-ms-hide>${m.hidden ? 'Einblenden' : 'Ausblenden'}</button>
      </div>
    </div>`;
}
