// The overview every page and the side bar read from, fetched once per page load.
// `changed()` tells the app to draw the current page again - after a build, say.

import { api } from './api.js';

let overview = null;
let phone = false;

export async function loadOverview() {
  overview = await api('/api/overview');
  return overview;
}

export const getOverview = () => overview;

/**
 * Asks the hub where this page is open. On the phone (through Tailscale) the body gets the
 * class "phone", and what only works on the PC - keys, backups, builds, releases, uploading,
 * files - is left out.
 */
export async function loadSession() {
  const session = await api('/api/session').catch(() => null);
  phone = Boolean(session?.phone);
  document.body.classList.toggle('phone', phone);
  return phone;
}

export const onPhone = () => phone;

export const changed = () => window.dispatchEvent(new CustomEvent('hub:changed'));

export const keyOf = (o, product) => o.keys.find((k) => k.product === product);

export const projectOf = (o, id) => o.projects.find((p) => p.id === id);

/**
 * Trial length, follow-up and "läuft bald ab" as they apply to one plug-in: its own values
 * (Einstellungen → the plug-in), or the common ones while it has none.
 */
export const productSettings = (shared, product) => shared.products?.[product]
  ?? { trialDays: shared.trialDays, followUpDays: shared.followUpDays, endingSoonDays: shared.endingSoonDays };

/**
 * The page a project runs itself (projects.json, "url") - on the phone under this PC's
 * Tailscale name and the project's own port, or not at all when it has none.
 */
export const projectUrl = (p) => !p.url ? null
  : !phone ? p.url
  : p.phonePort ? `https://${location.hostname}:${p.phonePort}/` : null;
