// Verkauf und Export: make an activation code - bought or a trial - send it, build the
// plug-ins, find the files. The code form keeps what was typed in `form`, so a redraw
// (after a build, say) loses nothing.

import { api, post } from '../api.js';
import { changed, getOverview, keyOf, loadOverview } from '../store.js';
import { buildBadge, buildLine, dropParam, isoDate, issueList, linkChips, mailDialog, sharedClick, statusBadge } from '../parts.js';
import { busy, checkMachineId, copy, esc, fmtDate, fmtWhen, icon, toast } from '../ui.js';

const form = {
  product: null,
  kind: 'full',
  end: '',
  customerId: '',
  customerName: '',
  email: '',
  language: null,
  machineId: '',
  price: '',
  paid: false,
  note: '',
  result: null,
};

let register = { customers: [], licenses: [] };
let shared = { trialDays: 30, defaultLanguage: 'de' };

export async function render(main, { params }) {
  const [o, reg, settings] = await Promise.all([loadOverview(), api('/api/register'), api('/api/settings')]);
  register = reg;
  shared = settings.shared;
  form.language ??= shared.defaultLanguage;
  if (!form.end) form.end = daysFromNow(shared.trialDays);

  // "Neuer Code" on a customer page arrives with ?kunde=<id>
  if (params.kunde) {
    const c = reg.customers.find((x) => x.id === params.kunde);
    if (c) Object.assign(form, { customerId: c.id, customerName: c.name, email: c.email, language: c.language, result: null });
    dropParam('kunde');
  }

  // A test step brings the form half filled: ?produkt=laser&tage=2&geraet=dieser-pc (or art=kauf)
  if (params.produkt || params.tage || params.art || params.geraet) {
    if (params.produkt) form.product = params.produkt;
    const days = Number(params.tage);
    if (days >= 1 && days <= 365) Object.assign(form, { kind: 'trial', end: daysFromNow(days) });
    if (params.art === 'kauf') form.kind = 'full';
    if (params.geraet === 'dieser-pc') {
      form.machineId = (await api('/api/this-machine')).machineId;
      if (!form.note) form.note = 'Eigener Test';
    }
    form.result = null;
    ['produkt', 'tage', 'art', 'geraet'].forEach(dropParam);
  }

  const plugins = o.projects.filter((p) => p.kind === 'plugin');
  if (!plugins.some((p) => p.id === form.product)) {
    form.product = (plugins.find((p) => keyOf(o, p.id)?.inHub) ?? plugins[0])?.id ?? null;
  }

  const salesIssues = o.health.filter((h) =>
    h.level === 'problem' && ['keys', 'backup', 'secrets', 'build', 'mail'].includes(h.area));

  main.innerHTML = `
    <div class="page">
      <header class="page-head">
        <div>
          <h1>Verkauf und Export</h1>
          <p class="sub">Codes erzeugen und verschicken, Plugins bauen, Dateien finden.</p>
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

      <div class="card" id="plugins">
        <div class="card-head">
          <h3>${icon('hammer')} Plugins</h3>
          <div class="actions">
            <a class="btn ghost small" href="#/releases">${icon('tag')} Releases</a>
            <button class="btn" data-build="${plugins.map((p) => p.id).join(',')}">${icon('hammer')} Alle testen (bauen)</button>
          </div>
        </div>
        ${plugins.map(pluginRow).join('')}
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
  const trial = form.kind === 'trial';

  return `
    <div class="card-head"><h3>${icon('key')} Lizenzcode erzeugen</h3></div>
    <div class="form-grid">
      <div class="field">
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
      <div class="field">
        <span>Art</span>
        <div class="seg" role="radiogroup" aria-label="Art des Codes">
          <button type="button" role="radio" aria-checked="${!trial}" class="${trial ? '' : 'on'}" data-kind="full">${icon('check')} Kauf – unbegrenzt</button>
          <button type="button" role="radio" aria-checked="${trial}" class="${trial ? 'on' : ''}" data-kind="trial">${icon('hourglass')} Testphase</button>
        </div>
      </div>
      ${trial ? `
        <div class="field wide">
          <span>Gültig bis <em>der Code funktioniert bis einschließlich dieses Tages</em></span>
          <div class="inline wrap">
            <input type="date" name="end" value="${esc(form.end)}" min="${isoDate(new Date())}" style="max-width:190px">
            ${[14, 30, 60].map((n) => `<button type="button" class="btn small ${form.end === daysFromNow(n) ? 'on' : ''}" data-days="${n}">${n} Tage</button>`).join('')}
            <span class="faint">Standard: ${shared.trialDays} Tage (Einstellungen)</span>
          </div>
        </div>` : ''}
      <label class="field">
        <span>Kunde</span>
        <input type="text" name="customerName" list="customer-names" autocomplete="off" placeholder="Name – neu oder aus dem Register" value="${esc(form.customerName)}">
        <datalist id="customer-names">${register.customers.map((c) => `<option value="${esc(c.name)}">`).join('')}</datalist>
        <span class="hint" id="customer-hint">${esc(customerHint(match))}</span>
      </label>
      <div class="form-grid inner">
        <label class="field">
          <span>E-Mail ${trial ? '<em>für die Nachfrage vor Ablauf</em>' : '<em>optional</em>'}</span>
          <input type="email" name="email" autocomplete="off" placeholder="name@beispiel.de" value="${esc(form.email)}">
        </label>
        <div class="field">
          <span>Sprache</span>
          <div class="seg small" role="radiogroup" aria-label="Sprache der Mails">
            <button type="button" role="radio" aria-checked="${form.language === 'de'}" class="${form.language === 'de' ? 'on' : ''}" data-language="de">Deutsch</button>
            <button type="button" role="radio" aria-checked="${form.language === 'en'}" class="${form.language === 'en' ? 'on' : ''}" data-language="en">English</button>
          </div>
        </div>
      </div>
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
        <input type="text" name="price" inputmode="decimal" placeholder="${trial ? 'kostenlos' : '49,00 €'}" value="${esc(form.price)}">
      </label>
      <label class="field">
        <span>Notiz <em>optional</em></span>
        <input type="text" name="note" placeholder="z. B. Rechnung 2026-014" value="${esc(form.note)}">
      </label>
      ${trial ? '' : `<div class="field wide"><label class="check"><input type="checkbox" name="paid" ${form.paid ? 'checked' : ''}> Ist bezahlt</label></div>`}
    </div>
    <div class="actions" style="margin-top:16px">
      <button class="btn primary" data-issue>${icon('key')} ${trial ? 'Testcode erzeugen' : 'Code erzeugen'}</button>
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
  const validity = r.license.end
    ? `<span class="badge accent">${icon('hourglass')} ${r.license.trial ? 'Test' : 'gültig'} bis ${esc(fmtDate(r.license.end))}</span>`
    : '<span class="badge">unbegrenzt</span>';

  return `
    <div class="code-out">
      <div class="code-meta">
        <span class="badge ok">${icon('check')} geprüft</span>
        ${fit}
        ${validity}
        <span class="faint">${esc(product?.name ?? r.license.product)} · ${esc(r.license.machineId)}${r.customer ? ` · ${esc(r.customer.name)}` : ''}</span>
      </div>
      <div class="code-text">${esc(r.license.code)}</div>
      <div class="actions">
        <button class="btn" data-copy-code>${icon('copy')} Code kopieren</button>
        <button class="btn primary" data-mail-code>${icon('send')} ${r.customer?.email ? 'Per Mail senden …' : 'Text für den Kunden …'}</button>
        ${r.customer ? `<a class="btn ghost" href="#/kunden/${esc(r.customer.id)}">${icon('user')} Zum Kunden</a>` : ''}
      </div>
    </div>`;
}

function customerMatch() {
  const name = form.customerName.trim().toLowerCase();
  return name ? register.customers.find((c) => c.name.toLowerCase() === name) : null;
}

function customerHint(match) {
  if (!form.customerName.trim()) return 'Leer lassen für einen Code ohne Kunden (z. B. zum Testen).';
  if (match) return `Bestehender Kunde – Mails auf ${match.language === 'en' ? 'Englisch' : 'Deutsch'}.`;
  return 'Wird als neuer Kunde angelegt.';
}

function input(e, main) {
  const el = e.target;
  if (!el.name || !(el.name in form)) return;
  form[el.name] = el.type === 'checkbox' ? el.checked : el.value;

  if (el.name === 'customerName') {
    const match = customerMatch();
    form.customerId = match?.id ?? '';
    if (match) {
      form.language = match.language;
      if (!form.email) {
        form.email = match.email;
        main.querySelector('[name=email]').value = match.email;
      }
    }
    main.querySelector('#customer-hint').textContent = customerHint(match);
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
  if (product && !product.disabled) return pick(main, { product: product.dataset.product });
  const kind = t.closest('[data-kind]');
  if (kind) return pick(main, { kind: kind.dataset.kind });
  const days = t.closest('[data-days]');
  if (days) return pick(main, { end: daysFromNow(Number(days.dataset.days)) });
  const language = t.closest('[data-language]');
  if (language) return pick(main, { language: language.dataset.language });

  if (t.closest('[data-this-pc]')) {
    const { machineId } = await api('/api/this-machine');
    form.machineId = machineId;
    const field = main.querySelector('[name=machineId]');
    field.value = machineId;
    field.classList.remove('invalid');
    main.querySelector('#machine-error').textContent = '';
    return;
  }

  if (t.closest('[data-issue]')) return issue(t.closest('[data-issue]'), main);

  const r = form.result;
  if (t.closest('[data-copy-code]')) return copy(r.license.code, 'Code');
  if (t.closest('[data-mail-code]')) return mailDialog('code', r.license.id);

  const copyRow = t.closest('[data-copy-license]');
  if (copyRow) copy(copyRow.dataset.copyLicense, 'Code');
}

/** Changes a choice and redraws the form - the result below stays until the next code. */
function pick(main, change) {
  Object.assign(form, change);
  if ('product' in change) form.result = null;
  main.querySelector('#code-card').innerHTML = codeCard(getOverview(), getOverview().projects.filter((p) => p.kind === 'plugin'));
}

async function issue(button, main) {
  const check = checkMachineId(form.machineId);
  if (!check.ok) {
    main.querySelector('#machine-error').textContent = check.error || 'Füg die Geräte-ID aus dem Aktivierungsfenster ein.';
    main.querySelector('[name=machineId]').focus();
    return;
  }
  const trial = form.kind === 'trial';
  if (trial && !form.end) {
    toast('Wähl, bis wann der Testcode gilt.', 'bad');
    return;
  }

  const result = await busy(button, () => post('/api/licenses', {
    product: form.product,
    machineId: check.value,
    customerId: form.customerId || null,
    customerName: form.customerId ? null : form.customerName.trim() || null,
    customerEmail: form.email.trim() || null,
    language: form.language,
    trial,
    end: trial ? form.end : null,
    price: form.price.trim() || null,
    paid: trial ? false : form.paid,
    note: form.note.trim() || null,
  }));
  if (!result) return;

  // The next code starts from a clean form - product and kind stay
  Object.assign(form, {
    result, customerId: '', customerName: '', email: '', machineId: '', price: '', paid: false, note: '',
    language: shared.defaultLanguage, end: daysFromNow(shared.trialDays),
  });
  toast(result.license.end ? `Testcode bis ${fmtDate(result.license.end)} erzeugt` : 'Code erzeugt und im Register gespeichert');
  // Draw the whole page again: the new code belongs in "Zuletzt ausgestellt" too
  changed();
}

const daysFromNow = (n) => isoDate(new Date(Date.now() + n * 86_400_000));

// ── Recent codes ───────────────────────────────────────────────────────────

function recentTable(o) {
  const recent = [...register.licenses].sort((a, b) => b.license.issued.localeCompare(a.license.issued)).slice(0, 6);
  if (!recent.length) return '<div class="empty">Noch keine Codes ausgestellt.</div>';

  const nameOf = (id) => register.customers.find((c) => c.id === id)?.name;
  const productOf = (id) => o.projects.find((p) => p.id === id)?.name ?? id;
  return `
    <div class="table-wrap"><table class="table">
      <thead><tr><th>Kunde</th><th>Produkt</th><th>Geräte-ID</th><th>Ausgestellt</th><th>Stand</th><th class="shrink"></th></tr></thead>
      <tbody>${recent.map(({ license: l, status }) => `
        <tr>
          <td>${l.customerId ? `<a href="#/kunden/${esc(l.customerId)}">${esc(nameOf(l.customerId) ?? '–')}</a>` : '<span class="muted">ohne Kunde</span>'}</td>
          <td class="nowrap">${esc(productOf(l.product))}${l.trial ? ' <span class="badge">Test</span>' : ''}</td>
          <td class="mono">${esc(l.machineId)}</td>
          <td class="nowrap">${esc(fmtWhen(l.issued))}</td>
          <td class="nowrap">${statusBadge(status)}</td>
          <td class="shrink"><button class="btn small ghost icon-only" data-copy-license="${esc(l.code)}" title="Code kopieren" aria-label="Code kopieren">${icon('copy')}</button></td>
        </tr>`).join('')}
      </tbody>
    </table></div>`;
}

// ── Plug-ins ───────────────────────────────────────────────────────────────

function pluginRow(p) {
  const folder = p.links.find((l) => l.kind === 'select');
  return `
    <div class="rel">
      <div class="picon">${icon(p.icon)}</div>
      <div>
        <div class="t">${esc(p.name)} <span class="badge">${p.released ? `Release ${esc(p.released)}` : 'noch kein Release'}</span> ${buildBadge(p.build)}</div>
        <div class="faint">${buildLine(p.build)}</div>
      </div>
      <div class="actions">
        <button class="btn small" data-build="${esc(p.id)}">${icon('hammer')} Testbuild</button>
        ${folder ? `<button class="btn small" data-open="${esc(folder.id)}">${icon('folder')} Ordner</button>` : ''}
      </div>
    </div>`;
}
