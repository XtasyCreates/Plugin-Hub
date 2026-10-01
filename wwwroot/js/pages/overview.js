// Übersicht: what needs doing, the open tasks - each with the button that leads to where it gets
// done - the projects at a glance with their next step, the sales figures.

import { changed, loadOverview } from '../store.js';
import { actionButton, buildBadge, issueList, rhinoBadges, sharedClick } from '../parts.js';
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

      ${o.tasks.open ? `
        <section>
          <h2>Aufgaben <a class="faint" href="#/aufgaben">alle ${o.tasks.open} ansehen</a></h2>
          <div class="tasks compact">${o.tasks.top.map((t) => `
            <div class="task prio-${esc(t.priority)}">
              <span class="prio-dot" title="Wichtigkeit ${esc(t.priority)}"></span>
              <a class="task-body" href="#/aufgaben?feld=task-${esc(t.id)}"><div class="t">${esc(t.title)}</div></a>
              <div class="actions">
                ${t.priority === 'hoch' ? '<span class="badge bad">hoch</span>' : ''}
                ${actionButton(t.goto, { task: t.id })}
              </div>
            </div>`).join('')}
          </div>
        </section>` : ''}

      <section>
        <h2>Projekte</h2>
        <div class="pcards">${o.projects.map(projectCard).join('')}</div>
      </section>

      <section>
        <h2>Verkauf</h2>
        <div class="tiles">
          <a class="tile" href="#/kunden"><div class="label">Kunden</div><div class="value">${o.register.customers}</div></a>
          <a class="tile" href="#/kunden"><div class="label">Ausgestellte Codes</div><div class="value">${o.register.licenses}</div></a>
          <a class="tile" href="#/kunden"><div class="label">Laufende Testphasen</div><div class="value">${o.register.trials}</div></a>
          <a class="tile" href="#/mails?feld=anfragen"><div class="label">Code-Anfragen</div><div class="value ${o.inbox.new ? 'bad' : ''}">${o.inbox.new}</div></a>
          <a class="tile" href="#/mails"><div class="label">Fällige Nachfragen</div><div class="value ${o.mail.due ? 'bad' : ''}">${o.mail.due}</div></a>
          <a class="tile" href="#/kunden"><div class="label">Noch nicht bezahlt</div><div class="value ${o.register.unpaid ? 'bad' : ''}">${o.register.unpaid}</div></a>
          <a class="tile" href="#/schluessel"><div class="label">Letztes Backup</div>${backupValue(o.backup)}</a>
        </div>
      </section>
    </div>`;

  main.oninput = null;
  main.onchange = null;
  main.onclick = async (e) => {
    if (await sharedClick(e)) return;
    if (e.target.closest('[data-refresh]')) changed();
  };
}

function projectCard(p) {
  const facts = [];
  if (p.build) {
    facts.push(['Release', p.released ? esc(p.released) : '<span class="muted">noch keins</span>']);
    facts.push(['Gebaut', `${esc(fmtWhen(p.build.rhpTime))} ${buildBadge(p.build)}`]);
    facts.push(['Rhino', rhinoBadges(p.rhino)]);
  }
  facts.push(['Git', p.changes ? `<span class="badge">${plural(p.changes, 'Änderung', 'Änderungen')} offen</span>` : '<span class="badge ok">sauber</span>']);
  if (p.lastCommit) facts.push(['Zuletzt', `<span title="${esc(p.lastCommit.subject)}">${esc(fmtDate(p.lastCommit.date))} · ${esc(p.lastCommit.subject)}</span>`]);

  const plan = planLine(p.plan);
  if (plan) facts.push(['Planung', plan]);

  const next = p.plan?.next;
  return `
    <div class="pcard">
      <a class="pcard-top" href="#/projekt/${esc(p.id)}">
        <div class="picon">${icon(p.icon)}</div>
        <div><div class="name">${esc(p.name)}</div><div class="faint">${esc(kindLine(p))}</div></div>
      </a>
      ${p.note ? `<p class="faint">${esc(p.note)}</p>` : ''}
      <div class="facts">${facts.map(([k, v]) => `<div class="fact"><span>${k}</span><span>${v}</span></div>`).join('')}</div>
      ${next ? `<a class="next ${esc(next.kind)}" href="${esc(next.route)}">${icon(NEXT_ICON[next.kind] ?? 'arrowRight')}<span>${esc(next.text)}</span></a>` : ''}
    </div>`;
}

const NEXT_ICON = { problem: 'alert', test: 'flask', work: 'hammer', build: 'flag', release: 'tag' };

export function kindLine(p) {
  if (p.kind === 'library') return 'Bibliothek';
  if (p.kind === 'tool') return 'Werkzeug';
  if (p.kind === 'website') return 'Website · Vorschau im Hub';
  return [p.build?.productVersion && `Build ${p.build.productVersion}`, p.version].filter(Boolean).join(' · ') || 'noch nicht gebaut';
}

/** "2 zu testen · 1 entschieden · 8 Ideen" - what is on the board for this project. */
export function planLine(plan) {
  if (!plan) return '';
  const c = plan.counts;
  return [
    c.gebaut && `${c.gebaut} zu testen`,
    c.arbeit && `${c.arbeit} in Arbeit`,
    c.entschieden && `${c.entschieden} entschieden`,
    c.getestet && `${c.getestet} getestet`,
    c.idee && plural(c.idee, 'Idee', 'Ideen'),
  ].filter(Boolean).join(' · ');
}

function backupValue(backup) {
  if (!backup.last) return '<div class="value small bad">keins</div>';
  const stale = backup.keysChanged ? ' <span class="badge bad">veraltet</span>' : '';
  return `<div class="value small">${esc(fmtDate(backup.last.at))}${stale}</div>`;
}
