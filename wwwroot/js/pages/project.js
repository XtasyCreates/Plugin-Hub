// One project: its state, where its planning stands, its files, what Rhino loads, and the
// latest commits.

import { api, post } from '../api.js';
import { changed, loadOverview, projectUrl } from '../store.js';
import { buildBadge, buildLine, follow, issueList, linkChips, rhinoBadges, sharedClick } from '../parts.js';
import { busy, confirmDialog, dialog, esc, fmtDate, fmtWhen, icon, plural, toast } from '../ui.js';
import { STAGES } from './plan.js';

export async function render(main, { args }) {
  const [id] = args;
  const [, detail] = await Promise.all([loadOverview(), api(`/api/projects/${encodeURIComponent(id)}`)]);
  const p = detail.project;
  const isPlugin = p.kind === 'plugin';
  const isWebsite = p.kind === 'website';
  // The hub hands out the website folder under /website/ - on the phone through Tailscale too
  const site = '/website/';
  // A project that runs a page of its own - the TISS Bot does
  const own = projectUrl(p);
  const kind = isPlugin ? (p.released ? `Release ${p.released}` : 'noch kein Release')
    : p.kind === 'tool' ? 'Werkzeug' : isWebsite ? 'Website' : p.kind === 'extra' ? 'Eigenes Projekt' : 'Bibliothek';

  main.innerHTML = `
    <div class="page">
      <header class="page-head">
        <div style="display:flex;gap:14px;align-items:center">
          <div class="picon" style="width:44px;height:44px">${icon(p.icon)}</div>
          <div>
            <h1>${esc(p.name)}</h1>
            <p class="sub">${esc(kind)}${p.version ? ` · intern ${esc(p.version)}` : ''} · Ordner ${esc(p.folder)}</p>
          </div>
        </div>
        ${p.build?.canDevelop ? `<button class="btn" data-build-dev="${esc(p.id)}" title="Baut mit dem Entwickler-Werkzeug (LaserDevelop) – nie für Kunden, ein Release baut immer ohne">${icon('hammer')} Entwickler-Build</button>` : ''}
        ${isPlugin || p.build ? `<button class="btn primary" data-build="${esc(p.id)}">${icon('hammer')} Bauen</button>` : ''}
        ${isWebsite ? `<a class="btn primary" href="${site}" target="_blank" rel="noopener">${icon('external')} Website ansehen</a>` : ''}
        ${own ? `<a class="btn primary" href="${esc(own)}" target="_blank" rel="noopener">${icon('external')} ${esc(p.urlLabel ?? 'Öffnen')}</a>` : ''}
      </header>

      ${p.note ? `<div class="card" style="margin-bottom:16px"><p>${esc(p.note)}</p></div>` : ''}

      ${issueList(detail.health)}

      <div class="card" style="margin-top:16px">
        <div class="card-head"><h3>${icon('info')} Stand</h3></div>
        <div class="facts">
          ${p.build ? `
            <div class="fact"><span>Build</span><span>${buildLine(p.build)} ${buildBadge(p.build)}</span></div>
            <div class="fact"><span>Datei</span><span class="mono">${esc(p.build.rhp)}${p.build.productVersion ? ` · Version ${esc(p.build.productVersion)}` : ''}</span></div>
            <div class="fact"><span>Rhino lädt</span><span>${rhinoBadges(p.rhino)}</span></div>` : ''}
          ${isPlugin ? `
            <div class="fact"><span>Aktivierung</span><span>Befehle <span class="mono">${esc(p.startCommand ?? '–')}</span>, <span class="mono">${esc(p.activationCommand ?? '–')}</span></span></div>`
            : p.build && p.startCommand ? `
            <div class="fact"><span>Befehl</span><span class="mono">${esc(p.startCommand)}</span></div>` : ''}
          ${isWebsite ? `
            <div class="fact"><span>Vorschau</span><span><a href="${site}" target="_blank" rel="noopener">${esc(location.host)}${site}</a> – ${document.body.classList.contains('phone') ? 'hier über den Handy-Zugang' : 'am PC und über den Handy-Zugang'}</span></div>
            <div class="fact"><span>Im Netz</span><span>noch nicht – der Plan dazu steht unter Planung</span></div>` : ''}
          <div class="fact"><span>Git</span><span>${p.repo
            ? `${p.changes ? `${plural(p.changes, 'Änderung', 'Änderungen')} nicht committed` : 'alles committed'} · Repository „${esc(p.repo)}“`
            : 'liegt in keinem Git-Repository'}</span></div>
        </div>
        ${commitRow(p, detail.commit)}
        ${p.toolbarCache.length ? `
          <div class="actions" style="margin-top:14px">
            <span class="muted" style="flex:1">Rhino hat eine Kopie der Toolbar zwischengespeichert – neue Icons erscheinen erst, wenn sie weg ist.</span>
            <button class="btn small" data-clear-cache>${icon('trash')} Toolbar-Cache leeren</button>
          </div>` : ''}
      </div>

      ${shareCard(p, detail.share)}

      ${planCard(p)}

      <div class="card">
        <div class="card-head"><h3>${icon('folder')} Dateien</h3></div>
        ${linkChips(p.links)}
      </div>

      <div class="card">
        <div class="card-head"><h3>${icon('git')} Letzte Commits</h3></div>
        ${detail.log.length ? `<ul class="log">${detail.log.map((c) => `
          <li><span class="mono muted">${esc(c.hash)}</span><span>${esc(c.subject)}</span><span class="faint">${esc(fmtDate(c.date))}</span></li>`).join('')}</ul>`
        : `<div class="empty">${p.repo ? 'Noch keine Commits in diesem Ordner.' : 'Dieser Ordner liegt in keinem Git-Repository.'}</div>`}
      </div>
    </div>`;

  main.oninput = null;
  main.onchange = null;
  main.onclick = async (e) => {
    if (await sharedClick(e)) return;
    const share = e.target.closest('[data-share]');
    if (share) {
      // A job like a build: the dock follows it, and the page draws itself again when it is done
      const view = await busy(share, () => post(`/api/share/${encodeURIComponent(p.id)}`));
      if (view) follow(view);
      return;
    }
    if (e.target.closest('[data-commit]')) {
      await commitDialog(p, detail.commit);
      return;
    }
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

/**
 * For a project of its own in a repository of its own (MilkDrop): one button that commits
 * everything open and uploads the repository to GitHub. Nothing open and nothing to upload:
 * no button.
 */
function commitRow(p, commit) {
  if (!commit) return '';
  const waiting = commit.remote ? commit.ahead : 0;
  if (!p.changes && !waiting) return '';

  const label = !p.changes ? 'Hochladen' : commit.remote ? 'Committen und hochladen' : 'Committen';
  const text = [
    p.changes ? 'Hält den Stand von jetzt in einem Commit fest' : '',
    commit.remote
      ? `${p.changes ? 'und lädt' : 'Lädt'} das Repository nach ${esc(commit.remote)} hoch${waiting ? ` – ${plural(waiting, 'Commit ist', 'Commits sind')} noch nicht dort` : ''}`
      : '– eine Adresse auf GitHub ist für dieses Repository noch nicht eingetragen',
  ].filter(Boolean).join(' ');
  return `
    <div class="actions" style="margin-top:14px">
      <span class="muted" style="flex:1">${text}.</span>
      <button class="btn small primary" data-commit>${icon(commit.remote ? 'upload' : 'git')} ${label}</button>
    </div>`;
}

/** Asks for the commit's description - and whether it goes to GitHub at once - then does both. */
async function commitDialog(p, commit) {
  const waiting = commit.remote ? commit.ahead : 0;
  let committed = 0;
  const result = await dialog({
    title: p.changes ? `${p.name} committen` : `${p.name} hochladen`,
    body: `
      ${p.changes ? `
        <p>${plural(p.changes, 'Änderung kommt', 'Änderungen kommen')} in einen Commit im Repository „${esc(p.repo)}“.</p>
        <label class="field"><span>Beschreibung</span><input type="text" name="message" maxlength="200"
          value="${esc(`Stand ${fmtDate(new Date().toISOString())}`)}"></label>`
      : `<p>${plural(waiting, 'Commit ist', 'Commits sind')} noch nicht auf GitHub.</p>`}
      ${!commit.remote ? '<p class="faint">Hochgeladen wird nichts: Für dieses Repository ist noch keine Adresse auf GitHub eingetragen.</p>'
      : p.changes ? `<label class="check"><input type="checkbox" name="push" checked> <span>Danach zu GitHub hochladen <span class="faint">${esc(commit.remote)}</span></span></label>`
      : `<p>Hochladen nach <span class="mono">${esc(commit.remote)}</span>?</p>`}`,
    confirm: p.changes ? 'Committen' : 'Hochladen',
    submit: async (values) => {
      // A second try after a failed upload finds nothing left to commit - the count stays
      if (p.changes) committed += (await post('/api/git/commit-project', { project: p.id, message: values.message })).changes;
      const push = commit.remote && (p.changes ? values.push : true);
      return { view: push ? await post(`/api/git/push/${encodeURIComponent(p.id)}`) : null };
    },
  });

  if (committed) toast(`${plural(committed, 'Änderung', 'Änderungen')} committed`);
  if (result?.view) follow(result.view);
  if (committed || result) changed();
}

/**
 * Copies of the project to hand on - for a project that has a script for them. Every click
 * on the button makes the next version: a folder of its own, and a ZIP of it to send.
 */
function shareCard(p, share) {
  if (!share) return '';
  return `
    <div class="card">
      <div class="card-head">
        <h3>${icon('package')} Weitergabe</h3>
        <button class="btn primary small" data-share>${icon('package')} Paket erstellen</button>
      </div>
      <p class="muted">Macht aus dem Stand von jetzt ein Paket mit allem, was ein anderer PC braucht – als neuen Unterordner in
        <span class="mono">${esc(share.folder)}</span> und als ZIP daneben. Die ZIP ist die Datei zum Verschicken.</p>
      ${share.packages.length ? `
        <div class="table-wrap" style="margin-top:14px"><table class="table">
          <thead><tr><th>Paket</th><th>Erstellt</th><th>ZIP</th><th class="shrink"></th></tr></thead>
          <tbody>${share.packages.map((k, i) => `
            <tr>
              <td><strong>${esc(k.name)}</strong>${i === 0 ? ' <span class="badge ok">neuestes</span>' : ''}</td>
              <td class="nowrap">${esc(fmtWhen(k.made))}</td>
              <td class="nowrap">${k.zip ? `${Math.round(k.zipBytes / 1_048_576)} MB` : '<span class="faint">fehlt</span>'}</td>
              <td class="shrink nowrap">
                ${k.zip ? `<button class="btn small" data-open="${esc(`path:${k.zip}`)}">${icon('select')} ZIP zeigen</button>` : ''}
                <button class="btn small" data-open="${esc(`path:${k.path}`)}">${icon('folder')} Ordner</button>
              </td>
            </tr>`).join('')}
          </tbody>
        </table></div>`
      : '<div class="empty">Noch kein Paket.</div>'}
    </div>`;
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
