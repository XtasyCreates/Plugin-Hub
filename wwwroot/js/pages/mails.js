// Mails: code requests that came in - answered with a code in a few clicks - the follow-ups
// that are due, and everything sent so far. Nothing goes out without a click here.

import { api, post } from '../api.js';
import { changed, loadOverview } from '../store.js';
import { isoDate, mailDialog, sharedClick } from '../parts.js';
import { busy, confirmDialog, dialog, esc, fmtDate, fmtDateTime, fmtWhen, icon, notice, plural, toast } from '../ui.js';

const KIND = { code: 'Code', followup: 'Nachfrage', extension: 'Neuer Code', test: 'Test' };

export async function render(main) {
  const [o, mails, reg, inbox, settings] = await Promise.all([
    loadOverview(), api('/api/mails'), api('/api/register'), api('/api/inbox'), api('/api/settings')]);
  const nameOf = (id) => reg.customers.find((c) => c.id === id)?.name ?? '–';
  const findRequest = (id) => inbox.requests.find((v) => v.request.id === id);

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
          <a class="btn small" href="#/einstellungen?feld=mail">Einrichten</a>
        </div>`}

      ${requestsCard(o, inbox)}

      <div class="card">
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

    const check = t.closest('[data-inbox-check]');
    if (check) {
      const r = await busy(check, () => post('/api/inbox/check'));
      if (r) toast(r.added ? plural(r.added, 'neue Anfrage', 'neue Anfragen') : 'Keine neuen Anfragen');
      changed();
      return;
    }
    const answer = t.closest('[data-answer]');
    if (answer) return answerRequest(o, settings, findRequest(answer.dataset.answer), answer.dataset.kind);
    const read = t.closest('[data-read]');
    if (read) return readRequest(inbox, findRequest(read.dataset.read));
    const status = t.closest('[data-request-status]');
    if (status) {
      const [id, to] = status.dataset.requestStatus.split(':');
      if (await busy(status, () => post(`/api/inbox/${id}/status`, { status: to }))) {
        toast(to === 'neu' ? 'Wieder offen' : 'Als erledigt abgelegt');
      }
      changed();
      return;
    }

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

// ── Code requests ──────────────────────────────────────────────────────────

const WANTS = {
  trial: ['accent', 'möchte eine Testphase'],
  full: ['ok', 'möchte kaufen'],
  none: ['', 'Test oder Kauf offen'],
};

function requestsCard(o, inbox) {
  const open = inbox.requests.filter((v) => v.request.status === 'neu');
  const done = inbox.requests.filter((v) => v.request.status !== 'neu');
  const product = (id) => o.projects.find((p) => p.id === id);

  const row = (v) => {
    const r = v.request;
    const p = product(r.product);
    const [wantsClass, wantsText] = WANTS[r.wants ?? 'none'];
    const codes = v.codes.map((c) => `${esc(c.product)} (${esc(c.label)})`).join(', ');
    return `
      <div class="req">
        <div class="picon">${icon(p?.icon ?? 'key')}</div>
        <div class="req-body">
          <div class="t">${esc(r.fromName || r.fromEmail)} ${r.fromName ? `<span class="faint">${esc(r.fromEmail)}</span>` : ''}</div>
          <div class="req-facts">
            <span class="badge">${esc(p?.name ?? 'Plugin unklar')}</span>
            <span class="badge ${wantsClass}">${wantsText}</span>
            <span class="mono">${esc(r.machineId)}</span>
            ${r.version ? `<span class="faint">Version ${esc(r.version)}</span>` : ''}
            <span class="faint">${esc(fmtWhen(r.received))}</span>
          </div>
          ${v.customerId || codes ? `<div class="faint">${v.customerId ? `Bekannter Kunde: <a href="#/kunden/${esc(v.customerId)}">${esc(v.customerName)}</a>` : 'Neuer Kunde'}${codes ? ` · dieses Gerät hat schon: ${codes}` : ''}</div>` : ''}
          <div class="actions req-actions">
            <button class="btn small ${r.wants !== 'full' ? 'primary' : ''}" data-answer="${esc(r.id)}" data-kind="trial">${icon('hourglass')} Testcode senden …</button>
            <button class="btn small ${r.wants === 'full' ? 'primary' : ''}" data-answer="${esc(r.id)}" data-kind="full">${icon('key')} Kauf-Code senden …</button>
            <button class="btn small ghost" data-read="${esc(r.id)}">${icon('mail')} Mail lesen</button>
            <button class="btn small ghost" data-request-status="${esc(r.id)}:ignoriert">Ohne Code ablegen</button>
          </div>
        </div>
      </div>`;
  };

  const doneRow = (v) => {
    const r = v.request;
    const what = r.status === 'erledigt' && r.licenseId ? 'Code erzeugt' : 'ohne Code abgelegt';
    return `
      <div class="req done">
        <div class="req-body">
          <div class="t">${esc(r.fromName || r.fromEmail)} <span class="faint">· ${esc(product(r.product)?.name ?? 'Plugin unklar')} · ${esc(fmtDate(r.received))}</span></div>
          <div class="faint">${what}${r.handledAt ? ` am ${esc(fmtDate(r.handledAt))}` : ''}${v.customerId ? ` · <a href="#/kunden/${esc(v.customerId)}">${esc(v.customerName)}</a>` : ''}</div>
        </div>
        <div class="actions">
          <button class="btn small ghost" data-read="${esc(r.id)}">${icon('mail')} Mail lesen</button>
          <button class="btn small ghost" data-request-status="${esc(r.id)}:neu">Wieder öffnen</button>
        </div>
      </div>`;
  };

  const checked = inbox.lastCheck
    ? `geprüft ${esc(fmtWhen(inbox.lastCheck))} · alle ${inbox.intervalMinutes} Minuten, solange der Hub läuft`
    : 'noch nicht geprüft';

  return `
    <div class="card" data-place="anfragen" style="margin-top:16px">
      <div class="card-head">
        <h3>${icon('key')} Code-Anfragen ${open.length ? `<span class="badge accent">${open.length} neu</span>` : ''}</h3>
        <div class="actions">
          <span class="faint">${checked}</span>
          ${inbox.ready ? `<button class="btn small" data-inbox-check>${icon('refresh')} Jetzt prüfen</button>` : ''}
        </div>
      </div>
      <p class="card-note">Mails mit Geräte-ID an ${esc(inbox.account || 'dein Mailkonto')} – etwa aus „Request a code“ im Aktivierungsfenster.
        Der Hub liest das Postfach nur; in Gmail wird nichts markiert, verschoben oder gelöscht.</p>
      ${inbox.ready ? '' : `
        <div class="issue">${icon('info')}
          <div><div class="t">Noch kein Postfach</div>
          <div class="d">Der Hub liest die Anfragen mit dem Mailkonto aus den Einstellungen – demselben, mit dem er sendet.</div></div>
          <a class="btn small" href="#/einstellungen?feld=mail">Einrichten</a>
        </div>`}
      ${inbox.error ? `
        <div class="issue problem">${icon('alert')}
          <div><div class="t">Das Postfach ließ sich nicht lesen</div><div class="d">${esc(inbox.error)}</div></div>
          <a class="btn small" href="#/einstellungen?feld=mail">Zum E-Mail-Konto</a>
        </div>` : ''}
      ${open.length ? `<div class="reqs">${open.map(row).join('')}</div>`
        : inbox.ready ? '<div class="empty">Keine offenen Anfragen.</div>' : ''}
      ${done.length ? `
        <details class="reqs-done">
          <summary>Erledigt <span class="count">${done.length}</span></summary>
          <div class="reqs">${done.map(doneRow).join('')}</div>
        </details>` : ''}
    </div>`;
}

/**
 * A code for a request: what the mail said is filled in, and the customer is the one from the
 * register when the address or the device is known. Then the mail with the code, as an answer
 * to the request.
 */
async function answerRequest(o, settings, view, kind) {
  if (!view) return;
  const r = view.request;
  const trial = kind === 'trial';
  const plugins = o.projects.filter((p) => p.kind === 'plugin');
  const language = view.customerLanguage ?? settings.shared.defaultLanguage;
  const inDays = (n) => isoDate(new Date(Date.now() + n * 86_400_000));

  const result = await dialog({
    title: trial ? 'Testcode für diese Anfrage' : 'Kauf-Code für diese Anfrage',
    wide: true,
    confirm: 'Code erzeugen',
    body: `
      <p>${esc(r.fromName || r.fromEmail)} ${r.fromName ? `&lt;${esc(r.fromEmail)}&gt;` : ''} · ${esc(fmtDateTime(r.received))}</p>
      <div class="form-grid">
        <label class="field"><span>Plugin</span>
          <select name="product">${plugins.map((p) => `<option value="${esc(p.id)}" ${p.id === r.product ? 'selected' : ''}>${esc(p.name)}</option>`).join('')}</select></label>
        <label class="field"><span>Geräte-ID</span><input type="text" name="machineId" class="mono" value="${esc(r.machineId)}"></label>
        <label class="field"><span>Kunde ${view.customerId ? '<em>aus dem Register</em>' : '<em>wird angelegt</em>'}</span>
          <input type="text" name="customerName" value="${esc(view.customerName ?? r.fromName)}" ${view.customerId ? 'readonly' : ''}></label>
        <label class="field"><span>E-Mail</span><input type="email" name="email" value="${esc(r.fromEmail)}" ${view.customerId ? 'readonly' : ''}></label>
        ${trial
          ? `<label class="field"><span>Gültig bis <em>einschließlich</em></span><input type="date" name="end" value="${inDays(settings.shared.trialDays)}" min="${isoDate(new Date())}"></label>`
          : '<label class="field"><span>Preis <em>optional</em></span><input type="text" name="price" inputmode="decimal" placeholder="49,00 €"></label>'}
        <label class="field"><span>Sprache der Antwort</span>
          <select name="language"><option value="de" ${language === 'de' ? 'selected' : ''}>Deutsch</option><option value="en" ${language === 'en' ? 'selected' : ''}>English</option></select></label>
      </div>
      ${trial ? '' : '<label class="check"><input type="checkbox" name="paid"> Ist bezahlt</label>'}
      ${view.codes.length ? `<p class="faint">Dieses Gerät hat schon: ${view.codes.map((c) => `${esc(c.product)} (${esc(c.label)})`).join(', ')}</p>` : ''}`,
    submit: (v) => {
      if (!view.customerId && !v.customerName.trim() && !v.email.trim()) {
        throw new Error('Trag einen Namen oder eine E-Mail-Adresse für den Kunden ein.');
      }
      return post('/api/licenses', {
        product: v.product,
        machineId: v.machineId,
        customerId: view.customerId,
        customerName: view.customerId ? null : (v.customerName.trim() || v.email.trim()),
        customerEmail: view.customerId ? null : v.email.trim() || null,
        language: v.language,
        trial,
        end: trial ? v.end : null,
        price: trial ? null : v.price || null,
        paid: !trial && Boolean(v.paid),
        note: `Aus der Anfrage vom ${fmtDate(r.received)}`,
      });
    },
  });
  if (!result) return;

  await post(`/api/inbox/${r.id}/status`, { status: 'erledigt', licenseId: result.license.id }).catch((e) => toast(e.message, 'bad'));
  toast(trial ? `Testcode bis ${fmtDate(result.license.end)} erzeugt` : 'Kauf-Code erzeugt');
  changed();
  // The answer goes back into the customer's conversation
  if (result.customer?.email) await mailDialog('code', result.license.id, null, { inReplyTo: r.messageId });
}

const GMAIL = /@(gmail|googlemail)\.com$/i;

/** The request as it came in - and the way to it in Gmail. */
async function readRequest(inbox, view) {
  if (!view) return;
  const r = view.request;
  const id = r.messageId.replace(/^<|>$/g, '');
  const gmail = GMAIL.test(inbox.account) && !r.messageId.startsWith('sha256:')
    ? `https://mail.google.com/mail/?authuser=${encodeURIComponent(inbox.account)}#search/${encodeURIComponent(`rfc822msgid:${id}`)}`
    : null;
  await notice(r.subject || '(ohne Betreff)', `
    <p>${esc(r.fromName || '')} &lt;${esc(r.fromEmail)}&gt; · ${esc(fmtDateTime(r.received))}</p>
    <pre class="mail-text">${esc(r.text)}</pre>
    ${gmail ? `<p><a href="${gmail}" target="_blank" rel="noopener">In Gmail öffnen</a></p>` : ''}`);
}
