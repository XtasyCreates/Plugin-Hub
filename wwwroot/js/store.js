// The overview every page and the side bar read from, fetched once per page load.
// `changed()` tells the app to draw the current page again - after a build, say.

import { api } from './api.js';

let overview = null;

export async function loadOverview() {
  overview = await api('/api/overview');
  return overview;
}

export const getOverview = () => overview;

export const changed = () => window.dispatchEvent(new CustomEvent('hub:changed'));

export const keyOf = (o, product) => o.keys.find((k) => k.product === product);

export const projectOf = (o, id) => o.projects.find((p) => p.id === id);
