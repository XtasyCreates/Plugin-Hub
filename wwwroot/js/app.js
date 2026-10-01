// The frame: side bar (on the phone a narrow bar with a menu), routing between pages, the spot
// a link points at ("?feld=") and the task or test step it came from ("?aufgabe=",
// "?schritt="), the heartbeat that keeps the hub running, and the notice when it stops
// answering.

import { api, onOffline, post } from './api.js';
import { confirmDialog, esc, icon, toast } from './ui.js';
import { changed, getOverview, loadOverview, loadSession, onPhone } from './store.js';
import { dropParam, flash, resumeBuild } from './parts.js';
import * as overview from './pages/overview.js';
import * as tasks from './pages/tasks.js';
import * as plan from './pages/plan.js';
import * as sales from './pages/sales.js';
import * as stats from './pages/stats.js';
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
  { path: /^#\/planung(?:\/(entscheidungen|versionen|dokumente))?$/, page: plan, nav: '#/planung' },
  { path: /^#\/verkauf$/, page: sales, nav: '#/verkauf' },
  { path: /^#\/statistik$/, page: stats, nav: '#/statistik' },
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

// Each tab's colour (hub.css, [data-tab]) and icon - the side bar, the strip over the page and
// the tile at its title all show it, so the open tab is told at a glance
const TABS = {
  '#/': { key: 'uebersicht', icon: 'grid' },
  '#/aufgaben': { key: 'aufgaben', icon: 'list' },
  '#/planung': { key: 'planung', icon: 'board' },
  '#/verkauf': { key: 'verkauf', icon: 'package' },
  '#/statistik': { key: 'statistik', icon: 'chart' },
  '#/kunden': { key: 'kunden', icon: 'users' },
  '#/mails': { key: 'mails', icon: 'mail' },
  '#/releases': { key: 'releases', icon: 'tag' },
  '#/schluessel': { key: 'schluessel', icon: 'shield' },
  '#/einstellungen': { key: 'einstellungen', icon: 'sliders' },
};

/** Colours the page as its tab and puts the tab's tile before the title; project pages have their own tile. */
function markTab(route) {
  const tab = TABS[route.nav ?? ''];
  document.body.dataset.tab = tab?.key ?? 'projekt';
  const title = main.querySelector('.page-head h1');
  const box = title?.parentElement;
  if (!tab || !box || box.closest('.titled') || box.querySelector('.picon')) return;
  const titled = document.createElement('div');
  titled.className = 'titled';
  box.replaceWith(titled);
  titled.innerHTML = `<span class="tab-mark" aria-hidden="true">${icon(tab.icon)}</span>`;
  titled.append(box);
}

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
  if (!keepScroll) side.classList.remove('open');   // the phone's menu closes on the way to a page
  markTab(route);
  drawSide(path, route);
  if (keepScroll) window.scrollTo(0, scroll);
  if (params.feld) {
    markPlace(params.feld);
    dropParam('feld');   // once - a redraw after a save must not jump back there
  }
  if (params.aufgabe) await taskBar(params.aufgabe);
  else if (params.schritt) await stepBar(params.schritt);
}

/**
 * Scrolls to the spot a link names - a field by its name, a card by its id or data-place - and
 * lights it up; a field also gets the cursor.
 */
function markPlace(name) {
  const key = CSS.escape(name);
  const el = main.querySelector(`[data-place="${key}"]`) ?? main.querySelector(`#${key}`) ?? main.querySelector(`[name="${key}"]`);
  if (!el) return;
  const field = el.closest('.field');
  const box = field ?? el;
  box.scrollIntoView({ block: 'center' });
  flash(box);
  const control = el.matches('input,select,textarea') ? el : field?.querySelector('input:not([type=hidden]),select,textarea');
  control?.focus({ preventScroll: true });
}

/** Came here from a task: it stays in view on top, and "Erledigt" ticks it off right here. */
async function taskBar(id) {
  let task;
  try {
    task = (await api('/api/tasks')).find((t) => t.id === id);
  } catch {
    return;
  }
  if (!task) return;
  contextBar({
    iconName: 'list', label: 'Aufgabe', text: task.title, done: task.done, doneText: 'Erledigt', param: 'aufgabe',
    back: ['#/aufgaben', 'Alle Aufgaben'],
    tick: async () => {
      await post(`/api/tasks/${id}/done`, { done: true });
      toast('Aufgabe erledigt');
    },
  });
}

/** Came here from a test step on a roadmap card ("cardId:index"): the step on top, "Abgehakt" ticks it. */
async function stepBar(ref) {
  const [id, index] = ref.split(':');
  let card;
  try {
    card = (await api('/api/plan')).roadmap.find((x) => x.id === id);
  } catch {
    return;
  }
  const step = card?.tests[Number(index)];
  if (!step) return;
  contextBar({
    iconName: 'flask', label: `Test · ${card.title}`, text: step.text, done: step.done, doneText: 'Abgehakt', param: 'schritt',
    back: [`#/planung?karte=${encodeURIComponent(id)}`, 'Zur Karte'],
    tick: async () => {
      await post(`/api/roadmap/${id}/tests/${Number(index)}`, { done: true });
      toast('Schritt abgehakt');
    },
  });
}

function contextBar({ iconName, label, text, done, doneText, param, back, tick }) {
  if (main.querySelector('.taskbar')) return;
  const bar = document.createElement('div');
  bar.className = 'taskbar';
  bar.innerHTML = `
    ${icon(iconName)}
    <div class="t" title="${esc(text)}"><span class="label">${esc(label)}</span> ${esc(text)}</div>
    ${done
      ? `<span class="badge ok">${icon('check')} ${esc(doneText.toLowerCase())}</span>`
      : `<button class="btn small primary" data-bar-done>${icon('check')} ${esc(doneText)}</button>`}
    <a class="btn small ghost" href="${esc(back[0])}">${esc(back[1])}</a>
    <button class="btn small ghost icon-only" data-bar-close aria-label="Leiste schließen">${icon('x')}</button>`;
  bar.addEventListener('click', async (e) => {
    if (e.target.closest('[data-bar-done]')) {
      try {
        await tick();
        changed();
      } catch (ex) {
        toast(ex.message, 'bad');
      }
    }
    if (e.target.closest('[data-bar-close]')) {
      dropParam(param);
      bar.remove();
    }
  });
  main.prepend(bar);
}

function drawSide(path, route) {
  const o = getOverview();
  if (!o) return;
  document.title = o.inbox.new ? `(${o.inbox.new}) Plugin Hub` : 'Plugin Hub';
  const problem = (test) => o.health.some((h) => h.level === 'problem' && test(h));
  const current = route.nav ?? path;

  const link = (href, iconName, label, { alarm = false, count = null } = {}) => `
    <a href="${href}" data-tab="${TABS[href]?.key ?? 'projekt'}" class="${current === href ? 'on' : ''}" ${current === href ? 'aria-current="page"' : ''}>
      ${icon(iconName)}<span>${esc(label)}</span>
      ${alarm ? '<span class="dot" title="Braucht Aufmerksamkeit"></span>' : count ? `<span class="count">${count}</span>` : ''}
    </a>`;

  const rhino = o.rhino.length
    ? `<span class="led on"></span>${esc([...new Set(o.rhino.map((r) => r.label))].join(', '))} läuft`
    : '<span class="led"></span>Rhino ist geschlossen';

  // The page's name, for the narrow bar on the phone
  const here = { '#/': 'Übersicht', '#/aufgaben': 'Aufgaben', '#/planung': 'Planung', '#/verkauf': 'Verkauf und Export',
    '#/statistik': 'Statistik', '#/kunden': 'Kunden', '#/mails': 'Mails', '#/releases': 'Releases', '#/schluessel': 'Schlüssel und Backup',
    '#/einstellungen': 'Einstellungen' }[current] ?? o.projects.find((p) => `#/projekt/${p.id}` === current)?.name ?? '';
  const open = side.classList.contains('open');

  side.innerHTML = `
    <div class="brand">
      <img src="favicon.svg" alt=""><span>Plugin Hub</span><span class="here">${esc(here)}</span>
      <button class="btn ghost icon-only menu-btn" data-menu aria-expanded="${open}" aria-label="Menü">${icon(open ? 'x' : 'menu')}</button>
    </div>
    <nav class="nav">
      ${link('#/', 'grid', 'Übersicht', { alarm: problem(() => true) })}
      ${link('#/aufgaben', 'list', 'Aufgaben', { count: o.tasks.open })}
      ${link('#/planung', 'board', 'Planung', { count: o.plan.toTest })}
      ${link('#/verkauf', 'package', 'Verkauf und Export')}
      ${link('#/statistik', 'chart', 'Statistik')}
      ${link('#/kunden', 'users', 'Kunden', { count: o.register.customers })}
      ${link('#/mails', 'mail', 'Mails', { alarm: problem((h) => h.area === 'mail'), count: o.mail.due + o.inbox.new })}
      ${link('#/releases', 'tag', 'Releases')}
      ${link('#/schluessel', 'shield', 'Schlüssel und Backup', { alarm: problem((h) => ['keys', 'backup', 'secrets'].includes(h.area)) })}
      ${link('#/einstellungen', 'sliders', 'Einstellungen')}
      <div class="nav-label">Projekte</div>
      ${o.projects.map((p) => link(`#/projekt/${p.id}`, p.icon, p.name, { alarm: problem((h) => h.project === p.id) })).join('')}
    </nav>
    <div class="side-foot">
      <div class="rhino">${rhino}</div>
      ${onPhone() ? `<div class="rhino">${icon('phone')} Vom Handy über Tailscale</div>` : ''}
      <button class="btn ghost small" data-quit>${icon('power')} Hub beenden</button>
    </div>`;
}

side.addEventListener('click', async (e) => {
  const menu = e.target.closest('[data-menu]');
  if (menu) {
    const open = side.classList.toggle('open');
    menu.setAttribute('aria-expanded', String(open));
    menu.innerHTML = icon(open ? 'x' : 'menu');
    return;
  }
  // A tap on the page it already shows only folds the menu away
  if (e.target.closest('.nav a')) side.classList.remove('open');
  if (!e.target.closest('[data-quit]')) return;
  // With phone access on, the phone loses the hub too - until the next start
  const access = (await api('/api/session').catch(() => null))?.phoneAccess;
  const text = access
    ? 'Er startet wieder über die Desktop-Verknüpfung „Plugin Hub“ oder mit dem nächsten Windows-Start. Bis dahin erreicht ihn auch dein Handy nicht.'
    : 'Er startet wieder über die Desktop-Verknüpfung „Plugin Hub“.';
  if (!(await confirmDialog('Hub beenden?', text, { confirm: 'Beenden' }))) return;
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
  // The phone cannot restart it - it can only say what the PC needs
  else if (onPhone()) document.getElementById('offline-text').textContent =
    'Das Handy erreicht deinen PC nicht. Er muss an sein und darf nicht schlafen, und Tailscale muss auf PC und Handy verbunden sein.';
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

// It also says how many code requests wait: a new one shows as a message, in the tab's title
// and in the side bar
let requestsSeen = null;
async function heartbeat() {
  if (quitting) return;
  let n;
  try {
    n = (await post('/api/heartbeat'))?.requests ?? 0;
  } catch {
    return;
  }
  document.title = n ? `(${n}) Plugin Hub` : 'Plugin Hub';
  if (requestsSeen !== null && n > requestsSeen) {
    toast(n - requestsSeen === 1 ? 'Neue Code-Anfrage – siehe Mails' : `${n - requestsSeen} neue Code-Anfragen – siehe Mails`);
    if (!document.querySelector('dialog[open]')) changed();
  }
  requestsSeen = n;
}
setInterval(heartbeat, 30_000);
heartbeat();
document.addEventListener('visibilitychange', () => {
  // Coming back to the tab redraws the page - unless a dialog is open, which would lose its input
  if (document.visibilityState === 'visible' && !quitting && !document.querySelector('dialog[open]')) show({ keepScroll: true });
});

window.addEventListener('hashchange', () => show());
window.addEventListener('hub:changed', () => show({ keepScroll: true }));

// Where the page is open decides what it offers - asked once, before the first page
loadSession().finally(() => {
  show();
  resumeBuild();
});
