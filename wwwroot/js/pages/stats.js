// Statistik: what was sold, the trials and what became of them, and the code requests - month by
// month. The hub hands out anonymous rows (/api/stats); this page adds them up for the period and
// the plug-in chosen in the one filter row on top, so every figure and chart below agrees.
// The way of sale (Per Mail / Online) comes from the settings and from each sale itself.

import { api } from '../api.js';
import { loadOverview } from '../store.js';
import { esc, icon } from '../ui.js';

const MONTHS = ['Jan', 'Feb', 'Mär', 'Apr', 'Mai', 'Jun', 'Jul', 'Aug', 'Sep', 'Okt', 'Nov', 'Dez'];
const LONG = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'];
const CHANNEL = { mail: 'Per Mail', online: 'Online' };
const money = new Intl.NumberFormat('de-DE', { style: 'currency', currency: 'EUR' });

// What the filter row shows - kept while the hub page is open
const view = { range: '12', product: '' };

export async function render(main) {
  // The overview feeds the side bar - without it, opening the hub on this tab shows no navigation
  const [, data] = await Promise.all([loadOverview(), api('/api/stats')]);
  draw(main, data);

  main.oninput = null;
  main.onchange = null;
  main.onclick = (e) => {
    const range = e.target.closest('[data-range]');
    const product = e.target.closest('[data-product]');
    if (!range && !product) return;
    if (range) view.range = range.dataset.range;
    if (product) view.product = product.dataset.product;
    draw(main, data);
  };
}

function draw(main, data) {
  const months = period(data);
  const inPeriod = (row) => months.includes(row.month);
  const ofProduct = (row) => !view.product || row.product === view.product;

  const sales = data.sales.filter((r) => ofProduct(r) && inPeriod(r));
  const trials = data.trials.filter((r) => ofProduct(r) && inPeriod(r));
  const requests = data.requests.filter((r) => ofProduct(r) && inPeriod(r));
  // States of today ignore the period: a trial running now may have started before it
  const running = data.trials.filter((r) => ofProduct(r) && r.running).length;
  const open = data.requests.filter((r) => ofProduct(r) && r.open).length;

  main.innerHTML = `
    <div class="page stats">
      <header class="page-head">
        <div>
          <h1>Statistik</h1>
          <p class="sub">Verkäufe, Testphasen und Code-Anfragen je Monat · Verkaufsweg jetzt:
            <b>${esc(CHANNEL[data.channel] ?? data.channel)}</b> – <a href="#/einstellungen?feld=verkaufsweg">ändern</a></p>
        </div>
      </header>

      <div class="stats-filters" role="group" aria-label="Filter">
        <div class="seg" role="radiogroup" aria-label="Zeitraum">
          ${segButton('range', '12', '12 Monate')}
          ${segButton('range', 'all', 'Alles')}
        </div>
        <div class="seg" role="radiogroup" aria-label="Plugin">
          ${segButton('product', '', 'Alle Plugins')}
          ${data.products.map((p) => segButton('product', p.id, p.name)).join('')}
        </div>
      </div>

      ${salesCard(sales, months)}
      ${trialsCard(trials, running, months)}
      ${requestsCard(requests, open, months, Boolean(view.product))}
      ${visitsCard(data)}
    </div>`;

  for (const chart of main.querySelectorAll('.vchart')) wireTooltip(chart);
}

function segButton(kind, value, label) {
  const on = view[kind] === value;
  return `<button type="button" role="radio" aria-checked="${on}" class="${on ? 'on' : ''}" data-${kind}="${esc(value)}">${esc(label)}</button>`;
}

/** The months shown: the last twelve, or everything from the first row on (at least six). */
function period(data) {
  const now = data.now;
  if (view.range === '12') return monthsBack(now, 12);
  const first = [...data.sales, ...data.trials, ...data.requests].map((r) => r.month).sort()[0];
  const count = first ? monthsBetween(first, now) + 1 : 12;
  return monthsBack(now, Math.max(6, count));
}

function monthsBack(now, count) {
  const [y, m] = now.split('-').map(Number);
  const list = [];
  for (let i = count - 1; i >= 0; i--) {
    const d = new Date(y, m - 1 - i, 1);
    list.push(`${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`);
  }
  return list;
}

function monthsBetween(a, b) {
  const [ay, am] = a.split('-').map(Number);
  const [by, bm] = b.split('-').map(Number);
  return (by - ay) * 12 + (bm - am);
}

// ── The four cards ──────────────────────────────────────────────────────

function salesCard(sales, months) {
  const paid = sales.filter((s) => s.paid && s.price != null);
  const unpaid = sales.filter((s) => !s.paid && s.price > 0);
  const revenue = sum(paid, (s) => s.price);
  const online = sales.filter((s) => s.channel === 'online').length;
  const withoutPrice = sales.filter((s) => s.price == null || s.price === 0).length;

  const series = [
    { key: 'mail', label: 'Per Mail', cls: 's1' },
    { key: 'online', label: 'Online', cls: 's2' },
  ];
  const perMonth = months.map((month) => {
    const here = sales.filter((s) => s.month === month);
    return {
      month,
      values: { mail: here.filter((s) => s.channel !== 'online').length, online: here.filter((s) => s.channel === 'online').length },
      extra: `${money.format(sum(here.filter((s) => s.paid && s.price != null), (s) => s.price))} Umsatz`,
    };
  });

  return `
    <section class="card" data-place="verkaeufe">
      <div class="card-head"><h3>${icon('package')} Verkäufe und Umsatz</h3></div>
      <div class="figures">
        ${figure('Käufe', sales.length, sales.length ? `${sales.length - online} per Mail · ${online} online` : 'noch keine im Zeitraum')}
        ${figure('Umsatz bezahlt', money.format(revenue), paid.length ? `aus ${paid.length} ${paid.length === 1 ? 'Kauf' : 'Käufen'}` : '–')}
        ${figure('Noch offen', unpaid.length ? money.format(sum(unpaid, (s) => s.price)) : '0', unpaid.length ? `${unpaid.length} nicht bezahlt` : 'alles bezahlt')}
        ${figure('Ohne Preis', withoutPrice, 'z. B. geschenkte Lizenzen')}
      </div>
      ${columns({ title: 'Käufe je Monat', series, perMonth, empty: 'Noch keine Käufe im Zeitraum – hier erscheinen sie je Monat, getrennt nach Verkaufsweg.', tableExtra: 'Umsatz' })}
    </section>`;
}

function trialsCard(trials, running, months) {
  const bought = trials.filter((t) => t.bought);
  const days = bought.map((t) => t.daysToBuy).filter((d) => d != null);
  const rate = trials.length ? Math.round((bought.length / trials.length) * 100) : null;

  const series = [
    { key: 'bought', label: 'danach gekauft', cls: 's3' },
    { key: 'not', label: 'noch nicht gekauft', cls: 'rest' },
  ];
  const perMonth = months.map((month) => {
    const here = trials.filter((t) => t.month === month);
    const yes = here.filter((t) => t.bought).length;
    return { month, values: { bought: yes, not: here.length - yes } };
  });

  return `
    <section class="card" data-place="testphasen">
      <div class="card-head"><h3>${icon('hourglass')} Testphasen und Kauf danach</h3></div>
      <div class="figures">
        ${figure('Testphasen begonnen', trials.length, trials.filter((t) => t.extended).length ? `${trials.filter((t) => t.extended).length} davon verlängert` : 'ein Gerät zählt einmal, auch verlängert')}
        ${figure('Laufen jetzt', running, 'unabhängig vom Zeitraum')}
        ${figure('Danach gekauft', rate == null ? '–' : `${rate} %`, trials.length ? `${bought.length} von ${trials.length}` : 'noch keine Testphasen')}
        ${figure('Bis zum Kauf', days.length ? `${Math.round(sum(days, (d) => d) / days.length)} Tage` : '–', days.length ? 'im Schnitt' : 'noch kein Kauf nach Test')}
      </div>
      ${columns({ title: 'Begonnene Testphasen je Monat', series, perMonth, empty: 'Noch keine Testphasen im Zeitraum – hier erscheinen sie je Monat, mit dem Anteil, der danach gekauft hat.' })}
    </section>`;
}

function requestsCard(requests, open, months, filtered) {
  const trial = requests.filter((r) => r.wants === 'trial').length;
  const full = requests.filter((r) => r.wants === 'full').length;
  const series = [
    { key: 'trial', label: 'Testcode', cls: 's1' },
    { key: 'full', label: 'Kauf', cls: 's3' },
    { key: 'none', label: 'nichts angekreuzt', cls: 'rest' },
  ];
  const perMonth = months.map((month) => {
    const here = requests.filter((r) => r.month === month);
    return {
      month,
      values: {
        trial: here.filter((r) => r.wants === 'trial').length,
        full: here.filter((r) => r.wants === 'full').length,
        none: here.filter((r) => r.wants !== 'trial' && r.wants !== 'full').length,
      },
    };
  });

  return `
    <section class="card" data-place="anfragen">
      <div class="card-head">
        <h3>${icon('mail')} Code-Anfragen</h3>
        <a class="btn ghost small" href="#/mails?feld=anfragen">Zu den Anfragen ${icon('chevron')}</a>
      </div>
      <div class="figures">
        ${figure('Anfragen', requests.length, filtered ? 'Anfragen ohne Plugin-Namen zählen nur bei „Alle Plugins“' : 'aus dem Postfach')}
        ${figure('Offen jetzt', open, 'unabhängig vom Zeitraum')}
        ${figure('Testcode gewünscht', trial, requests.length ? `${Math.round((trial / requests.length) * 100)} %` : '–')}
        ${figure('Kauf gewünscht', full, requests.length ? `${Math.round((full / requests.length) * 100)} %` : '–')}
      </div>
      ${columns({ title: 'Anfragen je Monat', series, perMonth, empty: 'Noch keine Anfragen im Zeitraum – hier erscheinen sie je Monat, sobald der Hub das Postfach liest.' })}
    </section>`;
}

function visitsCard(data) {
  const off = data.siteStats === 'off';
  return `
    <section class="card" data-place="aufrufe">
      <div class="card-head"><h3>${icon('globe')} Website-Aufrufe</h3><span class="badge">${off ? 'aus' : esc(data.siteStats)}</span></div>
      <div class="empty">
        Die Aufrufe kommen dazu, sobald die Website online ist: Dann wählst du in den Einstellungen unter
        „Website-Statistik“ die Quelle, und hier erscheinen die Aufrufe je Monat – neben den Anfragen und Käufen.
        <div class="actions center-actions">
          <a class="btn small" href="#/einstellungen?feld=website-statistik">${icon('sliders')} Einstellungen</a>
          <a class="btn small" href="#/planung/dokumente?datei=Website%20Plan%2001_10_26.md">${icon('book')} Plan zur Website</a>
        </div>
      </div>
    </section>`;
}

function figure(label, value, note) {
  return `
    <div class="figure">
      <div class="label">${esc(label)}</div>
      <div class="value">${esc(String(value))}</div>
      <div class="note">${esc(note)}</div>
    </div>`;
}

// ── A column chart: one stacked column per month ─────────────────────────

/**
 * perMonth: [{ month, values: { key: n }, extra? }] - series: [{ key, label, cls }] in stacking
 * order from the baseline up. Values stay readable without hovering: the legend names the
 * series, the busiest and the latest month carry their total, the table holds every number.
 */
function columns({ title, series, perMonth, empty, tableExtra }) {
  const totals = perMonth.map((m) => series.reduce((n, s) => n + (m.values[s.key] ?? 0), 0));
  const max = Math.max(0, ...totals);
  const top = niceMax(max);
  const ticks = tickValues(top);
  const step = Math.max(1, Math.ceil(perMonth.length / 12));
  const peak = totals.indexOf(max);
  const last = perMonth.length - 1;

  const cols = perMonth.map((m, i) => {
    const total = totals[i];
    const parts = series.filter((s) => (m.values[s.key] ?? 0) > 0)
      .map((s) => `<i class="vseg ${s.cls}" style="flex-grow:${m.values[s.key]}"></i>`).join('');
    const said = series.map((s) => `${m.values[s.key] ?? 0} ${s.label}`).join(', ');
    const label = `${longMonth(m.month)}: ${total} gesamt – ${said}${m.extra ? `, ${m.extra}` : ''}`;
    const height = (total / top) * 100;
    // The total rides on the cap of the busiest and of the latest month - not on every column
    const cap = total > 0 && (i === peak || i === last)
      ? `<span class="vcap" style="bottom:calc(${height}% + 4px)">${total}</span>` : '';
    return `
      <div class="vcol" tabindex="0" role="img" aria-label="${esc(label)}" data-i="${i}">
        ${total > 0 ? `<div class="vbar" style="height:${height}%">${parts}</div>` : ''}${cap}
      </div>`;
  }).join('');

  const xLabels = perMonth.map((m, i) => {
    const show = i % step === 0 || i === last;
    return `<span>${show ? esc(shortMonth(m.month, i === 0)) : ''}</span>`;
  }).join('');

  const rows = perMonth.slice().reverse().map((m, i) => {
    const total = totals[perMonth.length - 1 - i];
    return `<tr><th scope="row">${esc(longMonth(m.month))}</th>${series.map((s) => `<td>${m.values[s.key] ?? 0}</td>`).join('')}
      <td>${total}</td>${tableExtra ? `<td>${esc((m.extra ?? '').replace(` ${tableExtra}`, ''))}</td>` : ''}</tr>`;
  }).join('');

  return `
    <div class="vchart" data-series='${esc(JSON.stringify(series.map((s) => ({ key: s.key, label: s.label, cls: s.cls }))))}'
         data-months='${esc(JSON.stringify(perMonth))}'>
      <div class="vchart-head">
        <span class="vchart-title">${esc(title)}</span>
        <span class="vlegend">${series.map((s) => `<span><i class="key ${s.cls}"></i>${esc(s.label)}</span>`).join('')}</span>
      </div>
      ${max === 0 ? `<p class="vempty">${esc(empty)}</p>` : ''}
      <div class="vplot">
        <div class="vgrid">${ticks.map((t) => `<div class="vtick" style="bottom:${(t / top) * 100}%"><span>${t}</span></div>`).join('')}</div>
        <div class="vcols" style="--n:${perMonth.length}">${cols}</div>
        <div class="vtip" aria-hidden="true" hidden></div>
      </div>
      <div class="vx" style="--n:${perMonth.length}">${xLabels}</div>
      <details class="vtable">
        <summary>Als Tabelle</summary>
        <div class="table-wrap"><table>
          <thead><tr><th scope="col">Monat</th>${series.map((s) => `<th scope="col">${esc(s.label)}</th>`).join('')}<th scope="col">Gesamt</th>${tableExtra ? `<th scope="col">${esc(tableExtra)}</th>` : ''}</tr></thead>
          <tbody>${rows}</tbody>
        </table></div>
      </details>
    </div>`;
}

/** One tooltip per chart: the month, then every series with its value - on hover and on focus. */
function wireTooltip(chart) {
  const tip = chart.querySelector('.vtip');
  const plot = chart.querySelector('.vplot');
  const series = JSON.parse(chart.dataset.series);
  const months = JSON.parse(chart.dataset.months);

  const show = (col) => {
    const m = months[Number(col.dataset.i)];
    tip.replaceChildren();
    const head = document.createElement('div');
    head.className = 'vtip-head';
    head.textContent = longMonth(m.month);
    tip.append(head);
    for (const s of series) {
      const row = document.createElement('div');
      row.className = 'vtip-row';
      const key = document.createElement('i');
      key.className = `line ${s.cls}`;
      const value = document.createElement('b');
      value.textContent = String(m.values[s.key] ?? 0);
      const name = document.createElement('span');
      name.textContent = s.label;
      row.append(key, value, name);
      tip.append(row);
    }
    if (m.extra) {
      const extra = document.createElement('div');
      extra.className = 'vtip-extra';
      extra.textContent = m.extra;
      tip.append(extra);
    }
    tip.hidden = false;
    // Beside the column, kept inside the plot
    const box = plot.getBoundingClientRect();
    const at = col.getBoundingClientRect();
    const width = tip.offsetWidth;
    let left = at.left - box.left + at.width / 2 + 10;
    if (left + width > box.width) left = at.left - box.left + at.width / 2 - width - 10;
    tip.style.left = `${Math.max(0, left)}px`;
  };
  const hide = () => { tip.hidden = true; };

  for (const col of chart.querySelectorAll('.vcol')) {
    col.addEventListener('pointerenter', () => show(col));
    col.addEventListener('focus', () => show(col));
    col.addEventListener('pointerleave', hide);
    col.addEventListener('blur', hide);
  }
}

// ── Small helpers ───────────────────────────────────────────────────────

function sum(list, pick) {
  return list.reduce((n, x) => n + Number(pick(x) ?? 0), 0);
}

/** A clean top for the axis: 4 → 4, 7 → 8, 13 → 15, 230 → 250. */
function niceMax(max) {
  if (max <= 4) return 4;
  const magnitude = 10 ** Math.floor(Math.log10(max));
  for (const f of [1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10]) {
    const top = f * magnitude;
    if (top >= max && Number.isInteger(top / 2)) return top;
  }
  return 10 * magnitude;
}

/** Zero, the middle and the top - whole numbers only. */
function tickValues(top) {
  const half = top / 2;
  return Number.isInteger(half) ? [0, half, top] : [0, top];
}

function shortMonth(month, first) {
  const [y, m] = month.split('-').map(Number);
  return m === 1 || first ? `${MONTHS[m - 1]} ’${String(y).slice(2)}` : MONTHS[m - 1];
}

function longMonth(month) {
  const [y, m] = month.split('-').map(Number);
  return `${LONG[m - 1]} ${y}`;
}
