// Verkauf und Export: make an activation code, build the plug-ins, find the files.
// The code form keeps what was typed in `form`, so a redraw (after a build, say) loses nothing.

import { api, post } from '../api.js';
import { changed, getOverview, keyOf, loadOverview } from '../store.js';
import { buildBadge, buildLine, issueList, linkChips, sharedClick } from '../parts.js';
import { replyText } from '../reply.js';
import { busy, checkMachineId, copy, esc, fmtWhen, icon, toast } from '../ui.js';

const form = {
  product: null,
  customerId: '',
  customerName: '',
  email: '',
  machineId: '',
  price: '',
  paid: false,
  note: '',
  result: null,
  lang: readLang(),
};

let register = { customers: [], licenses: [] };

export async function render(main, { params }) {
  const [o, reg] = await Promise.all([loadOverview(), api('/api/register')]);
  register = reg;

  // "Neuen Code für diesen Kunden" arrives with ?kunde=<id>
  if (params.kunde) {
    const c = reg.customers.find((x) => x.id === params.kunde);
    if (c) Object.assign(form, { customerId: c.id, customerName: c.name, email: c.email, result: null });
    history.replaceState(null, '', '#/verkauf');
  }

  const plugins = o.projects.filter((p) => p.kind === 'plugin');
  if (!plugins.some((p) => p.id === form.product)) {
    form.product = (plugins.find((p) => keyOf(o, p.id)?.inHub) ?? plugins[0])?.id ?? null;
  }

  const salesIssues = o.health.filter((h) =>
    h.level === 'problem' && ['keys', 'backup', 'secrets', 'build'].includes(h.area));

  main.innerHTML = `
    <div class="page">
      <header class="page-head">
        <div>
          <h1>Verkauf und Export</h1>
          <p class="sub">Codes erzeugen, Plugins bauen, Dateien finden.</p>
        </div>
      </header>

      ${issueList(salesIssues, { allGood: false })}

      <div class="card" id="code-card">${codeCard(o, plugins)}</div>

      <div class="card">
        <div class="card-head">
          <h3>${icon('clock')} Zuletzt ausgestellt</h3>
          <a class="btn ghost small" href="#/kunden">Alle Kunden ${icon('chevron')}</a>
        </div>
        ${recentTable(o)}
      </div>

      <div class="card">
        <div class="card-head">
          <h3>${icon('hammer')} Releases</h3>
          <button class="btn" data-build="${plugins.map((p) => p.id).join(',')}">${icon('hammer')} Alle bauen</button>
        </div>
        ${plugins.map(releaseRow).join('')}
      </div>

      <div class="card">
        <div class="card-head"><h3>${icon('folder')} Schnellzugriff</h3></div>
        <div class="chip-group"><div class="label">Allgemein</div>${linkChips(o.links)}</div>
        ${o.projects.map((p) => `<div class="chip-group"><div class="label">${esc(p.name)}</div>${linkChips(p.links)}</div>`).join('')}
      </div>
    </div>`;

  main.onclick = (e) => click(e, main);
  main.oninput = (e) => input(e, main);
  main.onchange = (e) => input(e, main);
}

// ── Code form ──────────────────────────────────────────────────────────────

function codeCard(o, plugins) {
  const match = customerMatch();
  const customerHint = !form.customerName.trim()
    ? 'Leer lassen für einen Code ohne Kunden (z. B. zum Testen).'
    : match ? 'Bestehender Kunde aus dem Register.' : 'Wird als neuer Kunde angelegt.';

  return `
    <div class="card-head"><h3>${icon('key')} Lizenzcode erzeugen</h3></div>
    <div class="form-grid">
      <div class="field wide">
        <span>Produkt</span>
        <div class="seg" role="radiogroup" aria-label="Produkt">
          ${plugins.map((p) => {
            const hasKey = keyOf(o, p.id)?.inHub;
            const on = p.id === form.product;
            return `<button type="button" role="radio" aria-checked="${on}" class="${on ? 'on' : ''}" data-product="${esc(p.id)}"
              ${hasKey ? '' : 'disabled title="Für dieses Plugin gibt es noch keinen Schlüssel"'}>${icon(p.icon)} ${esc(p.name)}</button>`;
          }).join('')}
        </div>
      </div>
      <label class="field">
        <span>Kunde</span>
        <input type="text" name="customerName" list="customer-names" autocomplete="off" placeholder="Name – neu oder aus dem Register" value="${esc(form.customerName)}">
        <datalist id="customer-names">${register.customers.map((c) => `<option value="${esc(c.name)}">`).join('')}</datalist>
        <span class="hint" id="customer-hint">${esc(customerHint)}</span>
      </label>
      <label class="field">
        <span>E-Mail <em>optional</em></span>
        <input type="email" name="email" autocomplete="off" placeholder="name@beispiel.de" value="${esc(form.email)}">
      </label>
      <div class="field wide">
        <span>Geräte-ID aus dem Aktivierungsfenster</span>
        <div class="inline">
          <input type="text" class="mono" name="machineId" autocomplete="off" spellcheck="false" placeholder="3F2A-91C0-7B44-E105-D2A8-6C19" value="${esc(form.machineId)}">
          <button type="button" class="btn" data-this-pc title="Die Geräte-ID dieses PCs einsetzen – für deine eigene Aktivierung">${icon('pc')} Dieser PC</button>
        </div>
        <span class="err" id="machine-error"></span>
      </div>
      <label class="field">
        <span>Preis <em>optional</em></span>
        <input type="text" name="price" inputmode="decimal" placeholder="49,00 €" value="${esc(form.price)}">
      </label>
      <label class="field">
        <span>Notiz <em>optional</em></span>
        <input type="text" name="note" placeholder="z. B. Rechnung 2026-014" value="${esc(form.note)}">
      </label>
      <div class="field wide">
        <label class="check"><input type="checkbox" name="paid" ${form.paid ? 'checked' : ''}> Ist bezahlt</label>
      </div>
    </div>
    <div class="actions" style="margin-top:16px">
      <button class="btn primary" data-issue>${icon('key')} Code erzeugen</button>
      <span class="faint">Jeder Code landet automatisch im Kundenregister.</span>
    </div>
    ${form.result ? resultBox(o) : ''}`;
}

function resultBox(o) {
  const r = form.result;
  const product = o.projects.find((p) => p.id === r.license.product);
  const fit = !r.matchesPlugin
    ? `<span class="badge bad">${icon('alert')} passt nicht zum Schlüssel im Plugin</span>`
    : !r.inBuild
      ? `<span class="badge bad">${icon('alert')} die .rhp ist noch nicht neu gebaut</span>`
      : `<span class="badge ok">${icon('check')} passt zum Plugin</span>`;

  return `
    <div class="code-out">
      <div class="code-meta">
        <span class="badge ok">${icon('check')} geprüft</span>
        ${fit}
        <span class="faint">${esc(product?.name ?? r.license.product)} · ${esc(r.license.machineId)}${r.customer ? ` · ${esc(r.customer.name)}` : ''}</span>
      </div>
      <div class="code-text">${esc(r.license.code)}</div>
      <div class="actions">
        <button class="btn" data-copy-code>${icon('copy')} Code kopieren</button>
        <button class="btn" data-copy-reply>${icon('mail')} Antworttext kopieren</button>
        <div class="seg small" role="radiogroup" aria-label="Sprache des Antworttexts">
          <button type="button" role="radio" aria-checked="${form.lang === 'de'}" class="${form.lang === 'de' ? 'on' : ''}" data-lang="de">Deutsch</button>
          <button type="button" role="radio" aria-checked="${form.lang === 'en'}" class="${form.lang === 'en' ? 'on' : ''}" data-lang="en">English</button>
        </div>
        ${r.customer ? `<a class="btn ghost" href="#/kunden/${esc(r.customer.id)}">${icon('user')} Zum Kunden</a>` : ''}
      </div>
    </div>`;
}

function customerMatch() {
  const name = form.customerName.trim().toLowerCase();
  return name ? register.customers.find((c) => c.name.toLowerCase() === name) : null;
}

function input(e, main) {
  const el = e.target;
  if (!el.name || !(el.name in form)) return;
  form[el.name] = el.type === 'checkbox' ? el.checked : el.value;

  if (el.name === 'customerName') {
    const match = customerMatch();
    form.customerId = match?.id ?? '';
    if (match && !form.email) {
      form.email = match.email;
      main.querySelector('[name=email]').value = match.email;
    }
    main.querySelector('#customer-hint').textContent = !form.customerName.trim()
      ? 'Leer lassen für einen Code ohne Kunden (z. B. zum Testen).'
      : match ? 'Bestehender Kunde aus dem Register.' : 'Wird als neuer Kunde angelegt.';
  }

  if (el.name === 'machineId') {
    const check = checkMachineId(el.value);
    main.querySelector('#machine-error').textContent = check.error ?? '';
    el.classList.toggle('invalid', Boolean(check.error));
  }
}

async function click(e, main) {
  if (await sharedClick(e)) return;
  const t = e.target;

  const product = t.closest('[data-product]');
  if (product && !product.disabled) {
    form.product = product.dataset.product;
    form.result = null;
    redrawCode(main);
    return;
  }

  if (t.closest('[data-this-pc]')) {
    const { machineId } = await api('/api/this-machine');
    form.machineId = machineId;
    const field = main.querySelector('[name=machineId]');
    field.value = machineId;
    field.classList.remove('invalid');
    main.querySelector('#machine-error').textContent = '';
    return;
  }

  if (t.closest('[data-issue]')) {
    await issue(t.closest('[data-issue]'), main);
    return;
  }

  const r = form.result;
  if (t.closest('[data-copy-code]')) copy(r.license.code, 'Code');
  if (t.closest('[data-copy-reply]')) copy(reply(r), 'Antworttext');
  const lang = t.closest('[data-lang]');
  if (lang) {
    form.lang = lang.dataset.lang;
    try { localStorage.setItem('hub.replyLang', form.lang); } catch { /* private window */ }
    redrawCode(main);
  }

  const copyRow = t.closest('[data-copy-license]');
  if (copyRow) copy(copyRow.dataset.copyLicense, 'Code');
}

async function issue(button, main) {
  const check = checkMachineId(form.machineId);
  if (!check.ok) {
    main.querySelector('#machine-error').textContent = check.error || 'Füg die Geräte-ID aus dem Aktivierungsfenster ein.';
    main.querySelector('[name=machineId]').focus();
    return;
  }

  const result = await busy(button, () => post('/api/licenses', {
    product: form.product,
    machineId: check.value,
    customerId: form.customerId || null,
    customerName: form.customerId ? null : form.customerName.trim() || null,
    customerEmail: form.email.trim() || null,
    price: form.price.trim() || null,
    paid: form.paid,
    note: form.note.trim() || null,
  }));
  if (!result) return;

  // The next code starts from a clean form - only the product stays
  Object.assign(form, { result, customerId: '', customerName: '', email: '', machineId: '', price: '', paid: false, note: '' });
  toast('Code erzeugt und im Register gespeichert');
  // Draw the whole page again: the new code belongs in "Zuletzt ausgestellt" too
  changed();
}

function redrawCode(main, o = getOverview()) {
  const plugins = o.projects.filter((p) => p.kind === 'plugin');
  main.querySelector('#code-card').innerHTML = codeCard(o, plugins);
}

function reply(result) {
  const o = getOverview();
  const product = o.projects.find((p) => p.id === result.license.product);
  return replyText({
    lang: form.lang,
    customerName: result.customer?.name,
    productName: product?.name ?? result.license.product,
    command: product?.startCommand ?? '',
    machineId: result.license.machineId,
    code: result.license.code,
    signature: o.signature,
  });
}

function readLang() {
  try { return localStorage.getItem('hub.replyLang') === 'en' ? 'en' : 'de'; } catch { return 'de'; }
}

// ── Recent codes ───────────────────────────────────────────────────────────

function recentTable(o) {
  const recent = [...register.licenses].sort((a, b) => b.issued.localeCompare(a.issued)).slice(0, 5);
  if (!recent.length) return '<div class="empty">Noch keine Codes ausgestellt.</div>';

  const nameOf = (id) => register.customers.find((c) => c.id === id)?.name;
  const productOf = (id) => o.projects.find((p) => p.id === id)?.name ?? id;
  return `
    <div class="table-wrap"><table class="table">
      <thead><tr><th>Kunde</th><th>Produkt</th><th>Geräte-ID</th><th>Ausgestellt</th><th class="shrink"></th></tr></thead>
      <tbody>${recent.map((l) => `
        <tr>
          <td>${l.customerId ? `<a href="#/kunden/${esc(l.customerId)}">${esc(nameOf(l.customerId) ?? '–')}</a>` : '<span class="muted">ohne Kunde</span>'}</td>
          <td>${esc(productOf(l.product))}</td>
          <td class="mono">${esc(l.machineId)}</td>
          <td>${esc(fmtWhen(l.issued))}</td>
          <td class="shrink"><button class="btn small ghost" data-copy-license="${esc(l.code)}">${icon('copy')} Code</button></td>
        </tr>`).join('')}
      </tbody>
    </table></div>`;
}

// ── Releases ───────────────────────────────────────────────────────────────

function releaseRow(p) {
  const folder = p.links.find((l) => l.kind === 'select');
  return `
    <div class="rel">
      <div class="picon">${icon(p.icon)}</div>
      <div>
        <div class="t">${esc(p.name)} <span class="badge">${esc(p.version ?? 'ohne Version')}</span> ${buildBadge(p.build)}</div>
        <div class="faint">${buildLine(p.build)}</div>
      </div>
      <div class="actions">
        <button class="btn small" data-build="${esc(p.id)}">${icon('hammer')} Bauen</button>
        ${folder ? `<button class="btn small" data-open="${esc(folder.id)}">${icon('folder')} Ordner</button>` : ''}
      </div>
    </div>`;
}
