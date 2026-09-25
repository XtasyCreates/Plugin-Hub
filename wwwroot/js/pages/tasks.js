// Aufgaben: the to-do list with priorities. It lives in Hub\tasks.json, in Git, so Claude
// reads the same list in every session.

import { api, del, post, put } from '../api.js';
import { changed, loadOverview } from '../store.js';
import { sharedClick } from '../parts.js';
import { confirmDialog, dialog, esc, fmtDate, icon, toast } from '../ui.js';

const PRIORITIES = [
  { id: 'hoch', label: 'Hoch' },
  { id: 'mittel', label: 'Mittel' },
  { id: 'niedrig', label: 'Niedrig' },
];

let showDone = false;

export async function render(main) {
  const [o, tasks] = await Promise.all([loadOverview(), api('/api/tasks')]);
  const projectName = (id) => o.projects.find((p) => p.id === id)?.name;
  const open = tasks.filter((t) => !t.done);
  const done = tasks.filter((t) => t.done).sort((a, b) => (b.doneAt ?? '').localeCompare(a.doneAt ?? ''));

  const item = (t) => `
    <div class="task ${t.done ? 'done' : ''} prio-${esc(t.priority)}">
      <label class="check" title="${t.done ? 'Wieder öffnen' : 'Erledigt'}">
        <input type="checkbox" data-done="${esc(t.id)}" ${t.done ? 'checked' : ''} aria-label="Erledigt">
      </label>
      <div class="task-body">
        <div class="t">${esc(t.title)}
          ${t.project ? `<span class="badge">${esc(projectName(t.project) ?? t.project)}</span>` : ''}</div>
        ${t.note ? `<div class="d">${esc(t.note)}</div>` : ''}
        <div class="faint">${t.done ? `erledigt am ${esc(fmtDate(t.doneAt))}` : `angelegt am ${esc(fmtDate(t.created))}`}</div>
      </div>
      <div class="actions">
        <button class="btn small ghost icon-only" data-edit="${esc(t.id)}" aria-label="Bearbeiten">${icon('pencil')}</button>
        <button class="btn small ghost icon-only" data-delete="${esc(t.id)}" aria-label="Löschen">${icon('trash')}</button>
      </div>
    </div>`;

  main.innerHTML = `
    <div class="page">
      <header class="page-head">
        <div>
          <h1>Aufgaben</h1>
          <p class="sub">${open.length} offen${open.some((t) => t.priority === 'hoch') ? `, davon ${open.filter((t) => t.priority === 'hoch').length} wichtig` : ''} · liegt in Hub\\tasks.json – Claude sieht dieselbe Liste.</p>
        </div>
        <button class="btn primary" data-new>${icon('plus')} Aufgabe</button>
      </header>

      ${PRIORITIES.map((p) => {
        const list = open.filter((t) => t.priority === p.id);
        return list.length ? `
          <section>
            <h2 class="prio-head prio-${p.id}">${p.label} <span class="faint">${list.length}</span></h2>
            <div class="tasks">${list.map(item).join('')}</div>
          </section>` : '';
      }).join('')}
      ${open.length ? '' : '<div class="all-good">' + icon('checkCircle') + ' Alles erledigt</div>'}

      ${done.length ? `
        <section>
          <button class="more" data-toggle-done>${showDone ? 'Erledigte ausblenden' : `${done.length} erledigte anzeigen`}</button>
          ${showDone ? `<div class="tasks" style="margin-top:10px">${done.map(item).join('')}</div>` : ''}
        </section>` : ''}
    </div>`;

  const byId = (id) => tasks.find((t) => t.id === id);

  main.oninput = null;
  main.onchange = async (e) => {
    const box = e.target.closest('[data-done]');
    if (!box) return;
    try {
      await post(`/api/tasks/${box.dataset.done}/done`, { done: box.checked });
      if (box.checked) toast('Erledigt');
    } catch (ex) {
      toast(ex.message, 'bad');
    }
    changed();
  };

  main.onclick = async (e) => {
    if (await sharedClick(e)) return;
    const t = e.target;

    if (t.closest('[data-toggle-done]')) {
      showDone = !showDone;
      return render(main);
    }
    if (t.closest('[data-new]')) return edit(o, null);

    const editButton = t.closest('[data-edit]');
    if (editButton) return edit(o, byId(editButton.dataset.edit));

    const remove = t.closest('[data-delete]');
    if (remove) {
      const task = byId(remove.dataset.delete);
      if (!(await confirmDialog('Aufgabe löschen?', `„${esc(task.title)}“ verschwindet aus der Liste.`, { confirm: 'Löschen', danger: true }))) return;
      await del(`/api/tasks/${task.id}`).catch((ex) => toast(ex.message, 'bad'));
      changed();
    }
  };
}

async function edit(o, task) {
  const t = task ?? { title: '', note: '', priority: 'mittel', project: null };
  const saved = await dialog({
    title: task ? 'Aufgabe bearbeiten' : 'Neue Aufgabe',
    confirm: task ? 'Speichern' : 'Anlegen',
    body: `
      <label class="field"><span>Titel</span><input type="text" name="title" value="${esc(t.title)}"></label>
      <div class="form-grid">
        <label class="field"><span>Wichtigkeit</span>
          <select name="priority">${PRIORITIES.map((p) => `<option value="${p.id}" ${t.priority === p.id ? 'selected' : ''}>${p.label}</option>`).join('')}</select>
        </label>
        <label class="field"><span>Projekt <em>optional</em></span>
          <select name="project"><option value="">–</option>${o.projects.map((p) => `<option value="${esc(p.id)}" ${t.project === p.id ? 'selected' : ''}>${esc(p.name)}</option>`).join('')}</select>
        </label>
      </div>
      <label class="field"><span>Notiz <em>optional</em></span><textarea name="note" rows="4">${esc(t.note)}</textarea></label>`,
    submit: (v) => {
      const body = { title: v.title, note: v.note, priority: v.priority, project: v.project || null };
      return task ? put(`/api/tasks/${task.id}`, body) : post('/api/tasks', body);
    },
  });
  if (saved) changed();
}
