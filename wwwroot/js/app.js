// The frame: side bar, routing between pages, the heartbeat that keeps the hub running,
// and the notice when it stops answering.

import { onOffline, post } from './api.js';
import { confirmDialog, esc, icon } from './ui.js';
import { getOverview, loadOverview } from './store.js';
import { resumeBuild } from './parts.js';
import * as overview from './pages/overview.js';
import * as tasks from './pages/tasks.js';
import * as sales from './pages/sales.js';
import * as customers from './pages/customers.js';
import * as customer from './pages/customer.js';
import * as mails from './pages/mails.js';
import * as releases from './pages/releases.js';
import * as keys from './pages/keys.js';
import * as settings from './pages/settings.js';
import * as project from './pages/project.js';

const ROUTES = [
  { path: /^#?\/?$/, page: overview, nav: '#/' },
  { path: /^#\/aufgaben$/, page: tasks, nav: '#/aufgaben' },
  { path: /^#\/verkauf$/, page: sales, nav: '#/verkauf' },
  { path: /^#\/kunden$/, page: customers, nav: '#/kunden' },
  { path: /^#\/kunden\/([\w-]+)$/, page: customer, nav: '#/kunden' },
  { path: /^#\/mails$/, page: mails, nav: '#/mails' },
  { path: /^#\/releases$/, page: releases, nav: '#/releases' },
  { path: /^#\/schluessel$/, page: keys, nav: '#/schluessel' },
  { path: /^#\/einstellungen$/, page: settings, nav: '#/einstellungen' },
  { path: /^#\/projekt\/([\w-]+)$/, page: project, nav: null },
];

const side = document.getElementById('side');
const main = document.getElementById('main');
let quitting = false;

async function show({ keepScroll = false } = {}) {
  const [path, query] = (location.hash || '#/').split('?');
  const route = ROUTES.find((r) => r.path.test(path)) ?? ROUTES[0];
  const args = path.match(route.path)?.slice(1) ?? [];
  const params = Object.fromEntries(new URLSearchParams(query ?? ''));
  const scroll = window.scrollY;

  try {
    await route.page.render(main, { args, params });
  } catch (e) {
    main.innerHTML = `<div class="page"><div class="issue problem">${icon('alert')}<div><div class="t">Die Seite ließ sich nicht laden</div><div class="d">${esc(e.message)}</div></div><span></span></div></div>`;
  }
  drawSide(path, route);
  if (keepScroll) window.scrollTo(0, scroll);
}

function drawSide(path, route) {
  const o = getOverview();
  if (!o) return;
  const problem = (test) => o.health.some((h) => h.level === 'problem' && test(h));
  const current = route.nav ?? path;

  const link = (href, iconName, label, { alarm = false, count = null } = {}) => `
    <a href="${href}" class="${current === href ? 'on' : ''}" ${current === href ? 'aria-current="page"' : ''}>
      ${icon(iconName)}<span>${esc(label)}</span>
      ${alarm ? '<span class="dot" title="Braucht Aufmerksamkeit"></span>' : count ? `<span class="count">${count}</span>` : ''}
    </a>`;

  const rhino = o.rhino.length
    ? `<span class="led on"></span>${esc([...new Set(o.rhino.map((r) => r.label))].join(', '))} läuft`
    : '<span class="led"></span>Rhino ist geschlossen';

  side.innerHTML = `
    <div class="brand"><img src="favicon.svg" alt=""><span>Plugin Hub</span></div>
    <nav class="nav">
      ${link('#/', 'grid', 'Übersicht', { alarm: problem(() => true) })}
      ${link('#/aufgaben', 'list', 'Aufgaben', { count: o.tasks.open })}
      ${link('#/verkauf', 'package', 'Verkauf und Export')}
      ${link('#/kunden', 'users', 'Kunden', { count: o.register.customers })}
      ${link('#/mails', 'mail', 'Mails', { alarm: problem((h) => h.area === 'mail'), count: o.mail.due })}
      ${link('#/releases', 'tag', 'Releases')}
      ${link('#/schluessel', 'shield', 'Schlüssel und Backup', { alarm: problem((h) => ['keys', 'backup', 'secrets'].includes(h.area)) })}
      ${link('#/einstellungen', 'sliders', 'Einstellungen')}
      <div class="nav-label">Projekte</div>
      ${o.projects.map((p) => link(`#/projekt/${p.id}`, p.icon, p.name, { alarm: problem((h) => h.project === p.id) })).join('')}
    </nav>
    <div class="side-foot">
      <div class="rhino">${rhino}</div>
      <button class="btn ghost small" data-quit>${icon('power')} Hub beenden</button>
    </div>`;
}

side.addEventListener('click', async (e) => {
  if (!e.target.closest('[data-quit]')) return;
  if (!(await confirmDialog('Hub beenden?', 'Er startet wieder über die Desktop-Verknüpfung „Plugin Hub“.', { confirm: 'Beenden' }))) return;
  quitting = true;
  await post('/api/quit').catch(() => {});
  showOffline(true);
});

// ── Offline notice ─────────────────────────────────────────────────────────

const offline = document.getElementById('offline');

function showOffline(down) {
  offline.hidden = !down;
  if (!down) return;
  document.getElementById('offline-title').textContent = quitting ? 'Der Hub ist beendet' : 'Der Hub antwortet nicht';
  if (quitting) document.getElementById('offline-text').textContent = 'Du kannst diesen Tab schließen. Neu starten geht über die Desktop-Verknüpfung „Plugin Hub“.';
}

onOffline((down) => {
  if (down || !quitting) showOffline(down);
});

document.getElementById('offline-retry').addEventListener('click', async () => {
  quitting = false;
  try {
    await loadOverview();
    showOffline(false);
    show();
  } catch {
    showOffline(true);
  }
});

// ── Keep the hub alive while this page is open ─────────────────────────────

setInterval(() => { if (!quitting) post('/api/heartbeat').catch(() => {}); }, 30_000);
document.addEventListener('visibilitychange', () => {
  // Coming back to the tab redraws the page - unless a dialog is open, which would lose its input
  if (document.visibilityState === 'visible' && !quitting && !document.querySelector('dialog[open]')) show({ keepScroll: true });
});

window.addEventListener('hashchange', () => show());
window.addEventListener('hub:changed', () => show({ keepScroll: true }));

show();
resumeBuild();
