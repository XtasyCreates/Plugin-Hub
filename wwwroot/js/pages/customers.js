// Kunden: everyone who got a code, where their codes stand, searchable, and a way to add
// someone by hand.

import { api, post } from '../api.js';
import { loadOverview } from '../store.js';
import { leadingStatus, sharedClick, statusBadge } from '../parts.js';
import { dialog, esc, fmtDate, icon, toast } from '../ui.js';

let query = '';

export async function render(main) {
  const [o, reg] = await Promise.all([loadOverview(), api('/api/register')]);
  const productName = (id) => o.projects.find((p) => p.id === id)?.name ?? id;

  const rows = reg.customers.map((c) => {
    const views = reg.licenses.filter((v) => v.license.customerId === c.id);
    const last = views.map((v) => v.license.issued).sort().at(-1) ?? c.created;
    return { c, views, last };
  }).sort((a, b) => b.last.localeCompare(a.last));

  const loose = reg.licenses.filter((v) => !v.license.customerId);

  main.innerHTML = `
    <div class="page">
      <header class="page-head">
        <div>
          <h1>Kunden</h1>
          <p class="sub">${reg.customers.length} Kunden · ${reg.licenses.length} ausgestellte Codes</p>
        </div>
        <div class="actions">
          <a class="btn" href="/api/register.csv" download>${icon('download')} Als CSV (Excel)</a>
          <button class="btn primary" data-new>${icon('plus')} Kunde anlegen</button>
        </div>
      </header>

      ${rows.length || loose.length ? `
        <div class="card">
          <div class="toolbar">
            <input type="search" name="q" placeholder="Suchen: Name, E-Mail, Geräte-ID, Code …" value="${esc(query)}" aria-label="Kunden durchsuchen">
          </div>
          <div class="table-wrap"><table class="table">
            <thead><tr><th>Name</th><th>E-Mail</th><th>Codes</th><th>Stand</th><th>Zuletzt</th><th class="num">Offen</th></tr></thead>
            <tbody>
              ${rows.map(({ c, views, last }) => row(`#/kunden/${c.id}`, `<strong>${esc(c.name)}</strong>`, c.email, views, last, productName,
                searchText(c, views))).join('')}
              ${loose.length ? row('#/kunden/ohne', '<span class="muted">Ohne Kunde</span>', '', loose,
                loose.map((v) => v.license.issued).sort().at(-1), productName,
                loose.map((v) => `${v.license.machineId} ${v.license.code} ${v.license.note}`).join(' ').toLowerCase()) : ''}
            </tbody>
          </table></div>
          <p class="empty" id="no-hits" hidden>Nichts gefunden.</p>
        </div>`
      : `<div class="empty">Noch keine Kunden. Wenn du unter „Verkauf und Export“ einen Code erzeugst, legt der Hub den Kunden hier an – oder du legst ihn gleich selbst an.</div>`}
    </div>`;

  filter(main);
  main.oninput = (e) => {
    if (e.target.name !== 'q') return;
    query = e.target.value;
    filter(main);
  };
  main.onchange = null;
  main.onclick = async (e) => {
    if (await sharedClick(e)) return;
    if (e.target.closest('[data-new]')) return newCustomer();
    const tr = e.target.closest('tr[data-href]');
    if (tr && !e.target.closest('a,button')) location.hash = tr.dataset.href;
  };
}

function row(href, name, email, views, last, productName, search) {
  const lead = leadingStatus([...views]);
  return `
    <tr class="link" data-href="${esc(href)}" data-search="${esc(search)}">
      <td>${name}</td>
      <td class="muted">${esc(email || '–')}</td>
      <td>${summary(views, productName)}</td>
      <td class="nowrap">${lead ? statusBadge(lead) : '<span class="muted">–</span>'}</td>
      <td class="nowrap">${esc(fmtDate(last))}</td>
      <td class="num">${unpaid(views)}</td>
    </tr>`;
}

function filter(main) {
  const words = query.toLowerCase().split(/\s+/).filter(Boolean);
  let shown = 0;
  main.querySelectorAll('tr[data-search]').forEach((tr) => {
    const hit = words.every((w) => tr.dataset.search.includes(w));
    tr.hidden = !hit;
    if (hit) shown++;
  });
  const none = main.querySelector('#no-hits');
  if (none) none.hidden = shown > 0;
}

const searchText = (c, views) =>
  [c.name, c.email, c.note, ...views.flatMap((v) => [v.license.machineId, v.license.code, v.license.note])].join(' ').toLowerCase();

function summary(views, productName) {
  if (!views.length) return '<span class="muted">–</span>';
  const counts = {};
  views.forEach((v) => { counts[v.license.product] = (counts[v.license.product] ?? 0) + 1; });
  return Object.entries(counts)
    .map(([product, n]) => `<span class="tag">${esc(productName(product))}${n > 1 ? ` × ${n}` : ''}</span>`)
    .join(' ');
}

function unpaid(views) {
  const n = views.filter((v) => !v.license.paid && v.license.price > 0).length;
  return n ? `<span class="badge bad">${n}</span>` : '<span class="muted">–</span>';
}

async function newCustomer() {
  const customer = await dialog({
    title: 'Kunde anlegen',
    confirm: 'Anlegen',
    body: customerFields({ name: '', email: '', note: '', language: 'de', mailConsent: true }),
    submit: (v) => post('/api/customers', { ...v, mailConsent: v.mailConsent }),
  });
  if (!customer) return;
  toast(`${customer.name} angelegt`);
  location.hash = `#/kunden/${customer.id}`;
}

/** The fields of a customer, for the new and the edit dialog. */
export function customerFields(c) {
  return `
    <label class="field"><span>Name</span><input type="text" name="name" value="${esc(c.name)}"></label>
    <label class="field"><span>E-Mail <em>optional</em></span><input type="email" name="email" value="${esc(c.email)}"></label>
    <div class="field">
      <span>Sprache der Mails</span>
      <select name="language">
        <option value="de" ${c.language === 'de' ? 'selected' : ''}>Deutsch</option>
        <option value="en" ${c.language === 'en' ? 'selected' : ''}>English</option>
      </select>
    </div>
    <label class="check"><input type="checkbox" name="mailConsent" ${c.mailConsent ? 'checked' : ''}> Darf vor dem Ende einer Testphase nachgefragt werden</label>
    <label class="field"><span>Notiz <em>optional</em></span><textarea name="note" rows="2">${esc(c.note)}</textarea></label>`;
}
