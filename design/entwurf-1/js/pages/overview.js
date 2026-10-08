// Übersicht: the figures at a glance, then what needs doing and the open tasks - each line leads
// to where it gets done - and beside them the projects with their next step.

import { changed, loadOverview } from '../store.js';
import { actionButton, buildBadge, issueList, sharedClick } from '../parts.js';
import { esc, fmtDate, fmtDateTime, fmtWhen, icon, plural } from '../ui.js';

export async function render(main) {
  const o = await loadOverview();
  const problems = o.health.filter((h) => h.level === 'problem').length;
  const hints = o.health.length - problems;
  const state = problems || hints
    ? [problems && plural(problems, 'Problem', 'Probleme'), hints && plural(hints, 'Hinweis', 'Hinweise')].filter(Boolean).join(', ')
    : 'alles in Ordnung';
  const r = o.register;

  main.innerHTML = `
    <div class="page">
      <header class="page-head">
        <div>
          <h1>Übersicht</h1>
          <p class="sub">Stand ${esc(fmtDateTime(o.now))} · ${esc(state)}</p>
        </div>
        <button class="btn" data-refresh>${icon('refresh')} Aktualisieren</button>
      </header>

      <div class="kpis">
        ${kpi('#/mails?feld=anfragen', 'key', 'Code-Anfragen', o.inbox.new, o.inbox.new ? 'warten auf Antwort' : 'keine offen', o.inbox.new ? 'attn' : '')}
        ${kpi('#/mails', 'send', 'Nachfragen', o.mail.due, o.mail.due ? 'fällig' : 'keine fällig', o.mail.due ? 'attn' : '')}
        ${kpi('#/kunden', 'hourglass', 'Testphasen', r.trials, 'laufen gerade')}
        ${kpi('#/kunden', 'users', 'Kunden', r.customers, `${plural(r.licenses, 'Code', 'Codes')} ausgestellt`)}
        ${kpi('#/kunden', 'euro', 'Nicht bezahlt', r.unpaid, r.unpaid ? 'offen' : 'alles bezahlt', r.unpaid ? 'attn' : '')}
        ${backupKpi(o.backup)}
      </div>

      <div class="dash">
        <div>
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
        </div>

        <div>
          <section>
            <h2>Projekte</h2>
            <div class="plist">${o.projects.filter((p) => p.kind !== 'extra').map(projectRow).join('')}</div>
          </section>
        </div>
      </div>
    </div>`;

  main.oninput = null;
  main.onchange = null;
  main.onclick = async (e) => {
    if (await sharedClick(e)) return;
    if (e.target.closest('[data-refresh]')) changed();
  };
}

const kpi = (href, iconName, label, value, note, mark = '') => `
  <a class="kpi ${mark}" href="${href}">
    <div class="label">${icon(iconName)} ${esc(label)}</div>
    <div class="value">${esc(String(value))}</div>
    <div class="note">${esc(note)}</div>
  </a>`;

function backupKpi(backup) {
  const stale = backup.last && (backup.keysChanged || backup.registerChanged);
  const mark = !backup.last ? 'bad' : stale ? 'attn' : '';
  const note = !backup.last ? 'noch keins' : backup.keysChanged ? 'Schlüssel seither geändert' : backup.registerChanged ? 'Register seither geändert' : 'aktuell';
  return `
    <a class="kpi ${mark}" href="#/schluessel">
      <div class="label">${icon('shield')} Letztes Backup</div>
      <div class="value small">${backup.last ? esc(fmtDate(backup.last.at)) : '–'}</div>
      <div class="note">${esc(note)}</div>
    </a>`;
}

/** One line per project: what it is, how its build stands, and where it goes next. */
function projectRow(p) {
  const next = p.plan?.next;
  const meta = [kindLine(p), p.build?.exists && `gebaut ${fmtWhen(p.build.rhpTime)}`].filter(Boolean).join(' · ');
  const state = [
    p.build ? buildBadge(p.build) : '',
    p.changes ? `<span class="badge" title="Nicht committed">${plural(p.changes, 'Änderung', 'Änderungen')}</span>` : '',
  ].join('');
  return `
    <div class="prow">
      <a class="cover" href="#/projekt/${esc(p.id)}" aria-label="${esc(p.name)} öffnen"></a>
      <div class="picon">${icon(p.icon)}</div>
      <div style="min-width:0"><div class="name">${esc(p.name)}</div><div class="meta">${esc(meta)}</div></div>
      <div class="state">${state}</div>
      ${next ? `<a class="next ${esc(next.kind)}" href="${esc(next.route)}">${icon(NEXT_ICON[next.kind] ?? 'arrowRight')}<span>${esc(next.text)}</span></a>` : ''}
    </div>`;
}

const NEXT_ICON = { problem: 'alert', test: 'flask', work: 'hammer', build: 'flag', release: 'tag' };

export function kindLine(p) {
  if (p.kind === 'library') return 'Bibliothek';
  if (p.kind === 'tool') return 'Werkzeug';
  if (p.kind === 'website') return 'Website · Vorschau im Hub';
  if (p.kind === 'extra') return 'Eigenes Projekt';
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
