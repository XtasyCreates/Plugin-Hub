// Small movements that say where something went. The page changes at once - nothing waits for
// the hub's answer - and what ends up elsewhere glides there instead of jumping. All of it keeps
// still when movement is switched off (Einstellungen → Aussehen).

import { moving } from './look.js';

const EASE = 'cubic-bezier(.2, .7, .2, 1)';
export const MOVE_MS = 190;

const rects = (things) => new Map([...(things ?? [])].map((x) => (Array.isArray(x) ? [x[0], x[1].getBoundingClientRect()] : [x, x.getBoundingClientRect()])));

function slide(el, was, now, ms) {
  const dx = was.left - now.left;
  const dy = was.top - now.top;
  if (Math.abs(dx) < 0.5 && Math.abs(dy) < 0.5) return;
  const run = el.animate([{ transform: `translate(${dx}px, ${dy}px)` }, { transform: 'none' }], { duration: ms, easing: EASE });
  if (Math.hypot(dx, dy) > 150) lift(el, run);   // a long way: it travels above what it passes
}

/** While it is on its way, it lies over its neighbours (the class "moving" in hub.css). */
function lift(el, run) {
  el.classList.add('moving');
  run.finished.catch(() => {}).then(() => el.classList.remove('moving'));
}

function resize(el, was, now, ms) {
  if (Math.abs(was.height - now.height) < 0.5) return;
  el.animate([{ height: `${was.height}px` }, { height: `${now.height}px` }], { duration: ms, easing: EASE });
}

/**
 * Runs a change to the page. What `things()` names and ends up at another place glides there;
 * what `boxes()` names and ends up with another height grows or shrinks to it.
 */
export function glide(things, change, { boxes = null, ms = MOVE_MS } = {}) {
  if (!moving()) return change();
  const places = rects(things());
  const sizes = rects(boxes?.());
  change();
  // read everything first, then set it going - a running animation would spoil the reading
  const moved = [...things()].filter((el) => places.has(el)).map((el) => [el, places.get(el), el.getBoundingClientRect()]);
  const sized = [...sizes].map(([el, was]) => [el, was, el.getBoundingClientRect()]);
  for (const [el, was, now] of moved) slide(el, was, now, ms);
  for (const [el, was, now] of sized) resize(el, was, now, ms);
}

/** Something let go of at `from` (left, top): it comes from there and settles in its place, above its neighbours. */
export function settle(el, from, ms = MOVE_MS) {
  if (!el || !moving()) return;
  const now = el.getBoundingClientRect();
  lift(el, el.animate(
    [{ transform: `translate(${from.left - now.left}px, ${from.top - now.top}px)`, boxShadow: '0 10px 26px rgba(0, 0, 0, .32)' }, { transform: 'none' }],
    { duration: ms, easing: EASE }));
}

/** A line that leaves its list folds away, after a moment in which it shows why; resolves when it is gone. */
export function fold(el, { ms = 170, wait = 110 } = {}) {
  if (!el || !moving()) return Promise.resolve();
  const style = getComputedStyle(el);
  const from = { height: `${el.getBoundingClientRect().height}px`, opacity: 1 };
  const to = { height: '0px', opacity: 0 };
  for (const side of ['marginTop', 'marginBottom', 'paddingTop', 'paddingBottom']) {
    from[side] = style[side];
    to[side] = '0px';
  }
  el.style.overflow = 'hidden';
  return el.animate([from, to], { duration: ms, delay: wait, easing: 'ease-in', fill: 'forwards' }).finished.catch(() => {});
}

/** Something that leaves the page for good fades; resolves when it is gone. */
export function fade(el, ms = 130) {
  if (!el || !moving()) return Promise.resolve();
  return el.animate([{ opacity: 1 }, { opacity: 0, transform: 'scale(.96)' }], { duration: ms, easing: 'ease-in', fill: 'forwards' })
    .finished.catch(() => {});
}

/** As long as a movement takes - what draws the page again afterwards waits for it. */
export const settled = (ms = MOVE_MS + 20) => new Promise((done) => setTimeout(done, moving() ? ms : 0));

// ── Through a redraw ───────────────────────────────────────────────────────
// A page can name what the frame follows when it draws the page again ("followed" in a page's
// file): `move` - things by name, which glide when they end up elsewhere and fade in when they
// are new - and `size` - boxes by name, whose height grows or shrinks along.

/** Where everything a page names stands right now - taken before the redraw. */
export const snapshot = (named) => ({ move: rects(named.move), size: rects(named.size) });

/** After the redraw: `before` is the snapshot, `named` the same names on the page as it is now. */
export function carry(before, named, ms = MOVE_MS) {
  if (!moving() || !before.move.size) return;
  const moved = [...(named.move ?? [])].map(([key, el]) => [el, before.move.get(key), el.getBoundingClientRect()]);
  const sized = [...(named.size ?? [])].filter(([key]) => before.size.has(key)).map(([key, el]) => [el, before.size.get(key), el.getBoundingClientRect()]);
  for (const [el, was, now] of moved) {
    if (was) slide(el, was, now, ms);
    else el.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 160, easing: 'ease-out' });
  }
  for (const [el, was, now] of sized) resize(el, was, now, ms);
}
