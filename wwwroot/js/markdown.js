// A small Markdown renderer for the documents in the Planung tab: headings, paragraphs, nested
// lists with checkboxes, tables, code, quotes, rules, emphasis and links. Everything is escaped
// before it is formatted - a document can hold no HTML of its own. Headings get ids, so a link
// can point at a section.

import { esc } from './ui.js';

const LIST = /^(\s*)([-*+]|\d+[.)])\s+(.*)$/;
const FENCE = /^\s*(```|~~~)/;
const RULE = /^\s*([-*_])(\s*\1){2,}\s*$/;
const HEADING = /^(#{1,6})\s+(.*?)\s*#*\s*$/;
const TABLE_ROW = /^\s*\|.*\|\s*$/;
const TABLE_RULE = /^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$/;

/** Markdown text to HTML. `headings` collects [level, text, id] for a table of contents. */
export function markdown(text, headings = []) {
  const slugs = new Map();
  return blocks(String(text ?? '').replace(/\r\n?/g, '\n').split('\n'), slugs, headings);
}

function blocks(lines, slugs, headings) {
  const out = [];
  let i = 0;
  while (i < lines.length) {
    const line = lines[i];
    let m;

    if (!line.trim()) { i++; continue; }

    if ((m = line.match(FENCE))) {
      const fence = m[1];
      const code = [];
      for (i++; i < lines.length && !lines[i].trim().startsWith(fence); i++) code.push(lines[i]);
      i++;
      out.push(`<pre class="md-code"><code>${esc(code.join('\n'))}</code></pre>`);
      continue;
    }

    if ((m = line.match(HEADING))) {
      const level = m[1].length;
      const id = `md-${slug(m[2], slugs)}`;   // prefixed: a heading must not take an id the page uses
      headings.push([level, plain(m[2]), id]);
      out.push(`<h${level} id="${id}">${inline(m[2])}</h${level}>`);
      i++;
      continue;
    }

    if (RULE.test(line)) { out.push('<hr>'); i++; continue; }

    if (/^\s*>/.test(line)) {
      const quote = [];
      while (i < lines.length && /^\s*>/.test(lines[i])) quote.push(lines[i++].replace(/^\s*>\s?/, ''));
      out.push(`<blockquote>${blocks(quote, slugs, headings)}</blockquote>`);
      continue;
    }

    if (TABLE_ROW.test(line) && TABLE_RULE.test(lines[i + 1] ?? '')) {
      const [html, next] = table(lines, i);
      out.push(html);
      i = next;
      continue;
    }

    if (LIST.test(line)) {
      const [html, next] = list(lines, i);
      out.push(html);
      i = next;
      continue;
    }

    const para = [];
    while (i < lines.length && lines[i].trim() && !startsBlock(lines, i)) para.push(lines[i++]);
    out.push(`<p>${para.map((l, k) => inline(l.trim()) + (k < para.length - 1 ? (/( {2,}|\\)$/.test(l) ? '<br>' : ' ') : '')).join('')}</p>`);
  }
  return out.join('\n');
}

function startsBlock(lines, i) {
  const line = lines[i];
  return FENCE.test(line) || HEADING.test(line) || RULE.test(line) || /^\s*>/.test(line) || LIST.test(line)
    || (TABLE_ROW.test(line) && TABLE_RULE.test(lines[i + 1] ?? ''));
}

// ── Lists ──────────────────────────────────────────────────────────────────

function list(lines, i) {
  const items = [];
  while (i < lines.length) {
    const line = lines[i];
    const m = line.match(LIST);
    if (m) {
      items.push({ indent: m[1].replace(/\t/g, '    ').length, ordered: /\d/.test(m[2]), start: parseInt(m[2], 10), text: m[3] });
      i++;
    } else if (!line.trim()) {
      // A blank line ends the list unless an item or an indented line follows
      const next = lines[i + 1];
      if (next !== undefined && (LIST.test(next) || /^\s{2,}\S/.test(next))) i++;
      else break;
    } else if (/^\s+\S/.test(line) || !startsBlock(lines, i)) {
      items[items.length - 1].text += ' ' + line.trim();   // the item goes on
      i++;
    } else {
      break;
    }
  }

  let html = '';
  const open = [];
  for (const item of items) {
    const tag = item.ordered ? 'ol' : 'ul';
    while (open.length && item.indent < open[open.length - 1].indent) html += `</li></${open.pop().tag}>`;
    let top = open[open.length - 1];
    // "1." after "-" on the same level starts a list of its own
    if (top && item.indent === top.indent && top.tag !== tag) {
      html += `</li></${open.pop().tag}>`;
      top = open[open.length - 1];
    }
    if (!top || item.indent > top.indent) {
      open.push({ indent: item.indent, tag });
      html += item.ordered && item.start > 1 ? `<ol start="${item.start}">` : `<${tag}>`;
    } else {
      html += '</li>';
    }
    const box = item.text.match(/^\[([ xX])\]\s+(.*)$/);
    html += box
      ? `<li class="md-task"><input type="checkbox" disabled ${box[1] !== ' ' ? 'checked' : ''}> ${inline(box[2])}`
      : `<li>${inline(item.text)}`;
  }
  while (open.length) html += `</li></${open.pop().tag}>`;
  return [html, i];
}

// ── Tables ─────────────────────────────────────────────────────────────────

function table(lines, i) {
  const cells = (line) => line.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map((c) => c.trim());
  const head = cells(lines[i]);
  const align = cells(lines[i + 1]).map((c) => (c.startsWith(':') && c.endsWith(':') ? 'center' : c.endsWith(':') ? 'right' : ''));
  const rows = [];
  for (i += 2; i < lines.length && TABLE_ROW.test(lines[i]); i++) rows.push(cells(lines[i]));

  const cell = (tag, text, k) => `<${tag}${align[k] ? ` style="text-align:${align[k]}"` : ''}>${inline(text ?? '')}</${tag}>`;
  return [`<div class="md-table"><table>
    <thead><tr>${head.map((h, k) => cell('th', h, k)).join('')}</tr></thead>
    <tbody>${rows.map((r) => `<tr>${head.map((_, k) => cell('td', r[k], k)).join('')}</tr>`).join('')}</tbody>
  </table></div>`, i];
}

// ── Inline ─────────────────────────────────────────────────────────────────

function inline(text) {
  // Code spans are set aside first: nothing inside them is formatted
  const codes = [];
  let s = text.replace(/`([^`]+)`/g, (_, code) => `\u0000${codes.push(code) - 1}\u0000`);
  s = esc(s);
  s = s.replace(/!\[([^\]]*)\]\([^)\s]+\)/g, '$1');
  s = s.replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, (_, label, href) =>
    /^(https?:|mailto:)/i.test(href) ? `<a href="${href}" target="_blank" rel="noopener">${label}</a>` : label);
  s = s.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>').replace(/__(.+?)__/g, '<strong>$1</strong>');
  s = s.replace(/(^|[^*\w])\*(?!\s)(.+?)\*(?!\*)/g, '$1<em>$2</em>');
  s = s.replace(/(^|[^_\w])_(?!\s)(.+?)_(?!\w)/g, '$1<em>$2</em>');
  s = s.replace(/~~(.+?)~~/g, '<del>$1</del>');
  return s.replace(/\u0000(\d+)\u0000/g, (_, n) => `<code>${esc(codes[Number(n)])}</code>`);
}

const plain = (text) => text.replace(/[`*_~]/g, '').replace(/\[([^\]]+)\]\([^)]*\)/g, '$1').trim();

function slug(text, used) {
  const base = plain(text).toLowerCase().replace(/[^\p{L}\p{N}\s-]/gu, '').trim().replace(/\s+/g, '-') || 'abschnitt';
  const n = used.get(base) ?? 0;
  used.set(base, n + 1);
  return n ? `${base}-${n + 1}` : base;
}
