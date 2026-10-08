// Einstellungen, in sections: the hub in general - seller and contact (Hub\settings.json, in
// Git), the mail account (private, the password never comes back to the page), the mail
// templates, phone access through Tailscale (private, switched on and off only on the PC) -
// and one section per project that has settings of its own: each plug-in its trial length,
// follow-up and "läuft bald ab", the website its sales channel, an extra project the way to
// its own page.

import { api, post, put } from '../api.js';
import { changed, loadOverview, onPhone, productSettings, projectUrl } from '../store.js';
import { sharedClick } from '../parts.js';
import { busy, confirmDialog, copy, esc, fmtDateTime, icon, toast } from '../ui.js';
import { LOOK, getLook, setLook } from '../look.js';

const KINDS = [
  { id: 'code', label: 'Code senden' },
  { id: 'followup', label: 'Nachfrage vor Ablauf der Testphase' },
  { id: 'extension', label: 'Neuer Code nach Verlängerung' },
];

const NUMBERS = ['trialDays', 'followUpDays', 'endingSoonDays'];

// The general section, or a project id. Not 'hub': that is the id of the project "Plugin Hub",
// which has no section of its own - what holds for the hub is the general section.
const GENERAL = 'allgemein';
let section = GENERAL;

/** The section a field stands in - a link to a field ("?feld=") opens that section. */
function sectionOf(field, plugins, website) {
  const number = NUMBERS.find((n) => field === n || field.startsWith(`${n}-`));
  // Links from before each plug-in had its own numbers name no plug-in: the first one
  if (number) return field.includes('-') ? field.slice(number.length + 1) : plugins[0]?.id;
  if (['verkaufsweg', 'website-statistik', 'salesChannel', 'siteStats', 'channel'].includes(field)) return website?.id;
  return GENERAL;
}

export async function render(main, { params = {} } = {}) {
  const [o, settings, templates] = await Promise.all([loadOverview(), api('/api/settings'), api('/api/templates/mails')]);
  const s = settings.shared;
  const m = settings.mail;

  const plugins = o.projects.filter((p) => p.kind === 'plugin');
  const website = o.projects.find((p) => p.kind === 'website');
  const extras  = o.projects.filter((p) => p.kind === 'extra');
  const sections = [{ id: GENERAL, name: 'Hub allgemein', icon: 'grid' }, ...plugins, ...(website ? [website] : []), ...extras];

  if (params.bereich) section = params.bereich;
  if (params.feld) section = sectionOf(params.feld, plugins, website) ?? section;
  if (!sections.some((x) => x.id === section)) section = GENERAL;
  const project = section === GENERAL ? null : o.projects.find((p) => p.id === section);

  const body = !project ? hubSection(s, m, settings, templates)
    : project.kind === 'plugin' ? pluginSection(project, s, plugins)
    : project.kind === 'website' ? websiteSection(s)
    : extraSection(project);

  main.innerHTML = `
    <div class="page">
      <header class="page-head">
        <div>
          <h1>Einstellungen</h1>
          <p class="sub">Für den Hub allgemein und für jedes Projekt einzeln. Allgemeines liegt in Hub\\settings.json (in Git), das Mailkonto nur auf diesem PC, das Aussehen in diesem Browser.</p>
        </div>
      </header>
      <div class="filters" role="group" aria-label="Bereich">${sections.map((x) => `
        <a class="chip ${x.id === section ? 'on' : ''}" href="#/einstellungen?bereich=${esc(x.id)}" ${x.id === section ? 'aria-current="true"' : ''}>${icon(x.icon)}${esc(x.name)}</a>`).join('')}
      </div>
      ${body}
    </div>`;

  // Asking Tailscale takes a moment - the rest of the page does not wait for it
  if (!project) api('/api/phone').then(showPhone).catch((e) => showPhone(null, e.message));

  main.oninput = null;
  main.onchange = null;

  // Every card saves the whole of settings.json - each sends the others' values as they are
  const sharedBody = (changes) => ({
    contact: s.contact, signature: s.signature, seller: s.seller, sellerAddress: s.sellerAddress,
    trialDays: s.trialDays, followUpDays: s.followUpDays, endingSoonDays: s.endingSoonDays, defaultLanguage: s.defaultLanguage,
    salesChannel: s.salesChannel ?? 'mail', siteStats: s.siteStats ?? 'off', products: s.products ?? {},
    ...changes,
  });
  const on = (id, handler) => { const form = main.querySelector(`#${id}`); if (form) form.onsubmit = handler; };

  on('shared', (e) => save(e, () => {
    const f = new FormData(e.target);
    return put('/api/settings/shared', sharedBody({
      contact: f.get('contact'), signature: f.get('signature'), seller: f.get('seller'), sellerAddress: f.get('sellerAddress'),
      defaultLanguage: f.get('defaultLanguage'),
    }));
  }, s.contact !== new FormData(e.target).get('contact')
    ? 'Gespeichert. Die neue Kontakt-Adresse kommt mit dem nächsten Build ins Plugin.' : 'Gespeichert'));

  // One plug-in's three numbers, as its form shows them right now
  const numbers = (form) => Object.fromEntries(NUMBERS.map((n) => [n, Number(new FormData(form).get(`${n}-${section}`))]));

  on('product', (e) => save(e, () =>
    put('/api/settings/shared', sharedBody({ products: { ...(s.products ?? {}), [section]: numbers(e.target) } })),
  `Gespeichert – gilt für ${project?.name}`));

  on('channel', (e) => save(e, () => {
    const f = new FormData(e.target);
    return put('/api/settings/shared', sharedBody({ salesChannel: f.get('salesChannel'), siteStats: f.get('siteStats') }));
  }, 'Gespeichert – neue Codes merken sich diesen Verkaufsweg'));

  // One "Speichern" for address and password: two buttons meant saving one and forgetting the other
  on('mail', (e) => save(e, async () => {
    const f = new FormData(e.target);
    const saved = await put('/api/settings/mail', {
      senderName: f.get('senderName'), senderEmail: f.get('senderEmail'), host: f.get('host'), port: Number(f.get('port')), userName: '',
    });
    // Empty means "keep the stored one" - the page never gets it back to show
    const password = String(f.get('password') ?? '').replace(/\s+/g, '');
    if (password) await put('/api/settings/mail-password', { password });
    return saved;
  }, 'Gespeichert'));

  on('templates', (e) => save(e, () => {
    const f = new FormData(e.target);
    const body = {};
    for (const k of KINDS) {
      body[k.id] = {};
      for (const lang of ['de', 'en']) body[k.id][lang] = { subject: f.get(`${k.id}.${lang}.subject`), body: f.get(`${k.id}.${lang}.body`) };
    }
    return put('/api/templates/mails', body);
  }, 'Vorlagen gespeichert'));

  main.onclick = async (e) => {
    if (await sharedClick(e)) return;

    // Aussehen: a choice works at once - nothing to save, nothing to draw again
    const look = e.target.closest('[data-look]');
    if (look) {
      setLook(look.dataset.look, look.dataset.value);
      for (const b of look.parentElement.children) {
        b.classList.toggle('on', b === look);
        b.setAttribute('aria-checked', String(b === look));
      }
      return;
    }

    // The numbers in this plug-in's form become every plug-in's - and the common ones, for a plug-in to come
    const sync = e.target.closest('[data-sync]');
    if (sync) {
      const form = sync.closest('form');
      if (!form.reportValidity()) return;
      const v = numbers(form);
      const others = plugins.filter((p) => p.id !== section).map((p) => p.name).join(', ');
      const text = `${others} ${plugins.length > 2 ? 'bekommen' : 'bekommt'} dieselben Werte wie ${project.name}: ` +
        `Testdauer ${v.trialDays} Tage, Nachfrage ${v.followUpDays} Tage vor dem Ende, „Läuft bald ab“ ab ${v.endingSoonDays} Tagen.`;
      if (!(await confirmDialog('Für alle Plugins übernehmen?', text, { confirm: 'Übernehmen' }))) return;
      const result = await busy(sync, () => put('/api/settings/shared',
        sharedBody({ ...v, products: Object.fromEntries(plugins.map((p) => [p.id, v])) })));
      if (result !== undefined) {
        toast('Alle Plugins haben jetzt dieselben Werte');
        changed();
      }
      return;
    }

    const channel = e.target.closest('[data-channel]');
    if (channel) {
      const form = channel.closest('form');
      form.querySelector('[name=salesChannel]').value = channel.dataset.channel;
      for (const b of form.querySelectorAll('[data-channel]')) {
        b.classList.toggle('on', b === channel);
        b.setAttribute('aria-checked', String(b === channel));
      }
      return;
    }
    const test = e.target.closest('[data-test]');
    if (test) {
      const result = await busy(test, () => post('/api/mails/test'));
      if (result) toast(`Test-Mail an ${result.to} gesendet – schau in dein Postfach`);
    }
    const phone = e.target.closest('[data-phone]');
    if (phone) {
      const action = phone.dataset.phone;
      if (action === 'copy') return copy(phone.dataset.address, 'Adresse');
      const request = action === 'check' ? () => api('/api/phone')
        : action.startsWith('unattended') ? () => post('/api/phone/unattended', { on: action === 'unattended-on' })
        : () => post(`/api/phone/${action}`);
      const view = await busy(phone, request);
      if (!view) return;
      showPhone(view);
      if (action === 'on' && view.enabled) toast('Handy-Zugang ist an');
      if (action === 'on' && !view.enabled && view.approval) toast('Tailscale möchte zuerst deine Freigabe – siehe Karte');
      if (action === 'off') toast('Handy-Zugang ist aus');
      if (action === 'unattended-on') toast('Tailscale verbindet jetzt schon beim Windows-Start');
      if (action === 'unattended-off') toast('Tailscale verbindet wieder erst nach dem Anmelden');
      if (action !== 'check') changed();
    }
  };
}

// ── The sections ───────────────────────────────────────────────────────────

/** What holds for the hub and every plug-in alike. */
function hubSection(s, m, settings, templates) {
  return `
      <form class="card" id="shared">
        <div class="card-head"><h3>${icon('sliders')} Anbieter und Kontakt</h3></div>
        <p class="card-note">Gilt für alle Plugins. Testdauer und Nachfragen stellst du je Plugin ein – oben in dessen Bereich.</p>
        <div class="form-grid">
          <label class="field"><span>Kontakt-Adresse für Kunden</span>
            <input type="email" name="contact" value="${esc(s.contact)}" placeholder="name@beispiel.de">
            <span class="hint">Steht im Plugin („Request a free trial“, „Buy a license“), in der Anleitung und als Antwort-Adresse der Mails. Im Plugin wirkt eine Änderung ab dem nächsten Build.</span></label>
          <label class="field"><span>Unterschrift unter Mails</span>
            <input type="text" name="signature" value="${esc(s.signature)}"></label>
          <label class="field"><span>Anbieter (Name oder Firma)</span>
            <input type="text" name="seller" value="${esc(s.seller)}">
            <span class="hint">Steht in den Lizenzbedingungen und im Paket. Pflicht für ein Release.</span></label>
          <label class="field"><span>Anschrift des Anbieters</span>
            <input type="text" name="sellerAddress" value="${esc(s.sellerAddress)}" placeholder="Straße 1, 12345 Stadt, Deutschland"></label>
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

      ${lookCard()}

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
      </div>`;
}

/** How the hub looks on this device: each choice works at once and stays in this browser (js/look.js). */
function lookCard() {
  const look = getLook();
  return `<div class="card" id="aussehen" data-place="aussehen">
        <div class="card-head"><h3>${icon('layers')} Aussehen</h3><span class="badge">nur dieses Gerät</span></div>
        <p class="card-note">Wirkt sofort und bleibt in diesem Browser – PC und Handy können verschieden aussehen.</p>
        <div class="looks">${Object.entries(LOOK).map(([key, choice]) => `
          <div class="look">
            <span>${esc(choice.label)}</span>
            <div class="seg small" role="radiogroup" aria-label="${esc(choice.label)}">${choice.values.map(([value, label]) => `
              <button type="button" role="radio" aria-checked="${look[key] === value}" class="${look[key] === value ? 'on' : ''}"
                data-look="${key}" data-value="${value}">${esc(label)}</button>`).join('')}
            </div>
            ${choice.hint ? `<span class="hint">${esc(choice.hint)}</span>` : ''}
          </div>`).join('')}
        </div>
      </div>`;
}

/** One plug-in: how long its trials run and when the hub speaks up about them. */
function pluginSection(p, s, plugins) {
  const v = productSettings(s, p.id);
  const own = Boolean(s.products?.[p.id]);
  // Whether the plug-ins differ - then "für alle übernehmen" has something to do
  const same = plugins.every((x) => NUMBERS.every((n) => productSettings(s, x.id)[n] === v[n]));
  // A link from before each plug-in had its own numbers ("?feld=trialDays") lands in the first plug-in
  const legacy = (name) => (p.id === plugins[0]?.id ? `data-place="${name}"` : '');
  return `
      <form class="card" id="product">
        <div class="card-head">
          <h3>${icon('clock')} Testphase von ${esc(p.name)}</h3>
          <span class="badge ${same ? 'ok' : ''}">${same ? 'bei allen Plugins gleich' : own ? 'eigene Werte' : 'gemeinsame Werte'}</span>
        </div>
        <p class="card-note">Gilt nur für Codes von ${esc(p.name)}.${plugins.length > 1 ? ' „Für alle Plugins übernehmen“ setzt die Werte, die hier stehen, auch bei den anderen.' : ''}</p>
        <div class="form-grid">
          <label class="field"><span>Standard-Testdauer <em>Tage</em></span>
            <input type="number" name="trialDays-${esc(p.id)}" ${legacy('trialDays')} min="1" max="365" value="${v.trialDays}" required>
            <span class="hint">Wird beim Erzeugen eines Testcodes zuerst angeboten.</span></label>
          <label class="field"><span>Nachfrage vorschlagen <em>Tage vor Ende der Testphase</em></span>
            <input type="number" name="followUpDays-${esc(p.id)}" ${legacy('followUpDays')} min="0" max="60" value="${v.followUpDays}" required></label>
          <label class="field"><span>„Läuft bald ab“ ab <em>Tage vorher</em></span>
            <input type="number" name="endingSoonDays-${esc(p.id)}" ${legacy('endingSoonDays')} min="1" max="60" value="${v.endingSoonDays}" required></label>
        </div>
        <div class="actions" style="margin-top:14px">
          <button class="btn primary" type="submit">Speichern</button>
          ${plugins.length > 1 ? `<button class="btn" type="button" data-sync>${icon('copy')} Für alle Plugins übernehmen</button>` : ''}
        </div>
      </form>

      <div class="card">
        <div class="card-head"><h3>${icon(p.icon)} ${esc(p.name)}</h3></div>
        <div class="facts">
          <div class="fact"><span>Aktivierung in Rhino</span><span>Befehle <span class="mono">${esc(p.startCommand ?? '–')}</span>, <span class="mono">${esc(p.activationCommand ?? '–')}</span></span></div>
          <div class="fact"><span>Kontakt, Anbieter, Mails</span><span>für alle Plugins gleich – <a href="#/einstellungen?bereich=allgemein">Hub allgemein</a></span></div>
        </div>
        <div class="actions" style="margin-top:12px">
          <a class="btn small" href="#/projekt/${esc(p.id)}">${icon(p.icon)} Zum Projekt</a>
          <a class="btn small" href="#/releases?feld=release-${esc(p.id)}">${icon('tag')} Release</a>
          <a class="btn small" href="#/schluessel?feld=keys">${icon('shield')} Schlüssel</a>
        </div>
      </div>`;
}

/** The website: how codes are sold, and where its visit numbers come from. */
function websiteSection(s) {
  return `
      <form class="card" id="channel" data-place="verkaufsweg">
        <div class="card-head"><h3>${icon('chart')} Verkaufsweg und Website</h3></div>
        <p class="card-note">Wenn die Website online geht und du dort verkaufst, stellst du hier um – sonst nichts. Jeder neue Code merkt sich
          den Weg, die Statistik zeigt beide getrennt; bisherige Codes zählen als „Per Mail“.</p>
        <div class="form-grid">
          <div class="field">
            <span>Verkaufsweg</span>
            <div class="seg" role="radiogroup" aria-label="Verkaufsweg">
              <button type="button" role="radio" aria-checked="${s.salesChannel !== 'online'}" class="${s.salesChannel !== 'online' ? 'on' : ''}" data-channel="mail">${icon('mail')} Per Mail</button>
              <button type="button" role="radio" aria-checked="${s.salesChannel === 'online'}" class="${s.salesChannel === 'online' ? 'on' : ''}" data-channel="online">${icon('globe')} Online</button>
            </div>
            <input type="hidden" name="salesChannel" value="${esc(s.salesChannel ?? 'mail')}">
            <span class="hint">Per Mail: Anfrage per Mail, Code aus dem Hub. Online: Website und Zahlungsanbieter.</span>
          </div>
          <label class="field" data-place="website-statistik"><span>Website-Statistik <em>Quelle der Aufrufe</em></span>
            <select name="siteStats">
              <option value="off" selected>Aus – die Website ist noch nicht online</option>
              <option disabled>Zähler des Webspace – kommt mit dem Hosting</option>
            </select>
            <span class="hint">Die Quelle kommt dazu, wenn Hosting und Domain entschieden sind (offene Fragen im Projekt Website).</span></label>
        </div>
        <div class="actions" style="margin-top:14px"><button class="btn primary" type="submit">Speichern</button></div>
      </form>`;
}

/** A project of its own: it keeps its settings itself - the hub only shows the way. */
function extraSection(p) {
  const url = projectUrl(p);
  return `
      <div class="card">
        <div class="card-head"><h3>${icon(p.icon)} ${esc(p.name)}</h3></div>
        ${p.note ? `<p class="card-note">${esc(p.note)}</p>` : ''}
        <p>${esc(p.name)} verwaltet seine Einstellungen selbst, auf seiner eigenen Seite – sie gehören zu dir und nicht zu den Plugins.</p>
        <div class="actions" style="margin-top:12px">
          ${url ? `<a class="btn primary" href="${esc(url)}" target="_blank" rel="noopener">${icon('external')} ${esc(p.urlLabel ?? 'Öffnen')}</a>`
                : '<span class="faint">Seine Seite ist von hier aus nicht erreichbar.</span>'}
          <a class="btn" href="#/projekt/${esc(p.id)}">${icon(p.icon)} Zum Projekt</a>
          <a class="btn" href="#/planung?projekt=${esc(p.id)}">${icon('board')} Zur Roadmap</a>
        </div>
      </div>`;
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
    // After a restart: the hub starts with Windows once you sign in; Tailscale connects at the
    // Windows start only when it runs unattended - otherwise with its app, a minute later
    const restart = v.unattended === true
      ? `${icon('check')} Tailscale verbindet schon beim Windows-Start`
      : v.unattended === false
        ? '<span class="warn">Tailscale verbindet erst nach dem Anmelden, etwa eine Minute später</span>'
        : '–';
    const facts = [
      ['Adresse fürs Handy', `<span class="mono">${esc(v.address)}</span>`],
      ['Nur für das Konto', esc(v.login ?? '')],
      ['An seit', esc(v.since ? fmtDateTime(v.since) : '–')],
      ['Tailscale auf diesem PC', v.state === 'Running' ? `${icon('check')} verbunden` : `<span class="warn">${esc(v.state ?? 'nicht erreichbar')}</span>`],
      ['Weitergabe an den Hub', v.servesHub ? `${icon('check')} aktiv` : '<span class="warn">aus – der Hub richtet sie ein, sobald Tailscale verbunden ist</span>'],
      ...(v.mended ? [['Weitergabe selbst repariert', esc(fmtDateTime(v.mended))]] : []),
      ['Startet mit Windows', v.autostart ? `${icon('check')} ja, nach dem Anmelden` : '<span class="warn">nein</span>'],
      ['Nach einem Neustart', restart],
    ];
    const unattended = !pc ? ''
      : v.unattended === false
        ? `<button class="btn primary" type="button" data-phone="unattended-on">${icon('power')} Tailscale vor dem Anmelden verbinden</button>`
        : v.unattended === true
          ? '<button class="btn ghost" type="button" data-phone="unattended-off">Wieder erst nach dem Anmelden</button>'
          : '';
    card.innerHTML = `
      ${head('<span class="badge ok">an</span>')}
      <div class="facts">${facts.map(([k, val]) => `<div class="fact"><span>${k}</span><span>${val}</span></div>`).join('')}</div>
      <p class="card-note" style="margin-top:12px">Öffne die Adresse auf dem Handy – dort muss Tailscale mit demselben Konto verbunden sein – und
        leg sie als Lesezeichen oder auf den Home-Bildschirm. Nach einem Neustart meldest du dich an: Der Hub startet dann von selbst (sein Symbol
        erscheint neben der Uhr) und richtet die Weitergabe wieder ein, sobald Tailscale verbunden ist.
        ${v.unattended === false ? '„Tailscale vor dem Anmelden verbinden“ schaltet in Tailscale „Run unattended“ ein – dann ist es schon beim Windows-Start verbunden, auch ohne seine App.' : ''}</p>
      ${pc ? `<div class="actions">
        ${unattended}
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
        <div class="issue" style="margin-top:12px">${icon('info')}<div><div class="t">Schritt 1: Tailscale fragt einmal nach deiner Freigabe</div>
          <div class="d">Damit dein Tailnet HTTPS und „Serve“ nutzen darf. Öffne den Link, melde dich an, falls Tailscale fragt, und bestätige dort.
            Schritt 2: hier noch einmal „Einschalten“.</div></div>
          <a class="btn small primary" href="${esc(v.approval)}" target="_blank" rel="noopener">${icon('external')} Freigabe öffnen</a></div>`
      : `<p class="card-note" style="margin-top:12px">„Einschalten“ lässt Tailscale die Anfragen deines Handys an den Hub weiterreichen und trägt den Hub
          in den Autostart ein. Nur dein Konto ${esc(v.tailscaleLogin ?? '')} kommt herein.</p>`}`;
  }

  card.innerHTML = `
    ${head('<span class="badge">aus</span>')}
    ${intro}
    ${step}
    ${pc ? `<div class="actions" style="margin-top:14px">
      ${v.installed && v.state === 'Running'
        ? `<button class="btn ${v.approval ? '' : 'primary'}" type="button" data-phone="on">${icon('phone')} ${v.approval ? 'Noch einmal einschalten' : 'Einschalten'}</button>` : ''}
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
