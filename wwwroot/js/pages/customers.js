// Kunden: everyone who got a code, searchable, with a way to add someone by hand.

import { api, post } from '../api.js';
import { loadOverview } from '../store.js';
import { sharedClick } from '../parts.js';
import { dialog, esc, fmtDate, icon, toast } from '../ui.js';

let query = '';

export async function render(main) {
  const [o, reg] = await Promise.all([loadOverview(), api('/api/register')]);
  const productName = (id) => o.projects.find((p) => p.id === id)?.name ?? id;

  const rows = reg.customers.map((c) => {
    const licenses = reg.licenses.filter((l) => l.customerId === c.id);
    const last = licenses.map((l) => l.issued).sort().at(-1) ?? c.created;
    return { c, licenses, last };
  }).sort((a, b) => b.last.localeCompare(a.last));

  const loose = reg.licenses.filter((l) => !l.customerId);

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
            <thead><tr><th>Name</th><th>E-Mail</th><th>Codes</th><th>Zuletzt</th><th class="num">Offen</th></tr></thead>
            <tbody>
              ${rows.map(({ c, licenses, last }) => `
                <tr class="link" data-href="#/kunden/${esc(c.id)}" data-search="${esc(searchText(c, licenses))}">
                  <td><strong>${esc(c.name)}</strong></td>
                  <td class="muted">${esc(c.email || '–')}</td>
                  <td>${summary(licenses, productName)}</td>
                  <td>${esc(fmtDate(last))}</td>
                  <td class="num">${unpaid(licenses)}</td>
                </tr>`).join('')}
              ${loose.length ? `
                <tr class="link" data-href="#/kunden/ohne" data-search="${esc(loose.map((l) => `${l.machineId} ${l.code} ${l.note}`).join(' ').toLowerCase())}">
                  <td><span class="muted">Ohne Kunde</span></td>
                  <td class="muted">–</td>
                  <td>${summary(loose, productName)}</td>
                  <td>${esc(fmtDate(loose.map((l) => l.issued).sort().at(-1)))}</td>
                  <td class="num">${unpaid(loose)}</td>
                </tr>` : ''}
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
    const row = e.target.closest('tr[data-href]');
    if (row && !e.target.closest('a,button')) location.hash = row.dataset.href;
  };
}

function filter(main) {
  const words = query.toLowerCase().split(/\s+/).filter(Boolean);
  let shown = 0;
  main.querySelectorAll('tr[data-search]').forEach((row) => {
    const hit = words.every((w) => row.dataset.search.includes(w));
    row.hidden = !hit;
    if (hit) shown++;
  });
  const none = main.querySelector('#no-hits');
  if (none) none.hidden = shown > 0;
}

const searchText = (c, licenses) =>
  [c.name, c.email, c.note, ...licenses.flatMap((l) => [l.machineId, l.code, l.note])].join(' ').toLowerCase();

function summary(licenses, productName) {
  if (!licenses.length) return '<span class="muted">–</span>';
  const counts = {};
  licenses.forEach((l) => { counts[l.product] = (counts[l.product] ?? 0) + 1; });
  return Object.entries(counts)
    .map(([product, n]) => `<span class="badge accent">${esc(productName(product))}${n > 1 ? ` × ${n}` : ''}</span>`)
    .join(' ');
}

function unpaid(licenses) {
  const n = licenses.filter((l) => !l.paid && l.price > 0).length;
  return n ? `<span class="badge bad">${n}</span>` : '<span class="muted">–</span>';
}

async function newCustomer() {
  const customer = await dialog({
    title: 'Kunde anlegen',
    confirm: 'Anlegen',
    body: `
      <label class="field"><span>Name</span><input type="text" name="name" required></label>
      <label class="field"><span>E-Mail <em>optional</em></span><input type="email" name="email"></label>
      <label class="field"><span>Notiz <em>optional</em></span><textarea name="note" rows="2"></textarea></label>`,
    submit: (v) => post('/api/customers', v),
  });
  if (!customer) return;
  toast(`${customer.name} angelegt`);
  location.hash = `#/kunden/${customer.id}`;
}
