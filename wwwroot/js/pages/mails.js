// Mails: the follow-ups that are due - trials close to their end - ready to check and send,
// and everything sent so far. Nothing goes out without a click here.

import { api, post } from '../api.js';
import { changed, loadOverview } from '../store.js';
import { mailDialog, sharedClick } from '../parts.js';
import { busy, confirmDialog, esc, fmtDate, fmtDateTime, icon, plural, toast } from '../ui.js';

const KIND = { code: 'Code', followup: 'Nachfrage', extension: 'Neuer Code', test: 'Test' };

export async function render(main) {
  const [o, mails, reg] = await Promise.all([loadOverview(), api('/api/mails'), api('/api/register')]);
  const nameOf = (id) => reg.customers.find((c) => c.id === id)?.name ?? '–';

  main.innerHTML = `
    <div class="page">
      <header class="page-head">
        <div>
          <h1>Mails</h1>
          <p class="sub">Der Hub schlägt vor, du entscheidest – ohne deinen Klick geht nichts raus.</p>
        </div>
      </header>

      ${mails.ready ? '' : `
        <div class="issue problem">${icon('alert')}
          <div><div class="t">Der Mailversand ist noch nicht eingerichtet</div>
          <div class="d">Trag in den Einstellungen dein Gmail-Konto und ein App-Passwort ein. Bis dahin kannst du die Texte kopieren und selbst verschicken.</div></div>
          <a class="btn small" href="#/einstellungen">Einrichten</a>
        </div>`}

      <div class="card" style="margin-top:16px">
        <div class="card-head">
          <h3>${icon('hourglass')} Fällige Nachfragen</h3>
          ${mails.due.length > 1 && mails.ready ? `<button class="btn primary" data-send-all>${icon('send')} Alle ${mails.due.length} senden …</button>` : ''}
        </div>
        <p class="card-note">Testphasen, die in den nächsten Tagen enden – bei Kunden mit E-Mail-Adresse, die nachgefragt werden dürfen.</p>
        ${mails.due.length ? mails.due.map((d) => `
          <div class="rel">
            <div class="picon">${icon('mail')}</div>
            <div>
              <div class="t">${esc(d.customerName)} <span class="badge">${esc(d.product)}</span>
                <span class="badge ${d.daysLeft < 0 ? 'bad' : 'accent'}">${d.daysLeft < 0 ? `seit ${esc(fmtDate(d.end))} abgelaufen` : d.daysLeft === 0 ? 'endet heute' : `endet in ${plural(d.daysLeft, 'Tag', 'Tagen')}`}</span></div>
              <div class="faint">${esc(d.draft.to)} · „${esc(d.draft.subject)}“</div>
            </div>
            <div class="actions">
              <button class="btn small" data-preview="${esc(d.licenseId)}">${icon('mail')} Ansehen und senden</button>
              <button class="btn small ghost" data-skip="${esc(d.licenseId)}">Überspringen</button>
            </div>
          </div>`).join('')
        : '<div class="empty">Gerade ist keine Nachfrage fällig.</div>'}
      </div>

      <div class="card">
        <div class="card-head"><h3>${icon('send')} Gesendet</h3></div>
        ${mails.sent.length ? `
          <div class="table-wrap"><table class="table">
            <thead><tr><th>Wann</th><th>An</th><th>Betreff</th><th>Art</th></tr></thead>
            <tbody>${mails.sent.slice(0, 50).map((m) => `
              <tr class="${m.customerId ? 'link' : ''}" ${m.customerId ? `data-href="#/kunden/${esc(m.customerId)}"` : ''}>
                <td class="nowrap">${esc(fmtDateTime(m.sent))}</td>
                <td>${esc(m.customerId ? nameOf(m.customerId) : m.to)}</td>
                <td>${esc(m.subject)}</td>
                <td><span class="badge">${esc(KIND[m.kind] ?? m.kind)}</span></td>
              </tr>`).join('')}
            </tbody>
          </table></div>`
        : '<div class="empty">Noch keine Mails gesendet.</div>'}
      </div>
    </div>`;

  main.oninput = null;
  main.onchange = null;
  main.onclick = async (e) => {
    if (await sharedClick(e)) return;
    const t = e.target;

    const preview = t.closest('[data-preview]');
    if (preview) return mailDialog('followup', preview.dataset.preview);

    const skip = t.closest('[data-skip]');
    if (skip) {
      const ok = await confirmDialog('Nachfrage überspringen?', 'Der Hub schlägt sie für diesen Code dann nicht mehr vor.', { confirm: 'Überspringen' });
      if (!ok) return;
      if (await busy(skip, () => post('/api/mails/skip', { kind: 'followup', licenseId: skip.dataset.skip }))) {
        toast('Übersprungen');
      }
      changed();
      return;
    }

    const all = t.closest('[data-send-all]');
    if (all) return sendAll(all, mails.due);

    const tr = t.closest('tr[data-href]');
    if (tr) location.hash = tr.dataset.href;
  };
}

/** Sends every due follow-up as drafted - after one confirmation listing who gets what. */
async function sendAll(button, due) {
  const ok = await confirmDialog(`${due.length} Nachfragen senden?`,
    `An ${due.map((d) => esc(d.customerName)).join(', ')} – jeweils der vorbereitete Text in der Sprache des Kunden. Einzeln prüfen kannst du sie mit „Ansehen und senden“.`,
    { confirm: 'Alle senden' });
  if (!ok) return;

  let sent = 0;
  await busy(button, async () => {
    for (const d of due) {
      await post('/api/mails/send', { kind: 'followup', licenseId: d.licenseId, subject: d.draft.subject, body: d.draft.body });
      sent++;
    }
  });
  toast(`${plural(sent, 'Mail', 'Mails')} gesendet`);
  changed();
}
