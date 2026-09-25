// Pieces several pages share: the list of things to do, the one-click buttons that lead to
// where something gets done, links, build state, where a code stands, the mail and extend
// dialogs, and the build dock that stays in the corner while a build, release or upload runs.

import { api, post } from './api.js';
import { busy, copy, dialog, esc, fmtWhen, icon, toast } from './ui.js';
import { changed, getOverview } from './store.js';

// ── Things to do ───────────────────────────────────────────────────────────

/** `here`: the page showing the list - a button that would only lead back to it is left out. */
export function issueList(items, { allGood = true, here = null } = {}) {
  if (!items.length) return allGood ? `<div class="all-good">${icon('checkCircle')} Alles in Ordnung</div>` : '';
  return `<div class="issues">${items.map((h) => issue(h, here)).join('')}</div>`;
}

function issue(h, here) {
  const action = h.action && !(h.action.kind === 'route' && h.action.target === here) ? h.action : null;
  return `
    <div class="issue ${h.level}">
      ${icon(h.level === 'problem' ? 'alert' : 'info')}
      <div>
        <div class="t">${esc(h.title)}</div>
        ${h.detail ? `<div class="d">${esc(h.detail)}</div>` : ''}
      </div>
      ${action ? actionButton(action) : '<span></span>'}
    </div>`;
}

// ── One click to where something gets done ─────────────────────────────────

const ACTION_ICON = { route: 'arrowRight', open: 'folder', url: 'external', build: 'hammer', 'commit-hub': 'git', push: 'cloud' };

/**
 * The button for a place (see Places.cs): a spot in the hub, a file, a web page, a build.
 * `task` or `step` ("cardId:index"): what it belongs to - a hub page then shows that task or
 * test step at the top, with "Erledigt" or "Abgehakt".
 */
export function actionButton(action, { task = null, step = null, primary = false } = {}) {
  if (!action) return '';
  return `<button type="button" class="btn small ${primary ? 'primary' : ''}" data-act="${esc(action.kind)}" data-target="${esc(action.target)}"
    ${task ? `data-task="${esc(task)}"` : ''} ${step ? `data-step-ref="${esc(step)}"` : ''}>${icon(ACTION_ICON[action.kind] ?? 'arrowRight')} ${esc(action.label)}</button>`;
}

/**
 * Handles the clicks every page has in common - actions, links, build buttons.
 * Returns true when the click was one of them.
 */
export async function sharedClick(e) {
  const el = e.target.closest('[data-act],[data-open],[data-build]');
  if (!el) return false;

  if (el.dataset.open) await openLink(el.dataset.open, el);
  else if (el.dataset.build) await startBuild(el.dataset.build.split(','), el);
  else await runAction({ kind: el.dataset.act, target: el.dataset.target ?? '' }, el, { task: el.dataset.task, step: el.dataset.stepRef });
  return true;
}

export async function runAction(action, button, { task = null, step = null } = {}) {
  switch (action.kind) {
    case 'route':
      return go(task ? withParam(action.target, 'aufgabe', task) : step ? withParam(action.target, 'schritt', step) : action.target);
    case 'open': return openLink(action.target, button);
    case 'url': return void window.open(action.target, '_blank', 'noopener');
    case 'build': return startBuild(action.target.split(','), button);
    case 'commit-hub': return commitHub(button);
    case 'push': return pushRepo(button);
    default: return undefined;
  }
}

/** Goes to a hub address - also when it is the one showing, so its spot gets marked again. */
export function go(hash) {
  if (location.hash === hash) window.dispatchEvent(new HashChangeEvent('hashchange'));
  else location.hash = hash;
}

export const withParam = (hash, key, value) =>
  `${hash}${hash.includes('?') ? '&' : '?'}${key}=${encodeURIComponent(value)}`;

/** Takes a parameter out of the address without going anywhere - a one-off like "?feld=". */
export function dropParam(name) {
  const [path, query] = (location.hash || '#/').split('?');
  const rest = new URLSearchParams(query ?? '');
  rest.delete(name);
  const q = rest.toString();
  history.replaceState(null, '', q ? `${path}?${q}` : path);
}

/** Lights an element up for a moment - the spot a link led to. */
export function flash(el) {
  el.classList.remove('flash');
  void el.offsetWidth;   // restart the animation
  el.classList.add('flash');
  setTimeout(() => el.classList.remove('flash'), 2600);
}

export const openLink = (id, button) => busy(button, () => post('/api/open', { linkId: id }));

export async function pushRepo(button) {
  const view = await busy(button, () => post('/api/git/push'));
  if (view) follow(view);
}

export async function commitHub(button) {
  const result = await busy(button, () => post('/api/git/commit-hub'));
  if (result) {
    toast(result.files ? `${result.files} Hub-Dateien committed` : 'Nichts zu committen');
    changed();
  }
}

// ── Links ──────────────────────────────────────────────────────────────────

const KIND_ICON = { folder: 'folder', file: 'file', select: 'select', vscode: 'code' };

export const linkChips = (links) =>
  `<div class="chips">${links
    .map((l) => `<button class="chip" data-open="${esc(l.id)}">${icon(KIND_ICON[l.kind] ?? 'folder')} ${esc(l.label)}</button>`)
    .join('')}</div>`;

// ── Build state ────────────────────────────────────────────────────────────

export function buildBadge(build) {
  if (!build) return '';
  if (!build.exists) return '<span class="badge bad">nie gebaut</span>';
  if (build.outdated) return '<span class="badge">neu bauen</span>';
  return `<span class="badge ok">${icon('check')} aktuell</span>`;
}

export function buildLine(build) {
  if (!build?.exists) return 'Noch keine .rhp im Release-Ordner.';
  let text = `Gebaut ${fmtWhen(build.rhpTime)}`;
  if (build.productVersion) text += ` · Version ${build.productVersion}`;
  if (build.outdated) text += ` · geändert ${fmtWhen(build.newestSource)}: ${build.newestFile}`;
  if (build.locked) text += ' · Rhino hält die .rhp fest';
  return esc(text);
}

export function rhinoBadges(registrations) {
  if (!registrations.length) return '<span class="faint">–</span>';
  return registrations
    .map((r) => {
      if (r.state === 'release') return `<span class="badge ok">Rhino ${r.rhino} ${icon('check')}</span>`;
      if (r.state === 'other') return `<span class="badge bad">Rhino ${r.rhino}: anderer Ordner</span>`;
      return `<span class="badge">Rhino ${r.rhino}: fehlt</span>`;
    })
    .join(' ');
}

// ── Where a code stands ────────────────────────────────────────────────────

const STATE_CLASS = { unlimited: 'ok', valid: 'ok', ending: 'accent', expired: 'bad', oldkey: 'bad', replaced: '' };
const STATE_ORDER = ['ending', 'valid', 'unlimited', 'expired', 'replaced', 'oldkey'];

export const statusBadge = (status) =>
  `<span class="badge ${STATE_CLASS[status.state] ?? ''}">${esc(status.label)}</span>`;

/** The status that matters most for a customer: running out first, dead ones last. */
export function leadingStatus(views) {
  const live = views.filter((v) => v.status.state !== 'replaced');
  const pool = live.length ? live : views;
  return pool.sort((a, b) => STATE_ORDER.indexOf(a.status.state) - STATE_ORDER.indexOf(b.status.state))[0]?.status ?? null;
}

// ── Mails ──────────────────────────────────────────────────────────────────

const MAIL_TITLE = { code: 'Code per Mail senden', followup: 'Nachfrage senden', extension: 'Neuen Code per Mail senden' };

/**
 * Shows a mail as it would go out - subject and text editable - and sends it on request.
 * Resolves true when it was sent.
 */
export async function mailDialog(kind, licenseId, language = null) {
  let draft;
  try {
    draft = await post('/api/mails/preview', { kind, licenseId, language });
  } catch (e) {
    toast(e.message, 'bad');
    return false;
  }

  const hasTo = Boolean(draft.to);
  const sent = await dialog({
    title: MAIL_TITLE[kind] ?? 'Mail senden',
    wide: true,
    confirm: hasTo ? 'Senden' : 'Text kopieren',
    body: `
      <div class="field"><span>An</span><input type="text" value="${esc(draft.to || 'keine Adresse beim Kunden')}" readonly></div>
      <label class="field"><span>Betreff</span><input type="text" name="subject" value="${esc(draft.subject)}"></label>
      <label class="field"><span>Text</span><textarea name="body" rows="14">${esc(draft.body)}</textarea></label>
      ${hasTo ? '<p class="faint">Die Mail geht über dein Konto aus den Einstellungen raus und wird beim Kunden vermerkt.</p>'
              : '<p class="faint">Beim Kunden steht keine E-Mail-Adresse – du kannst den Text kopieren und selbst verschicken.</p>'}`,
    submit: async (v) => {
      if (!hasTo) {
        await copy(`${v.subject}\n\n${v.body}`, 'Text');
        return 'copied';
      }
      await post('/api/mails/send', { kind, licenseId, subject: v.subject, body: v.body });
      return 'sent';
    },
  });
  if (sent === 'sent') {
    toast(`Mail an ${draft.to} gesendet`);
    changed();
  }
  return sent === 'sent';
}

// ── Extend ─────────────────────────────────────────────────────────────────

/**
 * A new code for the same product and device: a longer trial, or the bought licence.
 * Resolves with the issue result, or null.
 */
export async function extendDialog(view, trialDays) {
  const l = view.license;
  const base = new Date(Math.max(Date.now(), l.end ? new Date(l.end).getTime() : 0));
  const inDays = (n) => isoDate(new Date(base.getTime() + n * 86_400_000));

  const result = await dialog({
    title: 'Code verlängern',
    confirm: 'Neuen Code erzeugen',
    body: `
      <p>Gleiches Produkt, gleiche Geräte-ID <span class="mono">${esc(l.machineId)}</span>. Der alte Code bleibt bis zu seinem Ende gültig.</p>
      <div class="field">
        <span>Art</span>
        <div class="seg" data-kind-seg>
          <button type="button" class="on" data-kind="full">Kauf – unbegrenzt</button>
          <button type="button" data-kind="trial">Test verlängern</button>
        </div>
        <input type="hidden" name="kind" value="full">
      </div>
      <div class="field" data-trial-only hidden>
        <span>Gültig bis</span>
        <div class="inline">
          <input type="date" name="end" value="${inDays(trialDays)}" min="${isoDate(new Date())}">
          <button type="button" class="btn small" data-days="14">+14 Tage</button>
          <button type="button" class="btn small" data-days="30">+30 Tage</button>
        </div>
      </div>
      <div class="form-grid">
        <label class="field"><span>Preis <em>optional</em></span><input type="text" name="price" inputmode="decimal" placeholder="49,00 €"></label>
        <label class="field"><span>Notiz <em>optional</em></span><input type="text" name="note"></label>
      </div>
      <label class="check"><input type="checkbox" name="paid"> Ist bezahlt</label>`,
    submit: (v) => post(`/api/licenses/${l.id}/extend`, {
      trial: v.kind === 'trial',
      end: v.kind === 'trial' ? v.end : null,
      price: v.price || null,
      paid: v.paid,
      note: v.note || null,
    }),
    onOpen: (form) => {
      form.addEventListener('click', (e) => {
        const kind = e.target.closest('[data-kind]');
        if (kind) {
          form.querySelectorAll('[data-kind]').forEach((b) => b.classList.toggle('on', b === kind));
          form.elements.kind.value = kind.dataset.kind;
          form.querySelector('[data-trial-only]').hidden = kind.dataset.kind !== 'trial';
        }
        const days = e.target.closest('[data-days]');
        if (days) form.elements.end.value = inDays(Number(days.dataset.days));
      });
    },
  });
  if (!result) return null;
  toast('Neuer Code erzeugt');
  changed();
  if (result.customer?.email) await mailDialog('extension', result.license.id);
  return result;
}

export const isoDate = (date) =>
  `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;

// ── Build dock ─────────────────────────────────────────────────────────────

let job = null;
let lines = [];
let timer = 0;
let expanded = false;

export async function startBuild(projects, button) {
  const view = await busy(button, () => post('/api/builds', { projects }));
  if (view) follow(view);
}

/** Shows a job the server already started - a release, for instance. */
export function follow(view) {
  job = view;
  lines = view.lines;
  expanded = true;
  renderDock();
  poll();
}

/** After a reload: pick up a build that is still running. */
export async function resumeBuild() {
  const view = await api('/api/builds/current').catch(() => null);
  if (view?.status !== 'running') return;
  job = view;
  lines = view.lines;
  renderDock();
  poll();
}

function poll() {
  clearTimeout(timer);
  timer = setTimeout(async () => {
    try {
      const view = await api(`/api/builds/current?from=${lines.length}`);
      if (view) {
        lines = lines.concat(view.lines);
        job = view;
      }
    } catch {
      return; // the offline notice takes over
    }
    renderDock();
    if (job.status === 'running') poll();
    else finished();
  }, 700);
}

function finished() {
  if (job.status === 'ok') {
    // Success needs no reading: fold the output away and leave after a while
    toast(`${title()} – fertig`);
    expanded = false;
    renderDock();
    const done = job;
    setTimeout(() => {
      if (job === done && !expanded) {
        job = null;
        renderDock();
      }
    }, 12_000);
  } else {
    expanded = true;
    renderDock();
    toast('Das hat nicht geklappt – die Ausgabe steht unten rechts.', 'bad');
  }
  changed();
}

function title() {
  if (job.title) return job.title;
  const projects = getOverview()?.projects ?? [];
  return job.projects.map((id) => projects.find((p) => p.id === id)?.name ?? id).join(' und ');
}

function renderDock() {
  let dock = document.getElementById('buildbar');
  if (!job) {
    dock?.remove();
    return;
  }
  if (!dock) {
    dock = document.createElement('div');
    dock.id = 'buildbar';
    dock.className = 'buildbar';
    dock.setAttribute('role', 'status');
    dock.addEventListener('click', dockClick);
    document.body.append(dock);
  }

  // Keep the reader's place when they scrolled up in the output
  const oldPre = dock.querySelector('pre');
  const atBottom = !oldPre || oldPre.scrollTop + oldPre.clientHeight >= oldPre.scrollHeight - 8;
  const oldScroll = oldPre?.scrollTop ?? 0;

  const running = job.status === 'running';
  const mark = running
    ? '<span class="spinner"></span>'
    : job.status === 'ok' ? `<span class="ok">${icon('checkCircle')}</span>` : `<span class="bad">${icon('alert')}</span>`;
  const heading = running ? `${title()} …` : `${title()}: ${job.status === 'ok' ? 'fertig' : 'fehlgeschlagen'}`;
  const results = running ? '' : job.results.map((r) => `${r.name}: ${r.ok ? 'ok' : r.message}`).join(' · ');

  dock.innerHTML = `
    <div class="buildbar-head">
      ${mark}
      <div class="title">${esc(heading)}${results ? `<div class="faint">${esc(results)}</div>` : ''}</div>
      <button class="btn ghost small" data-dock="toggle">${expanded ? 'Ausgabe ausblenden' : 'Ausgabe zeigen'}</button>
      ${running ? '' : `<button class="btn ghost small icon-only" data-dock="close" aria-label="Schließen">${icon('x')}</button>`}
    </div>
    ${expanded ? `<pre>${esc(lines.join('\n'))}</pre>` : ''}`;

  const pre = dock.querySelector('pre');
  if (pre) pre.scrollTop = atBottom ? pre.scrollHeight : oldScroll;
}

function dockClick(e) {
  const button = e.target.closest('[data-dock]');
  if (!button) return;
  if (button.dataset.dock === 'toggle') expanded = !expanded;
  if (button.dataset.dock === 'close') job = null;
  renderDock();
}
