// Übersicht: what needs doing, the projects at a glance, the sales figures.

import { changed, loadOverview } from '../store.js';
import { buildBadge, issueList, rhinoBadges, sharedClick } from '../parts.js';
import { esc, fmtDate, fmtDateTime, fmtWhen, icon, plural } from '../ui.js';

export async function render(main) {
  const o = await loadOverview();
  const problems = o.health.filter((h) => h.level === 'problem').length;
  const hints = o.health.length - problems;
  const state = problems || hints
    ? [problems && plural(problems, 'Problem', 'Probleme'), hints && plural(hints, 'Hinweis', 'Hinweise')].filter(Boolean).join(', ')
    : 'alles in Ordnung';

  main.innerHTML = `
    <div class="page">
      <header class="page-head">
        <div>
          <h1>Übersicht</h1>
          <p class="sub">Stand ${esc(fmtDateTime(o.now))} · ${esc(state)}</p>
        </div>
        <button class="btn" data-refresh>${icon('refresh')} Aktualisieren</button>
      </header>

      <section>
        <h2>Zu erledigen</h2>
        ${issueList(o.health)}
      </section>

      <section>
        <h2>Projekte</h2>
        <div class="pcards">${o.projects.map(projectCard).join('')}</div>
      </section>

      <section>
        <h2>Verkauf</h2>
        <div class="tiles">
          <a class="tile" href="#/kunden"><div class="label">Kunden</div><div class="value">${o.register.customers}</div></a>
          <a class="tile" href="#/kunden"><div class="label">Ausgestellte Codes</div><div class="value">${o.register.licenses}</div></a>
          <a class="tile" href="#/kunden"><div class="label">Noch nicht bezahlt</div><div class="value ${o.register.unpaid ? 'bad' : ''}">${o.register.unpaid}</div></a>
          <a class="tile" href="#/schluessel"><div class="label">Schlüssel</div><div class="value">${o.keys.filter((k) => k.inHub).length} von ${o.keys.length}</div></a>
          <a class="tile" href="#/schluessel"><div class="label">Letztes Backup</div>${backupValue(o.backup)}</a>
        </div>
      </section>
    </div>`;

  main.onclick = async (e) => {
    if (await sharedClick(e)) return;
    if (e.target.closest('[data-refresh]')) changed();
  };
}

function projectCard(p) {
  const facts = [];
  if (p.build) {
    facts.push(['Gebaut', `${fmtWhen(p.build.rhpTime)} ${buildBadge(p.build)}`]);
    facts.push(['Rhino', rhinoBadges(p.rhino)]);
  }
  facts.push(['Git', p.changes ? `<span class="badge">${plural(p.changes, 'Änderung', 'Änderungen')} offen</span>` : '<span class="badge ok">sauber</span>']);
  if (p.lastCommit) facts.push(['Zuletzt', `<span title="${esc(p.lastCommit.subject)}">${esc(fmtDate(p.lastCommit.date))} · ${esc(p.lastCommit.subject)}</span>`]);

  const version = p.kind === 'library' ? 'Bibliothek' : p.version ?? 'ohne Version';
  return `
    <a class="pcard" href="#/projekt/${esc(p.id)}">
      <div class="pcard-top">
        <div class="picon">${icon(p.icon)}</div>
        <div><div class="name">${esc(p.name)}</div><div class="faint">${esc(version)}</div></div>
      </div>
      ${p.note ? `<p class="faint">${esc(p.note)}</p>` : ''}
      <div class="facts">${facts.map(([k, v]) => `<div class="fact"><span>${k}</span><span>${v}</span></div>`).join('')}</div>
    </a>`;
}

function backupValue(backup) {
  if (!backup.last) return '<div class="value small bad">keins</div>';
  const stale = backup.keysChanged ? ' <span class="badge bad">veraltet</span>' : '';
  return `<div class="value small">${esc(fmtDate(backup.last.at))}${stale}</div>`;
}
