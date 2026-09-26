// Releases: the version customers see, the customer package and its archive. The assistant
// asks for version, changes and checks; the hub then builds, packs, commits and tags - the
// build dock shows how far it got.

import { api, post } from '../api.js';
import { loadOverview } from '../store.js';
import { commitHub, follow, sharedClick } from '../parts.js';
import { dialog, esc, fmtDate, icon, plural } from '../ui.js';

export async function render(main) {
  const [o, states] = await Promise.all([loadOverview(), api('/api/releases')]);

  main.innerHTML = `
    <div class="page">
      <header class="page-head">
        <div>
          <h1>Releases</h1>
          <p class="sub">Kundenpakete mit Versionsnummer, Anleitung und Lizenzbedingungen – archiviert unter Releases\\ und in Git.</p>
        </div>
        <button class="btn" data-open="releases">${icon('folder')} Kundenpakete</button>
      </header>

      <div class="issue phone-note">${icon('phone')}<div><div class="t">Auf dem Handy nur zum Ansehen</div>
        <div class="d">Releases erstellst du am PC – der Hub baut und packt sie dort.</div></div><span></span></div>

      ${states.map((s) => card(o, s)).join('')}
    </div>`;

  main.oninput = null;
  main.onchange = null;
  main.onclick = async (e) => {
    if (await sharedClick(e)) return;
    const start = e.target.closest('[data-release]');
    if (start) return assistant(start.dataset.release);
  };
}

function card(o, s) {
  const project = o.projects.find((p) => p.id === s.product);
  const last = s.last;
  const counts = project?.plan?.counts ?? {};
  const board = `#/planung?projekt=${encodeURIComponent(s.product)}`;
  const plan = [
    counts.getestet ? `<a href="${board}&stufe=getestet">${plural(counts.getestet, 'getestete Funktion geht', 'getestete Funktionen gehen')} mit</a>` : '',
    counts.gebaut ? `<a class="warn" href="${board}&stufe=gebaut">${plural(counts.gebaut, 'Funktion ist', 'Funktionen sind')} noch nicht getestet</a>` : '',
  ].filter(Boolean);
  return `
    <div class="card" id="release-${esc(s.product)}">
      <div class="card-head">
        <h3>${icon(project?.icon ?? 'package')} ${esc(s.name)}</h3>
        <button class="btn primary" data-release="${esc(s.product)}">${icon('tag')} Release ${esc(s.version ?? '')} erstellen …</button>
      </div>
      <div class="facts">
        <div class="fact"><span>Letztes Release</span><span>${last ? `${esc(last.version)} vom ${esc(fmtDate(last.date))}` : 'noch keins'}</span></div>
        <div class="fact"><span>Nächste Version</span><span>${esc(s.version ?? '–')} <span class="faint">(Testbuilds heißen ${esc(s.version ?? '')}-test)</span></span></div>
        <div class="fact"><span>Gebaute .rhp</span><span>${esc(s.buildVersion ?? '–')}</span></div>
        ${plan.length ? `<div class="fact"><span>Roadmap</span><span>${plan.join(' · ')}</span></div>` : ''}
      </div>
      ${last ? `
        <div class="since">
          <div class="label">${s.since.length ? `Seit ${esc(last.version)} geändert:` : `Seit ${esc(last.version)} nichts geändert.`}</div>
          ${s.since.length ? `<ul>${s.since.slice(-8).map((c) => `<li>${esc(c)}</li>`).join('')}</ul>` : ''}
        </div>` : ''}
      ${s.history.length ? `
        <div class="table-wrap" style="margin-top:14px"><table class="table">
          <thead><tr><th>Version</th><th>Datum</th><th>Paket</th><th>Tests</th><th class="shrink"></th></tr></thead>
          <tbody>${s.history.map((r) => {
            const ticked = Object.values(r.checks).filter(Boolean).length;
            return `
              <tr>
                <td><strong>${esc(r.version)}</strong></td>
                <td class="nowrap">${esc(fmtDate(r.date))}</td>
                <td class="mono">${esc(r.zip)} <span class="faint">${Math.round(r.zipSize / 1024)} KB</span></td>
                <td>${ticked}/${Object.keys(r.checks).length} abgehakt</td>
                <td class="shrink"><button class="btn small" data-open="${esc(`path:Releases/${s.name}/${r.version}/${r.zip}`)}">${icon('folder')} Zeigen</button></td>
              </tr>`;
          }).join('')}
          </tbody>
        </table></div>` : ''}
    </div>`;
}

async function assistant(product) {
  let plan;
  try {
    plan = await api(`/api/releases/${product}/plan`);
  } catch (e) {
    return;
  }

  const auto = plan.checks.filter((c) => !c.manual);
  const manual = plan.checks.filter((c) => c.manual);
  const hubDirty = auto.some((c) => c.id === 'clean' && !c.ok && (c.detail ?? '').includes('Hub-Dateien'));
  let warned = false;

  const view = await dialog({
    title: `Release ${plan.name}`,
    wide: true,
    confirm: 'Bauen und veröffentlichen',
    body: `
      <div class="form-grid">
        <label class="field"><span>Version</span><input type="text" name="version" value="${esc(plan.version)}" spellcheck="false"></label>
        <div class="field"><span>Bisher</span><input type="text" value="${esc(plan.lastVersion ?? 'noch kein Release')}" readonly></div>
      </div>
      <label class="field">
        <span>Änderungen <em>stehen so im Kundenpaket (Changes.txt)</em></span>
        <textarea name="changes" rows="7">${esc(plan.changes)}</textarea>
      </label>
      <div class="field">
        <span>Prüfungen</span>
        <div class="checks">${auto.map((c) => `
          <div class="${c.ok ? 'yes' : c.blocking ? 'no' : 'warn'}">
            ${icon(c.ok ? 'checkCircle' : 'alert')}
            <span>${esc(c.label)}${c.ok ? '' : ` – ${esc(c.detail ?? '')}`}</span>
          </div>`).join('')}
        </div>
        ${hubDirty ? '<div><button type="button" class="btn small" data-commit-hub>Hub-Änderungen committen</button></div>' : ''}
      </div>
      <div class="field">
        <span>Von dir getestet</span>
        ${manual.map((c) => `<label class="check"><input type="checkbox" name="${esc(c.id)}"> ${esc(c.label)}</label>`).join('')}
      </div>
      <p class="faint">Der Hub trägt die Version ein, baut ohne „-test“, schnürt das Paket nach Releases\\${esc(plan.name)}\\, committet es mit dem Tag ${esc(plan.product)}-&lt;Version&gt; und stellt die nächste Version ein.</p>`,
    onOpen: (form) => {
      form.querySelector('[data-commit-hub]')?.addEventListener('click', async (e) => {
        await commitHub(e.target);
        e.target.replaceWith(Object.assign(document.createElement('span'), { className: 'faint', textContent: 'Committed – schließ den Assistenten und öffne ihn neu.' }));
      });
    },
    submit: async (v) => {
      const confirmed = Object.fromEntries(manual.map((c) => [c.id, Boolean(v[c.id])]));
      if (!warned && Object.values(confirmed).some((x) => !x)) {
        warned = true;
        throw new Error('Nicht alle Tests sind abgehakt. Klick noch einmal, um trotzdem zu veröffentlichen.');
      }
      return post(`/api/releases/${plan.product}`, { version: v.version.trim(), changes: v.changes, confirmed });
    },
  });
  if (view) follow(view);
}
