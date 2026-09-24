// Talks to the hub. Every request carries X-Plugin-Hub: the server refuses changing
// requests without it, which is what keeps other web pages from using this one.

let reportOffline = () => {};
export const onOffline = (handler) => { reportOffline = handler; };

export async function api(path, { method = 'GET', body } = {}) {
  let response;
  try {
    response = await fetch(path, {
      method,
      headers: {
        'X-Plugin-Hub': '1',
        ...(body !== undefined && { 'Content-Type': 'application/json' }),
      },
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  } catch {
    reportOffline(true);
    throw new Error('Der Hub antwortet nicht.');
  }
  reportOffline(false);

  if (response.status === 204) return null;
  const data = await response.json().catch(() => null);
  if (!response.ok) throw new Error(data?.error ?? `Der Hub meldet Fehler ${response.status}.`);
  return data;
}

export const post = (path, body = {}) => api(path, { method: 'POST', body });
export const put = (path, body) => api(path, { method: 'PUT', body });
export const del = (path) => api(path, { method: 'DELETE' });
