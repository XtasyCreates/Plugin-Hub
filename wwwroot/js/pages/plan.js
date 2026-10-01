// Planung: the roadmap from idea to release as a board, decisions and open questions, the
// version history from Git, and the documents. Roadmap and decisions live in Hub\roadmap.json
// and Hub\decisions.json, in Git: Claude moves a card to "Gebaut" with the steps to test it,
// the user ticks them off in Rhino - or writes on the card what went wrong, for Claude to read.

import { api, del, post, put } from '../api.js';
import { changed, loadOverview } from '../store.js';
import { actionButton, dropParam, flash, runAction, sharedClick } from '../parts.js';
import { markdown } from '../markdown.js';
import { confirmDialog, copy, dialog, esc, fmtDate, fmtDateTime, icon, plural, toast } from '../ui.js';

export const STAGES = [
  { id: 'idee', label: 'Idee', icon: 'bulb' },
  { id: 'entschieden', label: 'Entschieden', icon: 'flag' },
  { id: 'arbeit', label: 'In Arbeit', icon: 'hammer' },
  { id: 'gebaut', label: 'Gebaut', icon: 'flask', hint: 'wartet auf deinen Test' },
  { id: 'getestet', label: 'Getestet', icon: 'checkCircle' },
  { id: 'veroeffentlicht', label: 'Veröffentlicht', icon: 'package' },
];
export const STAGE_LABEL = { ...Object.fromEntries(STAGES.map((s) => [s.id, s.label])), abgelehnt: 'Abgelehnt' };
const STAGE_ICON = { ...Object.fromEntries(STAGES.map((s) => [s.id, s.icon])), abgelehnt: 'ban' };

const TABS = [
  { id: '', label: 'Roadmap', icon: 'board' },
  { id: 'entscheidungen', label: 'Entscheidungen', icon: 'flag' },
  { id: 'versionen', label: 'Versionen', icon: 'history' },
  { id: 'dokumente', label: 'Dokumente', icon: 'book' },
];

const RELEASED_SHOWN = 6;

let filter = 'alle';        // a project id, 'allgemein' (no project) or 'alle'
let releasedAll = false;
let currentDoc = null;
let draft = null;           // { path, text, original, stamp } - an edit survives redraws and detours
let dragId = null;

export async function render(main, { args, params }) {
  const tab = args[0] ?? '';
  if (params.projekt) filter = params.projekt;
  const o = await loadOverview();
  if (!['alle', 'allgemein'].includes(filter) && !o.projects.some((p) => p.id === filter)) filter = 'alle';

  const view = tab === 'entscheidungen' ? await decisionsView(o)
    : tab === 'versionen' ? await versionsView(o)
    : tab === 'dokumente' ? await docsView(o, params)
    : await boardView(o);

  main.innerHTML = `
    <div class="page wide">
      <header class="page-head">
        <div>
          <h1>Planung</h1>
          <p class="sub">${view.sub}</p>
        </div>
        <div class="actions">${view.buttons ?? ''}</div>
      </header>
      <nav class="tabs" aria-label="Bereiche der Planung">${TABS.map((t) => `
        <a href="#/planung${t.id ? `/${t.id}` : ''}" class="${t.id === tab ? 'on' : ''}" ${t.id === tab ? 'aria-current="page"' : ''}>
          ${icon(t.icon)} ${t.label}${t.id === 'entscheidungen' && o.plan.openQuestions ? ` <span class="count">${o.plan.openQuestions}</span>` : ''}
        </a>`).join('')}
      </nav>
      <div class="filters" role="group" aria-label="Projekt">${filterChips(o, tab)}</div>
      ${view.html}
    </div>`;

  main.oninput = view.input ?? null;
  main.onchange = view.change ?? null;
  main.onkeydown = view.keydown ?? null;
  main.ondragstart = view.dragstart ?? null;
  main.ondragover = view.dragover ?? null;
  main.ondrop = view.drop ?? null;
  main.ondragend = view.dragend ?? null;
  main.onclick = async (e) => {
    if (await sharedClick(e)) return;
    await view.click?.(e);
  };
  view.after?.(main, params);
}

function filterChips(o, tab) {
  const base = `#/planung${tab ? `/${tab}` : ''}?projekt=`;
  const chip = (id, label, iconName = null) =>
    `<a class="chip ${filter === id ? 'on' : ''}" href="${base}${id}">${iconName ? icon(iconName) : ''}${esc(label)}</a>`;
  return chip('alle', 'Alle') + o.projects.map((p) => chip(p.id, p.name, p.icon)).join('') + chip('allgemein', 'Allgemein');
}

const matches = (project) => filter === 'alle' || (filter === 'allgemein' ? !project : project === filter);

const namer = (o) => (id) => o.projects.find((p) => p.id === id)?.name ?? 'Allgemein';

const hasProblem = (x) => Boolean(x.problem) && ['arbeit', 'gebaut'].includes(x.stage);

// ── Roadmap board ──────────────────────────────────────────────────────────

async function boardView(o) {
  const data = await api('/api/plan');
  const name = namer(o);
  const items = data.roadmap.filter((x) => matches(x.project));
  const inStage = (id) => items.filter((x) => x.stage === id);
  const find = (id) => data.roadmap.find((x) => x.id === id);

  const column = (s) => {
    const all = inStage(s.id);
    let shown = all;
    if (s.id === 'veroeffentlicht' && !releasedAll && all.length > RELEASED_SHOWN) {
      shown = [...all].sort((a, b) => (b.stageAt ?? '').localeCompare(a.stageAt ?? '')).slice(0, RELEASED_SHOWN);
    }
    return `
      <div class="col col-${s.id}" data-col="${s.id}">
        <div class="col-head">${icon(s.icon)}<span>${s.label}</span><span class="count">${all.length}</span></div>
        ${s.hint ? `<div class="col-hint">${s.hint}</div>` : ''}
        <div class="col-body" data-drop="${s.id}">
          ${shown.map((x) => card(x, name)).join('') || '<div class="col-empty">–</div>'}
        </div>
        ${shown.length < all.length ? `<button class="more" data-released-all>${all.length - shown.length} weitere anzeigen</button>` : ''}
      </div>`;
  };

  const rejected = inStage('abgelehnt');
  const toTest = inStage('gebaut').length;
  const html = `
    <div class="board">${STAGES.map(column).join('')}</div>
    <details class="rejected" data-drop="abgelehnt">
      <summary>${icon('ban')} Abgelehnt <span class="count">${rejected.length}</span>
        <span class="faint">– mit Grund, damit sie nicht wiederkommen. Zieh eine Karte hierher, um sie abzulehnen.</span></summary>
      ${rejected.length
        ? `<div class="rej-list">${rejected.map((x) => `
            <div class="rej" role="button" tabindex="0" data-rejected="${esc(x.id)}">
              <div class="t">${esc(x.title)}${filter === 'alle' ? ` <span class="badge">${esc(name(x.project))}</span>` : ''}</div>
              <div class="d">${esc(x.reason ?? '')}</div>
            </div>`).join('')}</div>`
        : '<p class="faint">Noch nichts abgelehnt.</p>'}
    </details>`;

  const open = (item) => item && cardDialog(o, data, item);

  return {
    sub: `Von der Idee bis zum Release – ziehen, um weiterzuschieben.${toTest ? ` ${plural(toTest, 'Karte wartet', 'Karten warten')} auf deinen Test.` : ''} Liegt in Hub\\roadmap.json, Claude sieht dieselbe Liste.`,
    buttons: `<button class="btn primary" data-new-card>${icon('plus')} Neuer Punkt</button>`,
    html,
    click: async (e) => {
      const c = e.target.closest('[data-card],[data-rejected]');
      if (c) return open(find(c.dataset.card ?? c.dataset.rejected));
      if (e.target.closest('[data-new-card]')) {
        return cardDialog(o, data, null, { project: ['alle', 'allgemein'].includes(filter) ? null : filter });
      }
      if (e.target.closest('[data-released-all]')) {
        releasedAll = true;
        changed();
      }
    },
    keydown: (e) => {
      if (e.key !== 'Enter' && e.key !== ' ') return;
      const c = e.target.closest('[data-card],[data-rejected]');
      if (!c) return;
      e.preventDefault();
      open(find(c.dataset.card ?? c.dataset.rejected));
    },
    dragstart: (e) => {
      const c = e.target.closest?.('[data-card]');
      if (!c) return;
      dragId = c.dataset.card;
      e.dataTransfer.effectAllowed = 'move';
      e.dataTransfer.setData('text/plain', dragId);
      requestAnimationFrame(() => c.classList.add('dragging'));
    },
    dragover: (e) => {
      const zone = e.target.closest?.('[data-drop]');
      if (!zone || !dragId) return;
      e.preventDefault();
      e.dataTransfer.dropEffect = 'move';
      showDropSpot(e.currentTarget, zone, e.clientY);
    },
    drop: async (e) => {
      const zone = e.target.closest?.('[data-drop]');
      if (!zone || !dragId) return;
      e.preventDefault();
      const id = dragId;
      const before = e.currentTarget.querySelector('.drop-marker')?.nextElementSibling?.dataset?.card ?? null;
      clearDrop(e.currentTarget);
      dragId = null;
      await moveCard(find(id), zone.dataset.drop, before);
    },
    dragend: (e) => {
      dragId = null;
      clearDrop(e.currentTarget);
    },
    after: (main, params) => {
      if (params.stufe) {
        const col = main.querySelector(`[data-col="${CSS.escape(params.stufe)}"]`);
        dropParam('stufe');
        if (col) {
          col.scrollIntoView({ block: 'nearest', inline: 'center' });
          flash(col);
        }
      }
      // Two redraws close together (a link, the tab coming into view) must not open the card twice
      if (params.karte && !document.querySelector('dialog[open]')) {
        dropParam('karte');
        open(find(params.karte));
      }
    },
  };
}

function card(x, name) {
  const done = x.tests.filter((t) => t.done).length;
  const problem = hasProblem(x);
  return `
    <div class="rcard ${x.kind === 'fehler' ? 'bug' : ''} ${problem ? 'problem' : ''}" role="button" tabindex="0"
         draggable="true" data-card="${esc(x.id)}" title="${esc(x.note || x.title)}">
      <div class="rt">${x.kind === 'fehler' ? icon('bug') : ''}<span>${esc(x.title)}</span></div>
      <div class="rmeta">
        ${filter === 'alle' ? `<span class="badge">${esc(name(x.project))}</span>` : ''}
        ${x.built ? `<span class="badge">${esc(x.built)}</span>` : ''}
        ${x.release ? `<span class="badge accent">${esc(x.release)}</span>` : ''}
        ${x.tests.length && x.stage !== 'veroeffentlicht' ? `<span class="badge ${done === x.tests.length ? 'ok' : ''}" title="Test-Schritte abgehakt">${icon('flask')} ${done}/${x.tests.length}</span>` : ''}
        ${problem ? `<span class="badge bad">${icon('alert')} Problem</span>` : ''}
      </div>
    </div>`;
}

function showDropSpot(main, zone, y) {
  main.querySelectorAll('.drop-zone').forEach((z) => z !== zone && z.classList.remove('drop-zone'));
  zone.classList.add('drop-zone');
  if (zone.tagName === 'DETAILS') {
    main.querySelector('.drop-marker')?.remove();
    return;   // turned down: no place in a list, just the pile
  }
  let marker = main.querySelector('.drop-marker');
  if (!marker) {
    marker = document.createElement('div');
    marker.className = 'drop-marker';
  }
  const next = [...zone.querySelectorAll('[data-card]')]
    .filter((c) => c.dataset.card !== dragId)
    .find((c) => {
      const r = c.getBoundingClientRect();
      return y < r.top + r.height / 2;
    });
  if (next) zone.insertBefore(marker, next);
  else zone.append(marker);
}

function clearDrop(main) {
  main.querySelector('.drop-marker')?.remove();
  main.querySelectorAll('.drop-zone,.dragging').forEach((x) => x.classList.remove('drop-zone', 'dragging'));
}

async function moveCard(item, stage, before) {
  if (!item) return;
  let reason = null;
  if (stage === 'abgelehnt' && item.stage !== 'abgelehnt') {
    reason = await askReason(item);
    if (reason === null) return;
  }
  const left = item.tests.filter((t) => !t.done).length;
  if (stage === 'getestet' && item.stage !== 'getestet' && left
      && !(await confirmDialog('Wirklich getestet?', `${plural(left, 'Test-Schritt ist', 'Test-Schritte sind')} noch nicht abgehakt.`, { confirm: 'Trotzdem getestet' }))) {
    return;
  }
  try {
    await post(`/api/roadmap/${item.id}/move`, { stage, before, reason });
    if (stage !== item.stage) toast(`„${item.title}“ → ${STAGE_LABEL[stage]}`);
  } catch (ex) {
    toast(ex.message, 'bad');
  }
  changed();
}

const askReason = (item) => dialog({
  title: 'Idee ablehnen',
  confirm: 'Ablehnen',
  body: `
    <p>„${esc(item.title)}“ kommt unter „Abgelehnt“ – mit dem Grund, damit sie nicht noch einmal vorgeschlagen wird.</p>
    <label class="field"><span>Warum?</span><textarea name="reason" rows="3">${esc(item.reason ?? '')}</textarea></label>`,
  submit: (v) => {
    if (!v.reason.trim()) throw new Error('Schreib kurz dazu, warum.');
    return v.reason.trim();
  },
});

/** A card: see and change everything, tick test steps, report a problem, hand it to Claude. */
async function cardDialog(o, data, item, defaults = {}) {
  const isNew = !item;
  const x = item ?? {
    id: null, title: '', note: '', project: defaults.project ?? null, stage: defaults.stage ?? 'idee', kind: 'funktion',
    tests: [], built: null, release: null, problem: null, reason: null, source: null,
  };
  const name = namer(o);
  const done = x.tests.filter((t) => t.done).length;
  const kind = o.projects.find((p) => p.id === x.project)?.kind;
  const testWhere = kind === 'tool' ? 'Test im Hub' : kind === 'website' ? 'Test im Browser' : kind ? 'Test in Rhino' : 'Test';
  let warned = false;

  const saved = await dialog({
    title: isNew ? 'Neuer Punkt' : `${name(x.project)} · ${STAGE_LABEL[x.stage]}`,
    wide: true,
    confirm: isNew ? 'Anlegen' : 'Speichern',
    body: `
      <label class="field"><span>Titel</span><input type="text" name="title" value="${esc(x.title)}" autocomplete="off"></label>
      ${isNew ? '<div class="similar" data-similar hidden></div>' : ''}
      <div class="form-grid three">
        <label class="field"><span>Projekt</span>
          <select name="project"><option value="">Allgemein</option>${o.projects.map((p) => `<option value="${esc(p.id)}" ${x.project === p.id ? 'selected' : ''}>${esc(p.name)}</option>`).join('')}</select></label>
        <label class="field"><span>Stufe</span>
          <select name="stage">${Object.entries(STAGE_LABEL).map(([id, label]) => `<option value="${id}" ${x.stage === id ? 'selected' : ''}>${label}</option>`).join('')}</select></label>
        <label class="field"><span>Art</span>
          <select name="kind"><option value="funktion">Funktion</option><option value="fehler" ${x.kind === 'fehler' ? 'selected' : ''}>Fehler</option></select></label>
      </div>
      <label class="field"><span>Notiz <em>optional</em></span><textarea name="note" rows="3">${esc(x.note)}</textarea></label>
      ${!isNew && x.tests.length ? `
        <div class="field">
          <span>${testWhere} <em data-progress>${done} von ${x.tests.length} abgehakt</em></span>
          <div class="steps">${x.tests.map((t, i) => `
            <div class="step-row">
              <label class="check step"><input type="checkbox" data-step="${i}" ${t.done ? 'checked' : ''}><span>${esc(t.text)}</span></label>
              ${actionButton(t.goto, { step: `${x.id}:${i}` })}
            </div>`).join('')}
          </div>
        </div>` : ''}
      <label class="field" data-when="problem" ${x.problem || ['arbeit', 'gebaut'].includes(x.stage) ? '' : 'hidden'}>
        <span>Was beim Test nicht geklappt hat <em>Claude liest das in der nächsten Sitzung</em></span>
        <textarea name="problem" rows="3">${esc(x.problem ?? '')}</textarea></label>
      <label class="field" data-when="reason" ${x.stage === 'abgelehnt' ? '' : 'hidden'}>
        <span>Warum abgelehnt</span><textarea name="reason" rows="2">${esc(x.reason ?? '')}</textarea></label>
      <details class="more-fields">
        <summary>Test-Schritte, Version und Quelle</summary>
        <label class="field"><span>Test-Schritte <em>einer pro Zeile – was in Rhino zu prüfen ist</em></span>
          <textarea name="tests" rows="4">${esc(x.tests.map((t) => t.text).join('\n'))}</textarea></label>
        <div class="form-grid three">
          <label class="field"><span>Gebaut in <em>Version</em></span><input type="text" name="built" value="${esc(x.built ?? '')}" placeholder="v48"></label>
          <label class="field"><span>Release</span><input type="text" name="release" value="${esc(x.release ?? '')}" placeholder="1.0.0"></label>
          <label class="field"><span>Quelle</span><input type="text" name="source" value="${esc(x.source ?? '')}"></label>
        </div>
      </details>
      ${isNew ? '' : `
        <div class="actions dlg-actions">
          ${x.stage === 'gebaut' ? `<button type="button" class="btn small" data-then="tested">${icon('checkCircle')} Getestet – alles klappt</button>` : ''}
          <button type="button" class="btn small" data-claude>${icon('clipboard')} Auftrag für Claude kopieren</button>
          <span class="spacer"></span>
          <button type="button" class="btn small danger" data-remove>${icon('trash')} Löschen</button>
        </div>
        <p class="faint">${esc(cardMeta(x))}</p>`}
      <input type="hidden" name="then" value="">`,
    onOpen: (form) => {
      const toggle = () => {
        const stage = form.elements.stage.value;
        form.querySelector('[data-when="problem"]').hidden = !(['arbeit', 'gebaut'].includes(stage) || form.elements.problem.value.trim());
        form.querySelector('[data-when="reason"]').hidden = stage !== 'abgelehnt';
      };
      form.elements.stage.addEventListener('change', toggle);

      const similarBox = form.querySelector('[data-similar]');
      if (similarBox) {
        form.elements.title.addEventListener('input', () => {
          const hits = similar(form.elements.title.value, data.roadmap);
          similarBox.hidden = hits.length === 0;
          similarBox.innerHTML = hits.length ? `${icon('info')}<div><strong>Gibt es schon ähnlich:</strong>${hits.map((h) => `
            <div>„${esc(h.title)}“ – ${esc(STAGE_LABEL[h.stage])}${h.stage === 'abgelehnt' && h.reason ? `: ${esc(h.reason)}` : ''}</div>`).join('')}</div>` : '';
        });
      }

      form.addEventListener('change', async (e) => {
        const box = e.target.closest('[data-step]');
        if (!box) return;
        try {
          await post(`/api/roadmap/${x.id}/tests/${box.dataset.step}`, { done: box.checked });
          x.tests[Number(box.dataset.step)].done = box.checked;
          const ticked = x.tests.filter((t) => t.done).length;
          form.querySelector('[data-progress]').textContent = `${ticked} von ${x.tests.length} abgehakt`;
          if (ticked === x.tests.length) toast('Alle Schritte abgehakt – „Getestet“ schiebt die Karte weiter');
        } catch (ex) {
          box.checked = !box.checked;
          toast(ex.message, 'bad');
        }
      });

      // A step's button: a spot in the hub closes the card - that page then shows the step on top
      form.addEventListener('click', async (e) => {
        const act = e.target.closest('[data-act]');
        if (!act) return;
        e.preventDefault();
        if (act.dataset.act === 'route') form.closest('dialog').querySelector('[data-cancel]').click();
        await runAction({ kind: act.dataset.act, target: act.dataset.target }, act, { step: act.dataset.stepRef });
      });

      form.querySelector('.dlg-foot [type=submit]').addEventListener('click', () => { form.elements.then.value = ''; });
      form.querySelector('[data-then]')?.addEventListener('click', (e) => {
        form.elements.then.value = e.currentTarget.dataset.then;
        form.requestSubmit();
      });
      form.querySelector('[data-claude]')?.addEventListener('click', () => copy(claudeText(x, name), 'Auftrag'));
      form.querySelector('[data-remove]')?.addEventListener('click', async () => {
        if (!(await confirmDialog('Punkt löschen?', `„${esc(x.title)}“ verschwindet ganz. Soll er nur nicht gebaut werden, schieb ihn lieber auf „Abgelehnt“ – dann bleibt der Grund erhalten.`, { confirm: 'Löschen', danger: true }))) return;
        try {
          await del(`/api/roadmap/${x.id}`);
          form.closest('dialog').querySelector('[data-cancel]').click();
          toast('Gelöscht');
          changed();
        } catch (ex) {
          toast(ex.message, 'bad');
        }
      });
    },
    submit: async (v, form) => {
      if (v.then === 'tested' && !warned) {
        const left = [...form.querySelectorAll('[data-step]')].filter((b) => !b.checked).length;
        if (left) {
          warned = true;
          throw new Error(`${plural(left, 'Schritt ist', 'Schritte sind')} noch nicht abgehakt – klick noch einmal, um die Karte trotzdem auf „Getestet“ zu setzen.`);
        }
      }
      const body = {
        title: v.title, note: v.note, project: v.project || null, kind: v.kind,
        stage: v.then === 'tested' ? 'getestet' : v.stage,
        built: v.built, release: v.release, tests: v.tests.split('\n'), problem: v.problem, reason: v.reason, source: v.source,
      };
      return isNew ? post('/api/roadmap', body) : put(`/api/roadmap/${x.id}`, body);
    },
  });
  if (!saved) return;
  toast(isNew ? 'Angelegt' : saved.stage !== x.stage ? `→ ${STAGE_LABEL[saved.stage]}` : 'Gespeichert');
  changed();
}

function cardMeta(x) {
  const parts = [`angelegt ${fmtDate(x.created)}`];
  if (x.stageAt) parts.push(`${STAGE_LABEL[x.stage]} seit ${fmtDate(x.stageAt)}`);
  if (x.source) parts.push(`Quelle: ${x.source}`);
  return parts.join(' · ');
}

/** What to paste into the chat so Claude knows which card and what to do. */
function claudeText(x, name) {
  const where = `${name(x.project)}, Roadmap-Karte ${x.id} in Hub/roadmap.json`;
  const lines = [];
  if (hasProblem(x)) {
    lines.push(`Beim Test von „${x.title}“${x.built ? ` (${x.built})` : ''} hat etwas nicht geklappt:`, '', x.problem, '',
      'Bitte beheben und die Karte danach wieder auf „Gebaut“ setzen, mit Test-Schritten.');
  } else if (['gebaut', 'getestet', 'veroeffentlicht'].includes(x.stage)) {
    lines.push(`Zu „${x.title}“${x.built ? ` (${x.built})` : ''}:`, '');
  } else {
    lines.push(`Bitte umsetzen: „${x.title}“.`);
    if (x.note) lines.push('', x.note);
    lines.push('', 'Danach die Karte auf „Gebaut“ setzen, mit Test-Schritten für mich.');
  }
  lines.push('', `(${where})`);
  return lines.join('\n');
}

// Word stems of four letters and more: "Gravur-Ebenen" and "Ebenen gravieren" meet at "ebene"
const stems = (text) => new Set(text.toLowerCase().normalize('NFKD').replace(/[^\p{L}\p{N}\s]/gu, ' ')
  .split(/\s+/).filter((w) => w.length >= 4).map((w) => w.slice(0, 5)));

function similar(title, items) {
  const mine = stems(title);
  if (!mine.size) return [];
  return items
    .map((x) => {
      const other = stems(x.title);
      const shared = [...mine].filter((s) => other.has(s)).length;
      return { x, score: other.size ? shared / Math.min(mine.size, other.size) : 0 };
    })
    .filter((r) => r.score >= 0.5)
    .sort((a, b) => b.score - a.score)
    .slice(0, 3)
    .map((r) => r.x);
}

// ── Decisions and open questions ───────────────────────────────────────────

async function decisionsView(o) {
  const data = await api('/api/plan');
  const name = namer(o);
  const list = data.decisions.filter((d) => matches(d.project));
  const open = list.filter((d) => !d.decision);
  const made = list.filter((d) => d.decision)
    .sort((a, b) => (b.date ?? '').localeCompare(a.date ?? '') || (b.created ?? '').localeCompare(a.created ?? ''));
  const find = (id) => data.decisions.find((d) => d.id === id);

  const where = (d) => [filter === 'alle' ? name(d.project) : null, d.source].filter(Boolean).map(esc).join(' · ');
  const tools = (d) => `
    <button class="btn small ghost icon-only" data-edit-dec="${esc(d.id)}" aria-label="Bearbeiten">${icon('pencil')}</button>
    <button class="btn small ghost icon-only" data-del-dec="${esc(d.id)}" aria-label="Löschen">${icon('trash')}</button>`;

  const html = `
    <div class="card" data-place="fragen">
      <div class="card-head"><h3>${icon('question')} Offene Fragen <span class="count">${open.length}</span></h3></div>
      ${open.length ? `<div class="decs">${open.map((d) => `
        <div class="dec open">
          <div class="dec-date">${icon('question')}</div>
          <div class="dec-body"><div class="t">${esc(d.question)}</div><div class="faint">${where(d)}</div></div>
          <div class="actions"><button class="btn small primary" data-answer="${esc(d.id)}">Beantworten</button>${tools(d)}</div>
        </div>`).join('')}</div>` : '<p class="faint">Keine offenen Fragen.</p>'}
    </div>
    <div class="card" data-place="entscheidungen">
      <div class="card-head"><h3>${icon('flag')} Entscheidungen <span class="count">${made.length}</span></h3></div>
      ${made.length ? `<div class="decs">${made.map((d) => `
        <div class="dec">
          <div class="dec-date">${esc(fmtDate(d.date))}</div>
          <div class="dec-body">
            <div class="t">${esc(d.decision)}</div>
            ${d.question ? `<div class="d">Frage: ${esc(d.question)}</div>` : ''}
            <div class="faint">${where(d)}</div>
          </div>
          <div class="actions">${tools(d)}</div>
        </div>`).join('')}</div>` : '<p class="faint">Noch keine Entscheidungen festgehalten.</p>'}
    </div>`;

  return {
    sub: 'Was entschieden ist – mit Datum – und was noch offen ist. Eine beantwortete Frage wird zur Entscheidung.',
    buttons: `<button class="btn" data-new-question>${icon('question')} Frage</button>
      <button class="btn primary" data-new-decision>${icon('plus')} Entscheidung</button>`,
    html,
    click: async (e) => {
      const t = e.target;
      const project = ['alle', 'allgemein'].includes(filter) ? null : filter;
      if (t.closest('[data-new-question]')) return decisionDialog(o, { project, question: '' }, 'question');
      if (t.closest('[data-new-decision]')) return decisionDialog(o, { project, decision: '' }, 'decision');
      const answer = t.closest('[data-answer]');
      if (answer) return answerDialog(o, find(answer.dataset.answer));
      const edit = t.closest('[data-edit-dec]');
      if (edit) {
        const d = find(edit.dataset.editDec);
        return decisionDialog(o, d, d.decision ? 'decision' : 'question');
      }
      const remove = t.closest('[data-del-dec]');
      if (remove) {
        const d = find(remove.dataset.delDec);
        if (!(await confirmDialog('Eintrag löschen?', `„${esc(d.decision ?? d.question)}“ verschwindet aus der Liste.`, { confirm: 'Löschen', danger: true }))) return;
        await del(`/api/decisions/${d.id}`).catch((ex) => toast(ex.message, 'bad'));
        changed();
      }
    },
  };
}

const projectSelect = (o, value) => `
  <select name="project"><option value="">Allgemein</option>${o.projects.map((p) => `<option value="${esc(p.id)}" ${value === p.id ? 'selected' : ''}>${esc(p.name)}</option>`).join('')}</select>`;

const today = () => new Date().toLocaleDateString('sv-SE');   // yyyy-MM-dd in local time

async function decisionDialog(o, d, kind) {
  const isNew = !d.id;
  const saved = await dialog({
    title: kind === 'question' ? (isNew ? 'Offene Frage' : 'Frage bearbeiten') : (isNew ? 'Entscheidung festhalten' : 'Entscheidung bearbeiten'),
    confirm: isNew ? 'Anlegen' : 'Speichern',
    wide: kind === 'decision',
    body: `
      ${kind === 'decision' ? `
        <label class="field"><span>Entscheidung</span><textarea name="decision" rows="3">${esc(d.decision ?? '')}</textarea></label>
        <label class="field"><span>Frage dahinter <em>optional</em></span><input type="text" name="question" value="${esc(d.question ?? '')}"></label>`
      : `<label class="field"><span>Frage</span><textarea name="question" rows="3">${esc(d.question ?? '')}</textarea></label>`}
      <div class="form-grid">
        <label class="field"><span>Projekt</span>${projectSelect(o, d.project)}</label>
        ${kind === 'decision' ? `<label class="field"><span>Entschieden am</span><input type="date" name="date" value="${esc(d.date ?? today())}"></label>` : ''}
      </div>
      <label class="field"><span>Quelle <em>optional – Gespräch, Dokument …</em></span><input type="text" name="source" value="${esc(d.source ?? '')}"></label>`,
    submit: (v) => {
      const body = { project: v.project || null, question: v.question, decision: kind === 'decision' ? v.decision : null, date: v.date ?? null, source: v.source };
      return isNew ? post('/api/decisions', body) : put(`/api/decisions/${d.id}`, body);
    },
  });
  if (saved) {
    toast('Gespeichert');
    changed();
  }
}

async function answerDialog(o, d) {
  const saved = await dialog({
    title: 'Frage beantworten',
    wide: true,
    confirm: 'Als Entscheidung festhalten',
    body: `
      <p>${esc(d.question)}</p>
      <label class="field"><span>Entscheidung</span><textarea name="decision" rows="3"></textarea></label>
      <div class="form-grid">
        <label class="field"><span>Entschieden am</span><input type="date" name="date" value="${today()}"></label>
        <label class="field"><span>Projekt</span>${projectSelect(o, d.project)}</label>
      </div>
      <label class="check"><input type="checkbox" name="toRoadmap"> Daraus einen Roadmap-Punkt machen (Stufe „Entschieden“)</label>
      <label class="field" data-roadmap hidden><span>Titel des Punkts</span><input type="text" name="title" value=""></label>`,
    onOpen: (form) => {
      form.elements.toRoadmap.addEventListener('change', () => {
        form.querySelector('[data-roadmap]').hidden = !form.elements.toRoadmap.checked;
        if (form.elements.toRoadmap.checked) form.elements.title.focus();
      });
    },
    submit: async (v) => {
      if (!v.decision.trim()) throw new Error('Schreib die Entscheidung auf.');
      if (v.toRoadmap && !v.title.trim()) throw new Error('Der Roadmap-Punkt braucht einen Titel.');
      const saved = await put(`/api/decisions/${d.id}`, { project: v.project || null, question: d.question, decision: v.decision, date: v.date, source: d.source });
      if (v.toRoadmap) {
        await post('/api/roadmap', { title: v.title, project: v.project || null, stage: 'entschieden', kind: 'funktion', note: v.decision, source: `Entscheidung vom ${fmtDate(v.date)}` });
      }
      return saved;
    },
  });
  if (saved) {
    toast('Als Entscheidung festgehalten');
    changed();
  }
}

// ── Versions ───────────────────────────────────────────────────────────────

async function versionsView(o) {
  const [versions, data] = await Promise.all([api('/api/versions'), api('/api/plan')]);
  const name = namer(o);
  const mine = (projects) => filter === 'alle' || (filter === 'allgemein' ? projects.length === 0 : projects.includes(filter));
  const list = versions.versions.filter((v) => mine(v.projects));
  const untagged = versions.untagged.filter((c) => mine(c.projects));

  const built = new Map();
  for (const x of data.roadmap) {
    if (!x.built || !matches(x.project)) continue;
    if (!built.has(x.built)) built.set(x.built, []);
    built.get(x.built).push(x);
  }

  const row = (v) => {
    const cards = built.get(v.tag) ?? [];
    return `
      <div class="ver ${v.release ? 'release' : ''}">
        <div class="ver-tag"><span class="mono">${esc(v.tag)}</span><span class="faint">${esc(fmtDate(v.date))}</span></div>
        <div class="ver-body">
          <div class="t">${esc(v.subject.replace(/^v\d+\s*[-–]\s*/, ''))}</div>
          <div class="faint">${plural(v.commits, 'Commit', 'Commits')}${filter === 'alle' && v.projects.length ? ` · ${v.projects.map((p) => esc(name(p))).join(', ')}` : ''}</div>
          ${cards.length ? `<div class="chips small">${cards.map((c) => `
            <a class="chip" href="#/planung?projekt=${esc(c.project ?? 'allgemein')}&karte=${esc(c.id)}" title="${esc(STAGE_LABEL[c.stage])}">${icon(STAGE_ICON[c.stage])} ${esc(c.title)}</a>`).join('')}</div>` : ''}
        </div>
        ${v.release ? `<span class="badge accent">${icon('tag')} Release ${esc(v.version)}</span>` : '<span class="badge">intern</span>'}
      </div>`;
  };

  return {
    sub: 'Aus den Git-Tags: v1, v2 … zählt Claude intern, Releases heißen z. B. laser-1.0.0. Karten mit „Gebaut in“ hängen an ihrer Version.',
    html: `
      ${untagged.length ? `
        <div class="card">
          <div class="card-head"><h3>${icon('clock')} Seit der letzten Version <span class="count">${untagged.length}</span></h3></div>
          <ul class="log">${untagged.slice(0, 12).map((c) => `
            <li><span class="mono muted">${esc(c.hash)}</span><span>${esc(c.subject)}</span><span class="faint">${esc(fmtDate(c.date))}</span></li>`).join('')}
          </ul>
          ${untagged.length > 12 ? `<p class="faint">und ${untagged.length - 12} ältere</p>` : ''}
        </div>` : ''}
      <div class="card">
        <div class="card-head"><h3>${icon('history')} Versionen <span class="count">${list.length}</span></h3></div>
        ${list.length ? `<div class="vers">${list.map(row).join('')}</div>` : '<p class="faint">Für diese Auswahl gibt es noch keine Version.</p>'}
      </div>`,
  };
}

// ── Documents ──────────────────────────────────────────────────────────────

async function docsView(o, params) {
  const docs = await api('/api/docs');
  const shown = docs.filter((d) => matches(d.project) || d.path === params.datei);
  const path = params.datei ?? (shown.some((d) => d.path === currentDoc) ? currentDoc : shown[0]?.path ?? null);
  currentDoc = path;

  let doc = null;
  let error = null;
  if (path) {
    try {
      doc = await api(`/api/docs/content?path=${encodeURIComponent(path)}`);
    } catch (ex) {
      error = ex.message;
    }
  }
  const editing = doc && draft?.path === doc.info.path;

  const groups = [...o.projects.map((p) => [p.id, p.name]), [null, 'Allgemein']];
  const listHtml = groups.map(([id, label]) => {
    const mine = shown.filter((d) => (d.project ?? null) === id);
    return mine.length ? `
      <div class="doc-group">
        <div class="label">${esc(label)}</div>
        ${mine.map((d) => `
          <a class="doc-link ${d.path === path ? 'on' : ''}" href="#/planung/dokumente?datei=${encodeURIComponent(d.path)}" title="${esc(d.path)}">
            ${icon(d.kind === 'md' ? 'book' : 'file')}<span>${esc(d.name)}</span>${d.readOnly ? '<span class="badge">Release</span>' : ''}
          </a>`).join('')}
      </div>` : '';
  }).join('');

  const headings = [];
  let content = '';
  if (error) content = `<div class="issue problem">${icon('alert')}<div><div class="t">${esc(error)}</div></div><span></span></div>`;
  else if (!doc) content = '<p class="faint">Für diese Auswahl gibt es keine Dokumente.</p>';
  else if (editing) content = `<textarea class="doc-edit" data-doc-text spellcheck="true" aria-label="Text des Dokuments">${esc(draft.text)}</textarea>`;
  else if (doc.info.kind === 'md') content = `<div class="md">${markdown(doc.text, headings)}</div>`;
  else content = `<pre class="doc-text">${esc(doc.text)}</pre>`;

  const sections = headings.filter(([level]) => level === 2);
  const html = `
    <div class="docs">
      <nav class="doc-list" aria-label="Dokumente">${listHtml || '<p class="faint">Keine Dokumente.</p>'}</nav>
      <article class="card doc">
        ${doc ? `
          <div class="doc-head">
            <div class="doc-name">
              <h2>${esc(doc.info.name)}</h2>
              <div class="faint">${esc(doc.info.path)} · geändert ${esc(fmtDateTime(doc.info.changed))}${doc.info.readOnly ? ' · gehört zu einem Release, nur lesen' : ''}</div>
            </div>
            <div class="actions">
              ${editing
                ? `<button class="btn primary" data-doc-save>${icon('check')} Speichern</button><button class="btn" data-doc-cancel>Abbrechen</button>`
                : `${doc.info.readOnly ? '' : `<button class="btn" data-doc-edit>${icon('pencil')} Bearbeiten</button>`}
                   <button class="btn ghost" data-open="${esc(`path:${doc.info.path}`)}">${icon('folder')} Im Explorer</button>`}
            </div>
          </div>
          ${!editing && sections.length > 2 ? `<nav class="toc" aria-label="Inhalt">${sections.map(([, text, id]) => `<button type="button" data-jump="${esc(id)}">${esc(text)}</button>`).join('')}</nav>` : ''}` : ''}
        ${content}
      </article>
    </div>`;

  const dirty = () => draft && draft.text !== draft.original;

  return {
    sub: 'Konzepte, Notizen und Lizenzbedingungen – lesen und bearbeiten. Was ein Release verschickt hat, bleibt, wie es war.',
    html,
    input: (e) => {
      if (e.target.matches('[data-doc-text]') && draft) draft.text = e.target.value;
    },
    click: async (e) => {
      const t = e.target;
      // Leaving an edit with changes: ask first
      const link = t.closest('a[href]');
      if (link && dirty()) {
        e.preventDefault();
        if (await confirmDialog('Änderungen verwerfen?', 'Das Dokument ist noch nicht gespeichert.', { confirm: 'Verwerfen', danger: true })) {
          draft = null;
          location.hash = link.getAttribute('href');
        }
        return;
      }
      const jump = t.closest('[data-jump]');
      if (jump) document.getElementById(jump.dataset.jump)?.scrollIntoView({ block: 'start', behavior: 'smooth' });
      if (t.closest('[data-doc-edit]')) {
        draft = { path: doc.info.path, text: doc.text, original: doc.text, stamp: doc.stamp };
        changed();
      }
      if (t.closest('[data-doc-cancel]')) {
        if (dirty() && !(await confirmDialog('Änderungen verwerfen?', 'Was du geändert hast, geht verloren.', { confirm: 'Verwerfen', danger: true }))) return;
        draft = null;
        changed();
      }
      const save = t.closest('[data-doc-save]');
      if (save) {
        save.classList.add('busy');
        try {
          await put('/api/docs/content', { path: draft.path, text: draft.text, stamp: draft.stamp });
          draft = null;
          toast('Gespeichert');
          changed();
        } catch (ex) {
          toast(ex.message, 'bad');
        } finally {
          save.classList.remove('busy');
        }
      }
    },
    after: (main) => {
      if (editing) main.querySelector('[data-doc-text]')?.focus({ preventScroll: true });
    },
  };
}
