// One project: its state, where its planning stands, its files, what Rhino loads, and the
// latest commits.

import { api, post } from '../api.js';
import { loadOverview } from '../store.js';
import { buildBadge, buildLine, issueList, linkChips, rhinoBadges, sharedClick } from '../parts.js';
import { busy, confirmDialog, esc, fmtDate, icon, plural, toast } from '../ui.js';
import { STAGES } from './plan.js';

export async function render(main, { args }) {
  const [id] = args;
  const [, detail] = await Promise.all([loadOverview(), api(`/api/projects/${encodeURIComponent(id)}`)]);
  const p = detail.project;
  const isPlugin = p.kind === 'plugin';
  const isWebsite = p.kind === 'website';
  // The hub hands out the website folder under /website/ - on the phone through Tailscale too
  const site = '/website/';

  main.innerHTML = `
    <div class="page">
      <header class="page-head">
        <div style="display:flex;gap:14px;align-items:center">
          <div class="picon" style="width:44px;height:44px">${icon(p.icon)}</div>
          <div>
            <h1>${esc(p.name)}</h1>
            <p class="sub">${esc(isPlugin ? (p.released ? `Release ${p.released}` : 'noch kein Release') : p.kind === 'tool' ? 'Werkzeug' : isWebsite ? 'Website' : 'Bibliothek')}${p.version ? ` · intern ${esc(p.version)}` : ''} · Ordner ${esc(p.folder)}</p>
          </div>
        </div>
        ${isPlugin ? `<button class="btn primary" data-build="${esc(p.id)}">${icon('hammer')} Bauen</button>` : ''}
        ${isWebsite ? `<a class="btn primary" href="${site}" target="_blank" rel="noopener">${icon('external')} Website ansehen</a>` : ''}
      </header>

      ${p.note ? `<div class="card" style="margin-bottom:16px"><p>${esc(p.note)}</p></div>` : ''}

      ${issueList(detail.health)}

      <div class="card" style="margin-top:16px">
        <div class="card-head"><h3>${icon('info')} Stand</h3></div>
        <div class="facts">
          ${isPlugin ? `
            <div class="fact"><span>Build</span><span>${buildLine(p.build)} ${buildBadge(p.build)}</span></div>
            <div class="fact"><span>Datei</span><span class="mono">${esc(p.build.rhp)}${p.build.productVersion ? ` · Version ${esc(p.build.productVersion)}` : ''}</span></div>
            <div class="fact"><span>Rhino lädt</span><span>${rhinoBadges(p.rhino)}</span></div>
            <div class="fact"><span>Aktivierung</span><span>Befehle <span class="mono">${esc(p.startCommand ?? '–')}</span>, <span class="mono">${esc(p.activationCommand ?? '–')}</span></span></div>` : ''}
          ${isWebsite ? `
            <div class="fact"><span>Vorschau</span><span><a href="${site}" target="_blank" rel="noopener">${esc(location.host)}${site}</a> – ${document.body.classList.contains('phone') ? 'hier über den Handy-Zugang' : 'am PC und über den Handy-Zugang'}</span></div>
            <div class="fact"><span>Im Netz</span><span>noch nicht – der Plan dazu steht unter Planung</span></div>` : ''}
          <div class="fact"><span>Git</span><span>${p.changes ? `${plural(p.changes, 'Änderung', 'Änderungen')} nicht committed` : 'alles committed'}</span></div>
        </div>
        ${p.toolbarCache.length ? `
          <div class="actions" style="margin-top:14px">
            <span class="muted" style="flex:1">Rhino hat eine Kopie der Toolbar zwischengespeichert – neue Icons erscheinen erst, wenn sie weg ist.</span>
            <button class="btn small" data-clear-cache>${icon('trash')} Toolbar-Cache leeren</button>
          </div>` : ''}
      </div>

      ${planCard(p)}

      <div class="card">
        <div class="card-head"><h3>${icon('folder')} Dateien</h3></div>
        ${linkChips(p.links)}
      </div>

      <div class="card">
        <div class="card-head"><h3>${icon('git')} Letzte Commits</h3></div>
        ${detail.log.length ? `<ul class="log">${detail.log.map((c) => `
          <li><span class="mono muted">${esc(c.hash)}</span><span>${esc(c.subject)}</span><span class="faint">${esc(fmtDate(c.date))}</span></li>`).join('')}</ul>`
        : '<div class="empty">Noch keine Commits in diesem Ordner.</div>'}
      </div>
    </div>`;

  main.oninput = null;
  main.onchange = null;
  main.onclick = async (e) => {
    if (await sharedClick(e)) return;
    const clear = e.target.closest('[data-clear-cache]');
    if (!clear) return;
    if (!(await confirmDialog('Toolbar-Cache leeren?', 'Rhino liest die Toolbar beim nächsten Start neu aus der .rui neben der .rhp.', { confirm: 'Leeren' }))) return;
    const result = await busy(clear, () => post('/api/rhino/toolbar-cache/clear', { project: p.id }));
    if (result) {
      toast(`${plural(result.removed, 'Datei', 'Dateien')} entfernt`);
      window.dispatchEvent(new CustomEvent('hub:changed'));
    }
  };
}

function planCard(p) {
  const plan = p.plan;
  const next = plan.next;
  const board = `#/planung?projekt=${encodeURIComponent(p.id)}`;
  return `
    <div class="card" data-place="planung">
      <div class="card-head">
        <h3>${icon('board')} Planung</h3>
        <a class="btn ghost small" href="${board}">Zur Roadmap ${icon('chevron')}</a>
      </div>
      ${next ? `<a class="next ${esc(next.kind)}" href="${esc(next.route)}">${icon('arrowRight')}<span>${esc(next.text)}</span></a>` : ''}
      <div class="stage-chips">${STAGES.map((s) => `
        <a class="chip ${plan.counts[s.id] ? '' : 'zero'}" href="${board}&stufe=${s.id}">${icon(s.icon)} ${esc(s.label)} <b>${plan.counts[s.id] ?? 0}</b></a>`).join('')}
      </div>
      <div class="actions" style="margin-top:12px">
        <a class="btn small" href="#/planung/entscheidungen?projekt=${encodeURIComponent(p.id)}">${icon('question')} ${plan.openQuestions ? plural(plan.openQuestions, 'offene Frage', 'offene Fragen') : 'Entscheidungen'}</a>
        <a class="btn small" href="#/planung/versionen?projekt=${encodeURIComponent(p.id)}">${icon('history')} Versionen</a>
        <a class="btn small" href="#/planung/dokumente?projekt=${encodeURIComponent(p.id)}">${icon('book')} Dokumente</a>
      </div>
    </div>`;
}
