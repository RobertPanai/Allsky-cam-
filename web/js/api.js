// Accesso al motore locale secondo docs/API.md. Solo richieste a /api sulla stessa origine.

const DEFAULT_TIMEOUT_MS = 8000;
const POLL_MS = 5000;

// Errore con un tipo ('network' | 'timeout' | 'http' | 'parse') e, se il motore l'ha dato, un messaggio.
export class ApiError extends Error {
  constructor(kind, message = null, status = 0) {
    super(message || kind);
    this.name = 'ApiError';
    this.kind = kind;
    this.status = status;
    this.hasMessage = Boolean(message);
  }
}

export async function request(path, { method = 'GET', body, timeout = DEFAULT_TIMEOUT_MS } = {}) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeout);
  const hasBody = body !== undefined;

  let res;
  try {
    res = await fetch(path, {
      method,
      cache: 'no-store',
      headers: hasBody ? { 'Content-Type': 'application/json' } : {},
      body: hasBody ? JSON.stringify(body) : undefined,
      signal: controller.signal,
    });
  } catch (err) {
    throw new ApiError(err.name === 'AbortError' ? 'timeout' : 'network');
  } finally {
    clearTimeout(timer);
  }

  let text = '';
  try {
    text = await res.text();
  } catch {
    throw new ApiError('network', null, res.status);
  }

  let data = null;
  if (text) {
    try {
      data = JSON.parse(text);
    } catch {
      if (res.ok) throw new ApiError('parse', null, res.status);
    }
  }

  if (!res.ok) {
    const message = data && typeof data.error === 'string' ? data.error : null;
    throw new ApiError('http', message, res.status);
  }
  return data;
}

export const api = {
  status: () => request('/api/status'),
  config: () => request('/api/config'),
  saveConfig: (config) => request('/api/config', { method: 'PUT', body: config }),
  cameras: () => request('/api/cameras/discover'),
  connectCamera: (id) => request('/api/camera/connect', { method: 'POST', body: { id } }),
  nights: () => request('/api/nights'),
  frames: (nightId, { limit = 1000, offset = 0 } = {}) =>
    request(`/api/nights/${encodeURIComponent(nightId)}/frames?limit=${limit}&offset=${offset}`),
  startTimelapse: (nightId) =>
    request(`/api/nights/${encodeURIComponent(nightId)}/timelapse`, { method: 'POST', body: {} }),
  deleteNight: (nightId) => request(`/api/nights/${encodeURIComponent(nightId)}`, { method: 'DELETE' }),

  // Immagini: si usano direttamente come src di <img>, non passano da fetch.
  urls: {
    latestFrame: (version = '') => `/api/frame/latest.jpg${version ? `?v=${encodeURIComponent(version)}` : ''}`,
    nightFrame: (nightId, frameId) =>
      `/api/nights/${encodeURIComponent(nightId)}/frames/${encodeURIComponent(frameId)}.jpg`,
    keogram: (nightId) => `/api/nights/${encodeURIComponent(nightId)}/keogram.jpg`,
    startrails: (nightId) => `/api/nights/${encodeURIComponent(nightId)}/startrails.jpg`,
  },
};

// Eventi dal motore: SSE su /api/events; se non disponibili, polling di /api/status ogni 5 s.
// Restituisce una funzione per chiudere tutto.
export function openEvents({ onStatus, onFrame, onConnection }) {
  let source = null;
  let pollTimer = null;
  let closed = false;

  const poll = async () => {
    try {
      const status = await api.status();
      onConnection(true);
      onStatus(status);
    } catch {
      onConnection(false);
    }
  };

  const startPolling = () => {
    if (pollTimer || closed) return;
    poll();
    pollTimer = setInterval(poll, POLL_MS);
  };

  const stopPolling = () => {
    if (pollTimer) clearInterval(pollTimer);
    pollTimer = null;
  };

  if (typeof EventSource === 'undefined') {
    startPolling();
  } else {
    source = new EventSource('/api/events');

    source.addEventListener('status', (event) => {
      let status;
      try {
        status = JSON.parse(event.data);
      } catch {
        return;
      }
      stopPolling();
      onConnection(true);
      onStatus(status);
    });

    source.addEventListener('frame', (event) => {
      try {
        onFrame(JSON.parse(event.data));
      } catch {
        // Un frame malformato non blocca la UI: arriverà lo status successivo.
      }
    });

    source.onopen = () => {
      stopPolling();
      onConnection(true);
    };

    source.onerror = () => {
      onConnection(false);
      startPolling();
    };
  }

  return () => {
    closed = true;
    if (source) source.close();
    stopPolling();
  };
}
