// One customer: details, and every code they got - paid or not, still valid or not.
// "#/kunden/ohne" lists the codes made without a customer.

import { api, del, put } from '../api.js';
import { keyOf, loadOverview } from '../store.js';
import { sharedClick } from '../parts.js';
import { confirmDialog, copy, dialog, esc, fmtDateTime, fmtPrice, icon, toast } from '../ui.js';

export async function render(main, { args }) {
  const [id] = args;
  const [o, reg] = await Promise.all([loadOverview(), api('/api/register')]);
  const customer = id === 'ohne' ? null : reg.customers.find((c) => c.id === id);

  if (id !== 'ohne' && !customer) {
    main.innerHTML = `<div class="page"><a class="back" href="#/kunden">${icon('arrowLeft')} Kunden</a>
      <div class="empty">Diesen Kunden gibt es nicht (mehr).</div></div>`;
    return;
  }

  const licenses = reg.licenses
    .filter((l) => (customer ? l.customerId === customer.id : !l.customerId))
    .sort((a, b) => b.issued.localeCompare(a.issued));
  const productName = (pid) => o.projects.find((p) => p.id === pid)?.name ?? pid;
  // A code dies with the key that signed it
  const valid = (l) => keyOf(o, l.product)?.fingerprint === l.keyFingerprint;

  main.innerHTML = `
    <div class="page">
      <a class="back" href="#/kunden">${icon('arrowLeft')} Kunden</a>
      <header class="page-head">
        <div>
          <h1>${esc(customer?.name ?? 'Codes ohne Kunde')}</h1>
          ${customer ? `<p class="sub">${customer.email ? `<a href="mailto:${esc(customer.email)}">${esc(customer.email)}</a> · ` : ''}im Register seit ${esc(fmtDateTime(customer.created))}</p>` : '<p class="sub">Codes, die ohne Kundennamen erzeugt wurden – zum Beispiel für eigene Tests.</p>'}
        </div>
        ${customer ? `
          <div class="actions">
            <button class="btn" data-edit>${icon('pencil')} Bearbeiten</button>
            <a class="btn primary" href="#/verkauf?kunde=${esc(customer.id)}">${icon('key')} Neuer Code</a>
          </div>` : ''}
      </header>

      ${customer?.note ? `<div class="card"><p>${esc(customer.note)}</p></div>` : ''}

      <div class="card">
        <div class="card-head"><h3>${icon('key')} Codes</h3></div>
        ${licenses.length ? `
          <div class="table-wrap"><table class="table">
            <thead><tr><th>Produkt</th><th>Geräte-ID</th><th>Ausgestellt</th><th class="num">Preis</th><th>Bezahlt</th><th>Notiz</th><th class="shrink"></th></tr></thead>
            <tbody>${licenses.map((l) => `
              <tr>
                <td class="nowrap">${esc(productName(l.product))} ${valid(l) ? '' : '<span class="badge bad" title="Mit einem Schlüssel erzeugt, den es nicht mehr gibt">alter Schlüssel</span>'}</td>
                <td class="mono">${esc(l.machineId)}</td>
                <td class="nowrap">${esc(fmtDateTime(l.issued))}</td>
                <td class="num">${esc(fmtPrice(l.price)) || '<span class="muted">–</span>'}</td>
                <td><label class="check"><input type="checkbox" data-paid="${esc(l.id)}" ${l.paid ? 'checked' : ''}> ${l.paid ? 'ja' : 'nein'}</label></td>
                <td class="muted">${esc(l.note || '')}</td>
                <td class="shrink">
                  <div class="actions">
                    <button class="btn small ghost icon-only" data-copy="${esc(l.id)}" title="Code kopieren" aria-label="Code kopieren">${icon('copy')}</button>
                    <button class="btn small ghost icon-only" data-edit-license="${esc(l.id)}" aria-label="Bearbeiten">${icon('pencil')}</button>
                    <button class="btn small ghost icon-only" data-delete-license="${esc(l.id)}" aria-label="Löschen">${icon('trash')}</button>
                  </div>
                </td>
              </tr>`).join('')}
            </tbody>
          </table></div>`
        : '<div class="empty">Noch keine Codes.</div>'}
      </div>

      ${customer ? `
        <div class="card">
          <div class="card-head"><h3>${icon('trash')} Kunde löschen</h3></div>
          <div class="actions">
            <p class="muted" style="flex:1;min-width:240px">Entfernt ${esc(customer.name)} aus dem Register${licenses.length ? ' – samt der Codes. Die Codes funktionieren beim Kunden trotzdem weiter.' : '.'}</p>
            <button class="btn danger" data-delete>${icon('trash')} Löschen</button>
          </div>
        </div>` : ''}
    </div>`;

  const byId = (lid) => licenses.find((l) => l.id === lid);

  main.oninput = null;
  main.onchange = async (e) => {
    const box = e.target.closest('[data-paid]');
    if (!box) return;
    const l = byId(box.dataset.paid);
    try {
      await put(`/api/licenses/${l.id}`, { price: l.price == null ? null : String(l.price), paid: box.checked, note: l.note });
      toast(box.checked ? 'Als bezahlt markiert' : 'Als offen markiert');
    } catch (ex) {
      toast(ex.message, 'bad');
    }
    render(main, { args });
  };

  main.onclick = async (e) => {
    if (await sharedClick(e)) return;
    const t = e.target;

    const copyButton = t.closest('[data-copy]');
    if (copyButton) return copy(byId(copyButton.dataset.copy).code, 'Code');

    if (t.closest('[data-edit]')) {
      const saved = await dialog({
        title: 'Kunde bearbeiten',
        confirm: 'Speichern',
        body: `
          <label class="field"><span>Name</span><input type="text" name="name" value="${esc(customer.name)}"></label>
          <label class="field"><span>E-Mail <em>optional</em></span><input type="email" name="email" value="${esc(customer.email)}"></label>
          <label class="field"><span>Notiz <em>optional</em></span><textarea name="note" rows="3">${esc(customer.note)}</textarea></label>`,
        submit: (v) => put(`/api/customers/${customer.id}`, v),
      });
      if (saved) render(main, { args });
      return;
    }

    const edit = t.closest('[data-edit-license]');
    if (edit) {
      const l = byId(edit.dataset.editLicense);
      const saved = await dialog({
        title: 'Code bearbeiten',
        confirm: 'Speichern',
        body: `
          <label class="field"><span>Preis <em>optional</em></span><input type="text" name="price" inputmode="decimal" value="${esc(l.price == null ? '' : String(l.price).replace('.', ','))}"></label>
          <label class="check"><input type="checkbox" name="paid" ${l.paid ? 'checked' : ''}> Ist bezahlt</label>
          <label class="field"><span>Notiz <em>optional</em></span><input type="text" name="note" value="${esc(l.note)}"></label>`,
        submit: (v) => put(`/api/licenses/${l.id}`, { price: v.price || null, paid: v.paid, note: v.note }),
      });
      if (saved) render(main, { args });
      return;
    }

    const remove = t.closest('[data-delete-license]');
    if (remove) {
      const l = byId(remove.dataset.deleteLicense);
      const ok = await confirmDialog('Code aus dem Register löschen?',
        `Der Eintrag für ${esc(l.machineId)} verschwindet aus dem Register. Der Code selbst bleibt beim Kunden gültig – zurückziehen lässt er sich offline nicht.`,
        { confirm: 'Löschen', danger: true });
      if (!ok) return;
      await del(`/api/licenses/${l.id}`).then(() => toast('Gelöscht')).catch((ex) => toast(ex.message, 'bad'));
      render(main, { args });
      return;
    }

    if (t.closest('[data-delete]')) {
      const ok = await confirmDialog(`${customer.name} löschen?`,
        licenses.length
          ? `Mit ${licenses.length === 1 ? 'seinem Code' : `seinen ${licenses.length} Codes`}. Das lässt sich nur über ein Backup rückgängig machen.`
          : 'Das lässt sich nur über ein Backup rückgängig machen.',
        { confirm: 'Löschen', danger: true });
      if (!ok) return;
      try {
        await del(`/api/customers/${customer.id}?licenses=true`);
        toast(`${customer.name} gelöscht`);
        location.hash = '#/kunden';
      } catch (ex) {
        toast(ex.message, 'bad');
      }
    }
  };
}
