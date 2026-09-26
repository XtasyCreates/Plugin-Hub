// Einstellungen: what belongs to the products (Hub\settings.json, in Git), the mail account
// (private, the password never comes back to the page), the mail templates, and phone access
// through Tailscale (private, switched on and off only on the PC).

import { api, post, put } from '../api.js';
import { changed, loadOverview, onPhone } from '../store.js';
import { sharedClick } from '../parts.js';
import { busy, copy, esc, fmtDateTime, icon, toast } from '../ui.js';

const KINDS = [
  { id: 'code', label: 'Code senden' },
  { id: 'followup', label: 'Nachfrage vor Ablauf der Testphase' },
  { id: 'extension', label: 'Neuer Code nach Verlängerung' },
];

export async function render(main) {
  const [o, settings, templates] = await Promise.all([loadOverview(), api('/api/settings'), api('/api/templates/mails')]);
  const s = settings.shared;
  const m = settings.mail;

  main.innerHTML = `
    <div class="page">
      <header class="page-head">
        <div>
          <h1>Einstellungen</h1>
          <p class="sub">Allgemeines liegt in Hub\\settings.json (in Git), das Mailkonto nur auf diesem PC.</p>
        </div>
      </header>

      <form class="card" id="shared">
        <div class="card-head"><h3>${icon('sliders')} Allgemein</h3></div>
        <div class="form-grid">
          <label class="field"><span>Kontakt-Adresse für Kunden</span>
            <input type="email" name="contact" value="${esc(s.contact)}" placeholder="name@beispiel.de">
            <span class="hint">Steht im Plugin („Request a code“), in der Anleitung und als Antwort-Adresse der Mails. Im Plugin wirkt eine Änderung ab dem nächsten Build.</span></label>
          <label class="field"><span>Unterschrift unter Mails</span>
            <input type="text" name="signature" value="${esc(s.signature)}"></label>
          <label class="field"><span>Anbieter (Name oder Firma)</span>
            <input type="text" name="seller" value="${esc(s.seller)}">
            <span class="hint">Steht in den Lizenzbedingungen und im Paket. Pflicht für ein Release.</span></label>
          <label class="field"><span>Anschrift des Anbieters</span>
            <input type="text" name="sellerAddress" value="${esc(s.sellerAddress)}" placeholder="Straße 1, 12345 Stadt, Deutschland"></label>
          <label class="field"><span>Standard-Testdauer <em>Tage</em></span>
            <input type="number" name="trialDays" min="1" max="365" value="${s.trialDays}">
            <span class="hint">Platzhalter, bis du deinen Zeitraum festgelegt hast.</span></label>
          <label class="field"><span>Nachfrage vorschlagen <em>Tage vor Ende der Testphase</em></span>
            <input type="number" name="followUpDays" min="0" max="60" value="${s.followUpDays}"></label>
          <label class="field"><span>„Läuft bald ab“ ab <em>Tage vorher</em></span>
            <input type="number" name="endingSoonDays" min="1" max="60" value="${s.endingSoonDays}"></label>
          <label class="field"><span>Sprache für neue Kunden</span>
            <select name="defaultLanguage">
              <option value="de" ${s.defaultLanguage === 'de' ? 'selected' : ''}>Deutsch</option>
              <option value="en" ${s.defaultLanguage === 'en' ? 'selected' : ''}>English</option>
            </select></label>
        </div>
        <div class="actions" style="margin-top:14px"><button class="btn primary" type="submit">Speichern</button></div>
      </form>

      <form class="card" id="mail">
        <div class="card-head">
          <h3>${icon('mail')} E-Mail</h3>
          <span class="badge ${settings.ready ? 'ok' : 'bad'}">${settings.ready ? 'eingerichtet' : 'nicht eingerichtet'}</span>
        </div>
        <p class="card-note">Der Hub sendet über dein Gmail-Konto. Dafür braucht er ein <em>App-Passwort</em> von Google – dein normales
          Gmail-Passwort funktioniert hier nicht und gehört auch nicht hierher.
          <a href="https://myaccount.google.com/apppasswords" target="_blank" rel="noopener">App-Passwort bei Google erzeugen</a>
          (geht nur, wenn im Google-Konto die Bestätigung in zwei Schritten an ist).</p>
        <div class="form-grid">
          <label class="field"><span>Absender-Adresse <em>deine Gmail-Adresse</em></span>
            <input type="email" name="senderEmail" value="${esc(m.senderEmail)}" placeholder="name@gmail.com"></label>
          <label class="field"><span>App-Passwort ${settings.hasPassword ? '<em>ist gespeichert – nur zum Ändern eintragen</em>' : '<em>16 Zeichen, Leerzeichen sind egal</em>'}</span>
            <input type="password" name="password" autocomplete="new-password" placeholder="${settings.hasPassword ? '••••••••••••••••' : 'abcd efgh ijkl mnop'}"></label>
          <label class="field"><span>Absender-Name <em>so sehen dich die Kunden</em></span>
            <input type="text" name="senderName" value="${esc(m.senderName)}" placeholder="Gustav"></label>
          <div class="form-grid inner">
            <label class="field"><span>Server</span><input type="text" name="host" value="${esc(m.host)}"></label>
            <label class="field"><span>Port</span><input type="number" name="port" value="${m.port}" style="width:90px"></label>
          </div>
        </div>
        <div class="actions" style="margin-top:14px">
          <button class="btn primary" type="submit">Speichern</button>
          <button class="btn" type="button" data-test ${settings.ready ? '' : 'disabled'}>${icon('send')} Test-Mail an mich</button>
          ${settings.ready ? '' : '<span class="faint">Die Test-Mail geht, sobald Adresse und App-Passwort gespeichert sind.</span>'}
        </div>
      </form>

      <form class="card" id="templates">
        <div class="card-head"><h3>${icon('file')} Mail-Vorlagen</h3></div>
        <p class="card-note">Platzhalter: {name} {product} {code} {deviceId} {end} {validity} {daysLeft} {command} {activationCommand} {signature} {contact}</p>
        ${KINDS.map((k) => `
          <div class="template">
            <div class="label">${esc(k.label)}</div>
            <div class="form-grid">
              ${['de', 'en'].map((lang) => `
                <div class="field">
                  <span>${lang === 'de' ? 'Deutsch' : 'English'}</span>
                  <input type="text" name="${k.id}.${lang}.subject" value="${esc(templates[k.id]?.[lang]?.subject ?? '')}">
                  <textarea name="${k.id}.${lang}.body" rows="9">${esc(templates[k.id]?.[lang]?.body ?? '')}</textarea>
                </div>`).join('')}
            </div>
          </div>`).join('')}
        <div class="actions" style="margin-top:14px"><button class="btn primary" type="submit">Vorlagen speichern</button></div>
      </form>

      <div class="card" id="handy">
        <div class="card-head"><h3>${icon('phone')} Handy-Zugang</h3></div>
        <p class="faint">Prüfe Tailscale …</p>
      </div>
    </div>`;

  // Asking Tailscale takes a moment - the rest of the page does not wait for it
  api('/api/phone').then(showPhone).catch((e) => showPhone(null, e.message));

  main.oninput = null;
  main.onchange = null;

  main.querySelector('#shared').onsubmit = (e) => save(e, () => {
    const f = new FormData(e.target);
    return put('/api/settings/shared', {
      contact: f.get('contact'), signature: f.get('signature'), seller: f.get('seller'), sellerAddress: f.get('sellerAddress'),
      trialDays: Number(f.get('trialDays')), followUpDays: Number(f.get('followUpDays')),
      endingSoonDays: Number(f.get('endingSoonDays')), defaultLanguage: f.get('defaultLanguage'),
    });
  }, s.contact !== new FormData(e.target).get('contact')
    ? 'Gespeichert. Die neue Kontakt-Adresse kommt mit dem nächsten Build ins Plugin.' : 'Gespeichert');

  // One "Speichern" for address and password: two buttons meant saving one and forgetting the other
  main.querySelector('#mail').onsubmit = (e) => save(e, async () => {
    const f = new FormData(e.target);
    const saved = await put('/api/settings/mail', {
      senderName: f.get('senderName'), senderEmail: f.get('senderEmail'), host: f.get('host'), port: Number(f.get('port')), userName: '',
    });
    // Empty means "keep the stored one" - the page never gets it back to show
    const password = String(f.get('password') ?? '').replace(/\s+/g, '');
    if (password) await put('/api/settings/mail-password', { password });
    return saved;
  }, 'Gespeichert');

  main.querySelector('#templates').onsubmit = (e) => save(e, () => {
    const f = new FormData(e.target);
    const body = {};
    for (const k of KINDS) {
      body[k.id] = {};
      for (const lang of ['de', 'en']) body[k.id][lang] = { subject: f.get(`${k.id}.${lang}.subject`), body: f.get(`${k.id}.${lang}.body`) };
    }
    return put('/api/templates/mails', body);
  }, 'Vorlagen gespeichert');

  main.onclick = async (e) => {
    if (await sharedClick(e)) return;
    const test = e.target.closest('[data-test]');
    if (test) {
      const result = await busy(test, () => post('/api/mails/test'));
      if (result) toast(`Test-Mail an ${result.to} gesendet – schau in dein Postfach`);
    }
    const phone = e.target.closest('[data-phone]');
    if (phone) {
      const action = phone.dataset.phone;
      if (action === 'copy') return copy(phone.dataset.address, 'Adresse');
      const view = await busy(phone, () => (action === 'check' ? api('/api/phone') : post(`/api/phone/${action}`)));
      if (!view) return;
      showPhone(view);
      if (action === 'on' && view.enabled) toast('Handy-Zugang ist an');
      if (action === 'off') toast('Handy-Zugang ist aus');
      if (action !== 'check') changed();
    }
  };
}

/**
 * The phone card: what Tailscale says, and one button for the next step - install, sign in,
 * allow Serve once, switch on. On the phone itself it only shows where things stand.
 */
function showPhone(v, error = null) {
  const card = document.getElementById('handy');
  if (!card) return;
  const pc = !onPhone();
  const head = (badge) => `<div class="card-head"><h3>${icon('phone')} Handy-Zugang</h3>${badge}</div>`;
  const check = `<button class="btn" type="button" data-phone="check">${icon('refresh')} Erneut prüfen</button>`;
  const intro = `<p class="card-note">Mit <strong>Tailscale</strong> erreichst du den Hub von deinem Handy aus – von überall, verschlüsselt und
    nur für deine eigenen Geräte. Im Internet ist nichts zu sehen, und am Router richtest du nichts ein. Vom Handy aus geht alles außer
    Schlüsseln, Backups, Releases, Bauen, Hochladen und Dateien auf dem PC.</p>`;

  if (!v) {
    card.innerHTML = `${head('')}<p class="faint">Tailscale ließ sich nicht fragen: ${esc(error ?? '')}</p>${pc ? `<div class="actions">${check}</div>` : ''}`;
    return;
  }

  if (v.enabled) {
    const facts = [
      ['Adresse fürs Handy', `<span class="mono">${esc(v.address)}</span>`],
      ['Nur für das Konto', esc(v.login ?? '')],
      ['An seit', esc(v.since ? fmtDateTime(v.since) : '–')],
      ['Tailscale auf diesem PC', v.state === 'Running' ? `${icon('check')} verbunden` : `<span class="warn">${esc(v.state ?? 'nicht erreichbar')}</span>`],
      ['Weitergabe an den Hub', v.servesHub ? `${icon('check')} aktiv` : '<span class="warn">aus – noch einmal einschalten</span>'],
      ['Startet mit Windows', v.autostart ? `${icon('check')} ja` : '<span class="warn">nein</span>'],
    ];
    card.innerHTML = `
      ${head('<span class="badge ok">an</span>')}
      <div class="facts">${facts.map(([k, val]) => `<div class="fact"><span>${k}</span><span>${val}</span></div>`).join('')}</div>
      <p class="card-note" style="margin-top:12px">Öffne die Adresse auf dem Handy – dort muss Tailscale mit demselben Konto verbunden sein – und
        leg sie als Lesezeichen oder auf den Home-Bildschirm. Der Hub startet mit Windows und läuft weiter; der PC darf dabei nicht schlafen.</p>
      ${pc ? `<div class="actions">
        <button class="btn" type="button" data-phone="copy" data-address="${esc(v.address)}">${icon('copy')} Adresse kopieren</button>
        <button class="btn danger" type="button" data-phone="off">${icon('power')} Ausschalten</button>
        ${check}
      </div>` : '<p class="faint">Ein- und ausschalten geht am PC.</p>'}`;
    return;
  }

  let step;
  if (!v.installed) {
    step = `
      <ol class="steps-list">
        <li>Tailscale auf diesem PC installieren und anmelden:
          <a href="https://tailscale.com/download/windows" target="_blank" rel="noopener">tailscale.com/download ${icon('external')}</a></li>
        <li>Tailscale auf dem Handy installieren (App Store oder Google Play) und mit <strong>demselben Konto</strong> anmelden.</li>
        <li>Hier „Erneut prüfen“, dann „Einschalten“.</li>
      </ol>`;
  } else if (v.state !== 'Running') {
    step = `<div class="issue">${icon('info')}<div><div class="t">Tailscale ist auf diesem PC nicht verbunden</div>
      <div class="d">Öffne Tailscale über das Symbol in der Taskleiste und melde dich an – mit dem Konto, das auch dein Handy nutzt.</div></div><span></span></div>`;
  } else {
    step = `
      <div class="facts">
        <div class="fact"><span>Dieser PC im Tailnet</span><span class="mono">${esc(v.tailscaleHost ?? '–')}</span></div>
        <div class="fact"><span>Angemeldet als</span><span>${esc(v.tailscaleLogin ?? '–')}</span></div>
      </div>
      ${v.approval ? `
        <div class="issue" style="margin-top:12px">${icon('info')}<div><div class="t">Tailscale fragt einmal nach deiner Freigabe</div>
          <div class="d">Damit dein Tailnet HTTPS und „Serve“ nutzen darf. Öffne den Link, bestätige dort, und klick danach noch einmal „Einschalten“.</div></div>
          <a class="btn small" href="${esc(v.approval)}" target="_blank" rel="noopener">${icon('external')} Freigabe öffnen</a></div>`
      : `<p class="card-note" style="margin-top:12px">„Einschalten“ lässt Tailscale die Anfragen deines Handys an den Hub weiterreichen und trägt den Hub
          in den Autostart ein. Nur dein Konto ${esc(v.tailscaleLogin ?? '')} kommt herein.</p>`}`;
  }

  card.innerHTML = `
    ${head('<span class="badge">aus</span>')}
    ${intro}
    ${step}
    ${pc ? `<div class="actions" style="margin-top:14px">
      ${v.installed && v.state === 'Running' ? `<button class="btn primary" type="button" data-phone="on">${icon('phone')} Einschalten</button>` : ''}
      ${check}
    </div>` : '<p class="faint">Ein- und ausschalten geht am PC.</p>'}`;
}

async function save(e, action, message) {
  e.preventDefault();
  const button = e.target.querySelector('[type=submit]');
  const result = await busy(button, action);
  if (result !== undefined) {
    toast(message);
    changed();
  }
}
