// Pieces several pages share: the list of things to do, links, build state and the build
// dock that stays in the corner while a build runs.

import { api, post } from './api.js';
import { busy, esc, fmtWhen, icon, toast } from './ui.js';
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
      ${action
        ? `<button class="btn small" data-act="${esc(action.kind)}" data-target="${esc(action.target)}">${esc(action.label)}</button>`
        : '<span></span>'}
    </div>`;
}

/**
 * Handles the clicks every page has in common - issue actions, links, build buttons.
 * Returns true when the click was one of them.
 */
export async function sharedClick(e) {
  const el = e.target.closest('[data-act],[data-open],[data-build]');
  if (!el) return false;

  if (el.dataset.open) await openLink(el.dataset.open, el);
  else if (el.dataset.build) await startBuild(el.dataset.build.split(','), el);
  else if (el.dataset.act === 'route') location.hash = el.dataset.target;
  else if (el.dataset.act === 'open') await openLink(el.dataset.target, el);
  else if (el.dataset.act === 'build') await startBuild([el.dataset.target], el);
  return true;
}

export const openLink = (id, button) => busy(button, () => post('/api/open', { linkId: id }));

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

// ── Build dock ─────────────────────────────────────────────────────────────

let job = null;
let lines = [];
let timer = 0;
let expanded = false;

export async function startBuild(projects, button) {
  const view = await busy(button, () => post('/api/builds', { projects }));
  if (!view) return;
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
    toast(`${names()} gebaut`);
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
    toast('Der Build ist fehlgeschlagen – die Ausgabe steht unten rechts.', 'bad');
  }
  changed();
}

function names() {
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
  const title = running ? `Baue ${names()} …` : `${names()}: ${job.status === 'ok' ? 'gebaut' : 'fehlgeschlagen'}`;
  const results = running ? '' : job.results.map((r) => `${r.name}: ${r.ok ? 'ok' : r.message}`).join(' · ');

  dock.innerHTML = `
    <div class="buildbar-head">
      ${mark}
      <div class="title">${esc(title)}${results ? `<div class="faint">${esc(results)}</div>` : ''}</div>
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
