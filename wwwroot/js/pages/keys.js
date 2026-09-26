// Schlüssel und Backup: one key pair per plug-in, the backup that keeps them safe, and the copy
// of the whole repository outside this PC.

import { post, put } from '../api.js';
import { changed, loadOverview } from '../store.js';
import { issueList, pushRepo, sharedClick } from '../parts.js';
import { busy, dialog, esc, fmtDateTime, icon, notice, plural, toast } from '../ui.js';

let changingRemote = false;

export async function render(main) {
  const o = await loadOverview();
  const issues = o.health.filter((h) => ['keys', 'backup', 'secrets'].includes(h.area));
  const remote = o.remote;

  main.innerHTML = `
    <div class="page">
      <header class="page-head">
        <div>
          <h1>Schlüssel und Backup</h1>
          <p class="sub">Jedes Plugin hat sein eigenes Schlüsselpaar – ein Code für das eine schaltet das andere nicht frei.</p>
        </div>
      </header>

      <div class="issue phone-note">${icon('phone')}<div><div class="t">Auf dem Handy nur zum Ansehen</div>
        <div class="d">Schlüssel, Backups und das Hochladen zu GitHub gehen am PC.</div></div><span></span></div>

      ${issueList(issues, { here: '#/schluessel' })}

      <div class="card" style="margin-top:16px" id="keys">
        <div class="card-head"><h3>${icon('key')} Schlüssel</h3></div>
        ${o.keys.map((k) => keyRow(k, o)).join('')}
      </div>

      <div class="card" id="backup">
        <div class="card-head"><h3>${icon('shield')} Backup</h3></div>
        ${backupState(o.backup)}
        <p class="card-note" style="margin-top:12px">Das Backup enthält alle privaten Schlüssel und das Kundenregister, verschlüsselt mit deinem Passwort.
          Leg die Datei auf einen USB-Stick oder in einen Cloud-Ordner und das Passwort in deinen Passwortmanager – ohne Passwort ist sie wertlos.</p>
        <div class="actions">
          <button class="btn primary" data-backup>${icon('download')} Backup erstellen</button>
          <button class="btn" data-restore>${icon('upload')} Backup wiederherstellen</button>
          <button class="btn ghost" data-open="backups">${icon('folder')} Backup-Ordner</button>
        </div>
      </div>

      ${remote ? repoCard(remote) : ''}

      <div class="card">
        <div class="card-head"><h3>${icon('lock')} Wo alles liegt</h3></div>
        <p class="card-note">Schlüssel und Register liegen außerhalb von „Plugins Rhino“, damit sie nie in Git, ein ZIP oder einen geteilten Ordner geraten.
          Die Schlüssel sind mit deinem Windows-Konto verschlüsselt: Nur du kannst sie lesen, und nur auf diesem PC. Auf einem neuen PC holst du sie aus dem Backup.</p>
        <div class="actions">
          <code class="mono">${esc(o.privateDir)}</code>
          <button class="btn small" data-open="private">${icon('folder')} Öffnen</button>
        </div>
      </div>
    </div>`;

  main.oninput = null;
  main.onchange = null;
  main.onclick = async (e) => {
    if (await sharedClick(e)) return;
    const t = e.target;
    const generate = t.closest('[data-generate]');
    if (generate) return generateKey(o.keys.find((k) => k.product === generate.dataset.generate));
    if (t.closest('[data-backup]')) return createBackup();
    if (t.closest('[data-restore]')) return restoreBackup();
    if (t.closest('[data-push]')) return pushRepo(t.closest('[data-push]'));
    if (t.closest('[data-change-remote]')) {
      changingRemote = true;
      changed();
    }
    if (t.closest('[data-keep-remote]')) {
      changingRemote = false;
      changed();
    }
  };
  main.querySelector('[data-remote-form]')?.addEventListener('submit', async (e) => {
    e.preventDefault();
    const url = new FormData(e.target).get('url');
    const state = await busy(e.target.querySelector('[type=submit]'), () => put('/api/git/remote', { url }));
    if (!state) return;
    changingRemote = false;
    toast('Verbunden – jetzt „Hochladen“ klicken');
    changed();
  });
}

/**
 * The repository outside this PC. Not set up: three steps and the address field. Set up: how
 * much is not uploaded yet, and the button that does it.
 */
function repoCard(remote) {
  const connected = remote.url && !changingRemote;
  const last = remote.lastPush && remote.lastPush.url === remote.url ? remote.lastPush : null;
  const behind = [remote.ahead && plural(remote.ahead, 'Commit', 'Commits'), remote.tags && plural(remote.tags, 'Version', 'Versionen')].filter(Boolean);

  const setup = `
    <ol class="steps-list">
      <li>Bei GitHub ein <strong>privates</strong>, leeres Repository anlegen – ohne README.
        <a href="https://github.com/new" target="_blank" rel="noopener">github.com/new ${icon('external')}</a>
        <div class="faint">Privat, weil der Quelltext der Plugins darin liegt. Schlüssel und Kunden sind nicht in Git.</div></li>
      <li>Die Adresse kopieren: auf GitHub der grüne Knopf „Code“ → HTTPS, z. B. https://github.com/name/plugins-rhino.git</li>
      <li>Hier eintragen, „Verbinden“, dann „Hochladen“. Beim ersten Mal öffnet Git ein Fenster, in dem du dich bei GitHub anmeldest.</li>
    </ol>
    <form class="inline" data-remote-form>
      <input type="text" name="url" data-place="git-remote" value="${esc(remote.url ?? '')}" placeholder="https://github.com/name/plugins-rhino.git" spellcheck="false" autocomplete="off">
      <button class="btn primary" type="submit">Verbinden</button>
      ${remote.url ? '<button class="btn" type="button" data-keep-remote>Abbrechen</button>' : ''}
    </form>`;

  const state = `
    <div class="facts">
      <div class="fact"><span>Repository</span><span class="mono" title="${esc(remote.url)}">${esc(remote.url)}</span></div>
      <div class="fact"><span>Zuletzt hochgeladen</span><span>${last ? esc(fmtDateTime(last.at)) : 'noch nie'}</span></div>
      <div class="fact"><span>Stand</span><span>${behind.length ? `${behind.join(' und ')} noch nicht oben` : `${icon('check')} alles gesichert`}</span></div>
    </div>
    <div class="actions" style="margin-top:14px">
      <button class="btn primary" data-push data-place="git-remote">${icon('cloud')} Hochladen</button>
      <button class="btn ghost" data-change-remote>Adresse ändern</button>
    </div>`;

  return `
    <div class="card" id="git-remote-card">
      <div class="card-head">
        <h3>${icon('cloud')} Repository sichern</h3>
        <span class="badge ${connected ? (behind.length ? 'accent' : 'ok') : 'bad'}">${connected ? (behind.length ? 'nicht aktuell' : 'gesichert') : 'nicht eingerichtet'}</span>
      </div>
      <p class="card-note">Quelltext, Versionen und Kundenpakete liegen nur auf diesem PC. Eine Kopie in einem privaten GitHub-Repository schützt vor einem Festplattenausfall –
        das Backup oben sichert nur Schlüssel und Kundenregister. Der Hub lädt nur hoch, wenn du klickst.</p>
      ${connected ? state : setup}
    </div>`;
}

function keyRow(k, o) {
  const project = o.projects.find((p) => p.id === k.product);
  const line = (ok, text) => `<div class="${ok ? 'yes' : 'no'}">${icon(ok ? 'checkCircle' : 'alert')} ${text}</div>`;

  const checks = k.inHub
    ? [
        line(true, `Im Hub · Fingerabdruck <span class="mono">${esc(k.fingerprint)}</span> · erzeugt ${esc(fmtDateTime(k.created))}`),
        k.inSource
          ? line(k.sourceMatches, k.sourceMatches
            ? `Im Plugin eingetragen (${esc(k.keyFile)})`
            : `Im Plugin steht ein anderer Schlüssel (<span class="mono">${esc(k.sourceFingerprint)}</span>)`)
          : line(false, `${esc(k.keyFile)} fehlt`),
        line(k.inBuild, k.inBuild ? 'In der gebauten .rhp enthalten' : 'Die .rhp ist noch mit dem alten Schlüssel gebaut'),
      ]
    : [line(false, 'Noch kein Schlüssel – der Hub kann für dieses Plugin keine Codes erzeugen')];

  const buttons = k.inHub
    ? `${k.sourceMatches && !k.inBuild ? `<button class="btn small" data-build="${esc(k.product)}">${icon('hammer')} Neu bauen</button>` : ''}
       <button class="btn small danger" data-generate="${esc(k.product)}">${icon('refresh')} Ersetzen …</button>`
    : `<button class="btn small primary" data-generate="${esc(k.product)}">${icon('key')} Schlüsselpaar erzeugen</button>`;

  return `
    <div class="keyrow">
      <div class="picon">${icon(project?.icon ?? 'key')}</div>
      <div><div class="t">${esc(k.name)}</div><div class="checks">${checks.join('')}</div></div>
      <div class="actions">${buttons}</div>
    </div>`;
}

function backupState(backup) {
  if (!backup.last) return `<div class="issue problem">${icon('alert')}<div><div class="t">Noch kein Backup</div></div><span></span></div>`;
  const stale = backup.keysChanged ? 'Die Schlüssel haben sich seitdem geändert – mach ein neues.'
    : backup.registerChanged ? 'Das Register hat sich seitdem geändert.' : 'Aktuell.';
  return `
    <div class="facts">
      <div class="fact"><span>Letztes Backup</span><span>${esc(fmtDateTime(backup.last.at))}</span></div>
      <div class="fact"><span>Inhalt</span><span>Schlüssel · ${backup.last.customers} Kunden · ${backup.last.licenses} Codes</span></div>
      <div class="fact"><span>Datei</span><span class="mono" title="${esc(backup.last.file)}">${esc(backup.last.file.split(/[\\/]/).pop())}</span></div>
      <div class="fact"><span>Stand</span><span>${esc(stale)}</span></div>
    </div>`;
}

async function generateKey(k) {
  const replacing = k.inHub || k.inSource;
  const info = await dialog({
    title: replacing ? `Neuen Schlüssel für ${k.name}?` : `Schlüsselpaar für ${k.name} erzeugen`,
    confirm: replacing ? 'Schlüssel ersetzen' : 'Erzeugen',
    danger: replacing,
    body: replacing
      ? `<p>Damit wird <strong>jeder bisher ausgegebene Code für ${esc(k.name)} ungültig</strong> – bei allen Kunden, sobald sie die nächste Version installieren. Du müsstest allen neue Codes schicken.</p>
         <label class="field"><span>Zur Bestätigung „${esc(k.name)}“ eintippen</span><input type="text" name="confirm" autocomplete="off"></label>`
      : `<p>Der private Schlüssel bleibt verschlüsselt im Hub. Der öffentliche wird in <span class="mono">${esc(k.keyFile)}</span> eingetragen – danach das Plugin neu bauen.</p>`,
    submit: (v) => post(`/api/keys/${k.product}/generate`, { confirm: v.confirm ?? null }),
  });
  if (!info) return;
  toast(`Neuer Schlüssel für ${k.name}: ${info.fingerprint}`);
  window.dispatchEvent(new CustomEvent('hub:changed'));
}

async function createBackup() {
  const record = await dialog({
    title: 'Backup erstellen',
    confirm: 'Backup erstellen',
    body: `
      <p>Wähl ein Passwort mit mindestens 10 Zeichen. Es lässt sich nicht zurücksetzen – ohne es ist das Backup wertlos.</p>
      <label class="field"><span>Passwort</span><input type="password" name="password" autocomplete="new-password"></label>
      <label class="field"><span>Passwort wiederholen</span><input type="password" name="repeat" autocomplete="new-password"></label>
      <label class="check"><input type="checkbox" name="saved"> Ich habe das Passwort in meinem Passwortmanager gespeichert</label>`,
    submit: (v) => {
      if ((v.password ?? '').length < 10) throw new Error('Das Passwort braucht mindestens 10 Zeichen.');
      if (v.password !== v.repeat) throw new Error('Die beiden Passwörter sind verschieden.');
      if (!v.saved) throw new Error('Speicher das Passwort zuerst – ohne es lässt sich das Backup nicht öffnen.');
      return post('/api/backup', { password: v.password });
    },
  });
  if (!record) return;
  window.dispatchEvent(new CustomEvent('hub:changed'));
  await dialog({
    title: 'Backup gespeichert',
    confirm: 'Im Explorer zeigen',
    cancel: 'Fertig',
    body: `<p>${esc(record.file.split(/[\\/]/).pop())}</p>
      <p>Kopier die Datei jetzt auf einen USB-Stick oder in einen Cloud-Ordner. Auf diesem PC allein schützt sie dich nicht, wenn der PC kaputtgeht.</p>`,
    submit: () => post('/api/open', { linkId: 'backups' }),
  });
}

async function restoreBackup() {
  const result = await dialog({
    title: 'Backup wiederherstellen',
    confirm: 'Wiederherstellen',
    body: `
      <p>Holt Schlüssel zurück, die hier fehlen, und ergänzt das Kundenregister. Nichts wird überschrieben.</p>
      <label class="field"><span>Backup-Datei (.hubbackup)</span><input type="file" name="file" accept=".hubbackup"></label>
      <label class="field"><span>Passwort</span><input type="password" name="password" autocomplete="current-password"></label>`,
    submit: async (v) => {
      if (!(v.file instanceof File) || !v.file.size) throw new Error('Wähl zuerst eine Backup-Datei.');
      return post('/api/backup/restore', { content: await v.file.text(), password: v.password });
    },
  });
  if (!result) return;

  const keys = [
    result.restored.length ? `Zurückgeholt: ${result.restored.join(', ')}` : '',
    result.same.length ? `Schon vorhanden: ${result.same.join(', ')}` : '',
    result.conflicts.length ? `<strong>Nicht übernommen</strong> (hier liegt ein anderer Schlüssel): ${result.conflicts.join(', ')}` : '',
  ].filter(Boolean);
  await notice('Backup wiederhergestellt', `
    <p>${keys.length ? keys.join('<br>') : 'Das Backup enthielt keine Schlüssel.'}</p>
    <p>Kundenregister: ${result.customers} Kunden und ${result.licenses} Codes ergänzt.</p>`);
  window.dispatchEvent(new CustomEvent('hub:changed'));
}
