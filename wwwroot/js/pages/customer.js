// One customer: details, every code they got and where it stands, extending a code for the
// same device, the mails that went out. "#/kunden/ohne" lists the codes made without a customer.

import { api, del, put } from '../api.js';
import { changed, loadOverview, productSettings } from '../store.js';
import { extendDialog, mailDialog, sharedClick, statusBadge } from '../parts.js';
import { confirmDialog, copy, dialog, esc, fmtDate, fmtDateTime, fmtPrice, icon, toast } from '../ui.js';
import { customerFields } from './customers.js';

const MAIL_KIND = { code: 'Code', followup: 'Nachfrage', extension: 'Neuer Code', test: 'Test' };

export async function render(main, { args }) {
  const [id] = args;
  const [o, reg, settings] = await Promise.all([loadOverview(), api('/api/register'), api('/api/settings')]);
  const customer = id === 'ohne' ? null : reg.customers.find((c) => c.id === id);

  if (id !== 'ohne' && !customer) {
    main.innerHTML = `<div class="page"><a class="back" href="#/kunden">${icon('arrowLeft')} Kunden</a>
      <div class="empty">Diesen Kunden gibt es nicht (mehr).</div></div>`;
    return;
  }

  const views = reg.licenses
    .filter((v) => (customer ? v.license.customerId === customer.id : !v.license.customerId))
    .sort((a, b) => b.license.issued.localeCompare(a.license.issued));
  const mails = customer ? reg.mails.filter((m) => m.customerId === customer.id).sort((a, b) => b.sent.localeCompare(a.sent)) : [];
  const productName = (pid) => o.projects.find((p) => p.id === pid)?.name ?? pid;
  const hasMail = Boolean(customer?.email);

  main.innerHTML = `
    <div class="page">
      <a class="back" href="#/kunden">${icon('arrowLeft')} Kunden</a>
      <header class="page-head">
        <div>
          <h1>${esc(customer?.name ?? 'Codes ohne Kunde')}</h1>
          ${customer ? `<p class="sub">${customer.email ? `<a href="mailto:${esc(customer.email)}">${esc(customer.email)}</a> · ` : 'keine E-Mail · '}
            Mails auf ${customer.language === 'en' ? 'Englisch' : 'Deutsch'} · ${customer.mailConsent ? 'Nachfragen erlaubt' : 'keine Nachfragen'} ·
            im Register seit ${esc(fmtDate(customer.created))}</p>`
          : '<p class="sub">Codes, die ohne Kundennamen erzeugt wurden – zum Beispiel für eigene Tests.</p>'}
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
        ${views.length ? `
          <div class="table-wrap"><table class="table">
            <thead><tr><th>Produkt</th><th>Stand</th><th>Geräte-ID</th><th>Ausgestellt</th><th class="num">Preis</th><th>Bezahlt</th><th class="shrink"></th></tr></thead>
            <tbody>${views.map(({ license: l, status }) => `
              <tr>
                <td>${esc(productName(l.product))}${l.trial ? ' <span class="badge">Test</span>' : ''}</td>
                <td class="nowrap">${statusBadge(status)}</td>
                <td class="mono">${esc(l.machineId)}</td>
                <td class="nowrap" title="${esc(fmtDateTime(l.issued))}${l.note ? ` · ${esc(l.note)}` : ''}">${esc(fmtDate(l.issued))}</td>
                <td class="num">${esc(fmtPrice(l.price)) || '<span class="muted">–</span>'}</td>
                <td><label class="check" title="${l.paid ? 'bezahlt' : 'noch nicht bezahlt'}"><input type="checkbox" data-paid="${esc(l.id)}" ${l.paid ? 'checked' : ''} aria-label="Bezahlt"></label></td>
                <td class="shrink">
                  <div class="actions">
                    ${status.state !== 'replaced' && status.state !== 'oldkey'
                      ? `<button class="btn small" data-extend="${esc(l.id)}" title="Neuer Code für dieselbe Geräte-ID">${icon('calendar')} Verlängern</button>` : ''}
                    <button class="btn small ghost icon-only" data-mail="${esc(l.id)}" title="${hasMail ? 'Mail senden' : 'Text für den Kunden'}" aria-label="Mail senden">${icon('send')}</button>
                    <button class="btn small ghost icon-only" data-copy="${esc(l.id)}" title="Code kopieren" aria-label="Code kopieren">${icon('copy')}</button>
                    <button class="btn small ghost icon-only" data-edit-license="${esc(l.id)}" title="Preis und Notiz" aria-label="Bearbeiten">${icon('pencil')}</button>
                    <button class="btn small ghost icon-only" data-delete-license="${esc(l.id)}" title="Aus dem Register löschen" aria-label="Löschen">${icon('trash')}</button>
                  </div>
                </td>
              </tr>`).join('')}
            </tbody>
          </table></div>`
        : '<div class="empty">Noch keine Codes.</div>'}
      </div>

      ${customer ? `
        <div class="card">
          <div class="card-head"><h3>${icon('mail')} Mails</h3></div>
          ${mails.length ? `<ul class="log">${mails.map((m) => `
            <li><span class="faint">${esc(fmtDate(m.sent))}</span><span>${esc(m.subject)}</span><span class="badge">${esc(MAIL_KIND[m.kind] ?? m.kind)}</span></li>`).join('')}</ul>`
          : '<div class="empty">Noch keine Mails an diesen Kunden.</div>'}
        </div>

        <div class="card">
          <div class="card-head"><h3>${icon('trash')} Kunde löschen</h3></div>
          <div class="actions">
            <p class="muted" style="flex:1;min-width:240px">Entfernt ${esc(customer.name)} aus dem Register${views.length ? ' – samt der Codes. Die Codes funktionieren beim Kunden trotzdem weiter.' : '.'}</p>
            <button class="btn danger" data-delete>${icon('trash')} Löschen</button>
          </div>
        </div>` : ''}
    </div>`;

  const byId = (lid) => views.find((v) => v.license.id === lid);

  main.oninput = null;
  main.onchange = async (e) => {
    const box = e.target.closest('[data-paid]');
    if (!box) return;
    const l = byId(box.dataset.paid).license;
    try {
      await put(`/api/licenses/${l.id}`, { price: l.price == null ? null : String(l.price), paid: box.checked, note: l.note });
      toast(box.checked ? 'Als bezahlt markiert' : 'Als offen markiert');
    } catch (ex) {
      toast(ex.message, 'bad');
    }
    changed();
  };

  main.onclick = async (e) => {
    if (await sharedClick(e)) return;
    const t = e.target;

    const copyButton = t.closest('[data-copy]');
    if (copyButton) return copy(byId(copyButton.dataset.copy).license.code, 'Code');

    const extend = t.closest('[data-extend]');
    if (extend) {
      const view = byId(extend.dataset.extend);
      return extendDialog(view, productSettings(settings.shared, view.license.product).trialDays);
    }

    const mail = t.closest('[data-mail]');
    if (mail) return chooseMail(byId(mail.dataset.mail));

    if (t.closest('[data-edit]')) {
      const saved = await dialog({
        title: 'Kunde bearbeiten',
        confirm: 'Speichern',
        body: customerFields(customer),
        submit: (v) => put(`/api/customers/${customer.id}`, v),
      });
      if (saved) changed();
      return;
    }

    const edit = t.closest('[data-edit-license]');
    if (edit) {
      const l = byId(edit.dataset.editLicense).license;
      const saved = await dialog({
        title: 'Code bearbeiten',
        confirm: 'Speichern',
        body: `
          <label class="field"><span>Preis <em>optional</em></span><input type="text" name="price" inputmode="decimal" value="${esc(l.price == null ? '' : String(l.price).replace('.', ','))}"></label>
          <label class="check"><input type="checkbox" name="paid" ${l.paid ? 'checked' : ''}> Ist bezahlt</label>
          <label class="field"><span>Notiz <em>optional</em></span><input type="text" name="note" value="${esc(l.note)}"></label>`,
        submit: (v) => put(`/api/licenses/${l.id}`, { price: v.price || null, paid: v.paid, note: v.note }),
      });
      if (saved) changed();
      return;
    }

    const remove = t.closest('[data-delete-license]');
    if (remove) {
      const l = byId(remove.dataset.deleteLicense).license;
      const ok = await confirmDialog('Code aus dem Register löschen?',
        `Der Eintrag für ${esc(l.machineId)} verschwindet aus dem Register. Der Code selbst bleibt beim Kunden gültig – zurückziehen lässt er sich offline nicht.`,
        { confirm: 'Löschen', danger: true });
      if (!ok) return;
      await del(`/api/licenses/${l.id}`).then(() => toast('Gelöscht')).catch((ex) => toast(ex.message, 'bad'));
      changed();
      return;
    }

    if (t.closest('[data-delete]')) {
      const ok = await confirmDialog(`${customer.name} löschen?`,
        views.length
          ? `Mit ${views.length === 1 ? 'seinem Code' : `seinen ${views.length} Codes`}. Das lässt sich nur über ein Backup rückgängig machen.`
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

/** Which mail about this code: the code itself, the trial follow-up, or a new code. */
async function chooseMail(view) {
  const l = view.license;
  const kinds = [['code', 'Der Code mit Anleitung']];
  if (l.trial && l.end) kinds.push(['followup', 'Nachfrage: Hat dir die Testphase gefallen?']);
  if (l.replaces) kinds.push(['extension', 'Neuer Code (Verlängerung)']);
  if (kinds.length === 1) return mailDialog('code', l.id);

  const kind = await dialog({
    title: 'Welche Mail?',
    confirm: 'Weiter',
    body: kinds.map(([k, label], i) => `<label class="check"><input type="radio" name="kind" value="${k}" ${i === 0 ? 'checked' : ''}> ${esc(label)}</label>`).join(''),
    submit: (v) => v.kind,
  });
  if (kind) await mailDialog(kind, l.id);
}
