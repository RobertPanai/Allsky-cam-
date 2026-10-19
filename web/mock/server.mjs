// Server di prova della UI: implementa il contratto di docs/API.md con dati finti,
// e serve i file statici di web/ su "/". Solo moduli built-in di Node.
//
// Avvio:  node web/mock/server.mjs        (porta 5810, oppure PORT=... per cambiarla)
// Stato:  MOCK_STATE=idle|error node web/mock/server.mjs   per vedere gli altri stati
// Durata finta della creazione del timelapse: MOCK_TIMELAPSE_MS=12000 (predefinito).
//
// Solo per le prove, fuori dal contratto (non usare dalla UI):
//   POST /api/__mock/timelapse  {"nightId":"2026-10-18","state":"none|ready|failed|generating"|null}
//       forza lo stato del timelapse di una notte (null lo ripristina).
//   POST /api/__mock/reset      annulla le modifiche di prova (overrides, notti eliminate, job).
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const WEB_ROOT = path.resolve(HERE, '..');
const ASSET_DIR = path.join(HERE, 'assets');
const PORT = Number(process.env.PORT) || 5810;
const HOST = '127.0.0.1';
const VERSION = '0.1.0';
const TZ = 'Europe/Rome';
const CADENCE_MS = 30_000;
const DAY_MS = 86_400_000;
const STARTED = Date.now();
const MOCK_STATE = ['capturing', 'idle', 'error'].includes(process.env.MOCK_STATE)
  ? process.env.MOCK_STATE
  : 'capturing';

// ----- Configurazione (in memoria, come da contratto) -----

let config = {
  schemaVersion: 1,
  language: 'it',
  site: { name: 'Osservatorio', latitude: 45.07, longitude: 7.69, elevationM: 240, timezone: TZ },
  camera: { id: 'sim-1', gain: 250, maxExposureMs: 30000, dayExposureMs: 2, binning: 1 },
  color: {
    whiteBalance: 'auto',
    manualGains: [1.0, 1.0, 1.0],
    smoothing: 0.85,
    gradientRemoval: 0.5,
    stretch: 'mtf',
    saturation: 1.1,
  },
  mask: { enabled: false, imagePath: null },
  overlay: { enabled: true, showTime: true, showSensorTemp: true, label: '' },
  capture: { intervalSeconds: 30, dayCapture: true },
  products: { timelapse: true, keogram: true, startrails: true, timelapseFps: 25 },
  storage: { folder: 'C:\\Users\\Osservatorio\\AllSky', keepNights: 30 },
  ui: { theme: 'auto', port: PORT, allowLan: false },
};

const TIMELAPSE_MS = Number(process.env.MOCK_TIMELAPSE_MS) || 12_000;

const deletedNights = new Set();
const timelapseJobs = new Map(); // nightId -> { kind, nightId, startedAt }
const timelapseDone = new Set(); // notti il cui timelapse è stato creato durante la prova
const timelapseOverride = new Map(); // nightId -> stato forzato da /api/__mock/timelapse

// ----- Tempo e Sole nel fuso dell'osservatorio -----

const localFormatter = new Intl.DateTimeFormat('en-US', {
  timeZone: TZ,
  hourCycle: 'h23',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
});

function localParts(ms) {
  const v = {};
  for (const part of localFormatter.formatToParts(new Date(ms))) {
    if (part.type !== 'literal') v[part.type] = Number(part.value);
  }
  return { year: v.year, month: v.month, day: v.day, hour: v.hour % 24, minute: v.minute, second: v.second };
}

function offsetMs(ms) {
  const p = localParts(ms);
  return Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute, p.second) - Math.floor(ms / 1000) * 1000;
}

function zonedToMs(y, m, d, h) {
  const guess = Date.UTC(y, m - 1, d, h);
  const first = offsetMs(guess);
  let ts = guess - first;
  const second = offsetMs(ts);
  if (second !== first) ts = guess - second;
  return ts;
}

function nightIdFor(ms) {
  const p = localParts(ms);
  const day = new Date(Date.UTC(p.year, p.month - 1, p.day + (p.hour < 12 ? -1 : 0)));
  return day.toISOString().slice(0, 10);
}

function nightSpan(id) {
  const [y, m, d] = id.split('-').map(Number);
  return { start: zonedToMs(y, m, d, 12), end: zonedToMs(y, m, d + 1, 12) };
}

function localHour(ms) {
  const p = localParts(ms);
  return p.hour + p.minute / 60 + p.second / 3600;
}

// Modello di comodo: altezza del Sole massima 50 gradi verso le 13:00 locali.
function sunAltitude(ms) {
  return 50 * Math.cos((2 * Math.PI * (localHour(ms) - 13)) / 24);
}

function moonAltitude(ms) {
  return 31 * Math.sin((ms / DAY_MS) * ((2 * Math.PI) / 27.3) + 0.8);
}

function phaseAt(ms) {
  const alt = sunAltitude(ms);
  if (alt > 0) return 'day';
  if (alt > -6) return 'twilight';
  return 'night';
}

function iso(ms) {
  return new Date(ms).toISOString().replace('.000Z', 'Z');
}

function round1(n) {
  return Math.round(n * 10) / 10;
}

// ----- Notti, fasi e frame (calcolati al volo, con cache per intervallo di 30 s) -----

const bandsCache = new Map();
const framesCache = new Map();
let nightsCache = { bucket: -1, list: null };

function bandsFor(nightId) {
  if (bandsCache.has(nightId)) return bandsCache.get(nightId);
  const span = nightSpan(nightId);
  const out = [];
  let cur = null;
  for (let ms = span.start; ms < span.end; ms += 60_000) {
    const phase = phaseAt(ms);
    if (!cur || cur.phase !== phase) {
      if (cur) out.push(cur);
      cur = { phase, from: ms, to: ms };
    }
    cur.to = ms + 60_000;
  }
  if (cur) out.push(cur);
  const bands = out.map((b) => ({ phase: b.phase, from: iso(b.from), to: iso(b.to) }));
  bandsCache.set(nightId, bands);
  return bands;
}

function framesFor(nightId) {
  const bucket = Math.floor(Date.now() / CADENCE_MS);
  const key = `${nightId}|${bucket}`;
  if (framesCache.has(key)) return framesCache.get(key);
  const span = nightSpan(nightId);
  const cutoff = Math.min(span.end, bucket * CADENCE_MS + CADENCE_MS);
  const list = [];
  for (let ms = span.start; ms < cutoff; ms += CADENCE_MS) {
    list.push({ id: iso(ms), timestamp: iso(ms), phase: phaseAt(ms) });
  }
  for (const k of framesCache.keys()) {
    if (!k.endsWith(`|${bucket}`)) framesCache.delete(k);
  }
  framesCache.set(key, list);
  return list;
}

// Un job finisce dopo TIMELAPSE_MS: da lì il timelapse della notte è pronto.
function finishJobs() {
  const now = Date.now();
  for (const [id, job] of timelapseJobs) {
    if (now - job.startedAt >= TIMELAPSE_MS) {
      timelapseJobs.delete(id);
      timelapseDone.add(id);
      nightsCache = { bucket: -1, list: null };
    }
  }
}

function timelapseOf(id) {
  if (timelapseOverride.has(id)) return timelapseOverride.get(id);
  if (timelapseJobs.has(id)) return 'generating';
  if (timelapseDone.has(id)) return 'ready';
  // Le notti passate hanno già il timelapse; quella in corso no, finché non lo si crea.
  return id === nightIdFor(Date.now()) ? 'none' : 'ready';
}

function nightsList() {
  finishJobs();
  const now = Date.now();
  const bucket = Math.floor(now / CADENCE_MS);
  if (nightsCache.bucket === bucket && nightsCache.list) return nightsCache.list;

  const list = [];
  for (let k = 0; k < 6; k++) {
    const id = nightIdFor(now - k * DAY_MS);
    if (deletedNights.has(id) || list.some((n) => n.id === id)) continue;
    const frames = framesFor(id);
    if (!frames.length) continue;
    const timelapse = timelapseOf(id);
    list.push({
      id,
      frames: frames.length,
      sizeMb: Math.round(frames.length * 0.62),
      firstFrame: frames[0].timestamp,
      lastFrame: frames[frames.length - 1].timestamp,
      keogram: true,
      startrails: true,
      timelapse,
      sunBands: bandsFor(id),
    });
  }
  nightsCache = { bucket, list };
  return list;
}

// ----- Immagini di prova (SVG in web/mock/assets, servite come /api/.../*.jpg) -----

const assetCache = new Map();

function asset(name) {
  if (!assetCache.has(name)) {
    const file = path.join(ASSET_DIR, name);
    assetCache.set(name, fs.readFileSync(file));
  }
  return assetCache.get(name);
}

function frameAssetFor(phase, nightId) {
  if (phase === 'day') return 'frame-day.svg';
  if (phase === 'twilight') return 'frame-dusk.svg';
  const even = nightId ? Number(nightId.slice(-2)) % 2 === 0 : true;
  return even ? 'frame-night-a.svg' : 'frame-night-b.svg';
}

// ----- Stato -----

function activeJobs() {
  finishJobs();
  const now = Date.now();
  const out = [];
  for (const job of timelapseJobs.values()) {
    const progress = Math.min(99, Math.floor(((now - job.startedAt) / TIMELAPSE_MS) * 100));
    out.push({ kind: job.kind, nightId: job.nightId, progress });
  }
  return out;
}

function status() {
  const now = Date.now();
  const frameMs = Math.floor(now / CADENCE_MS) * CADENCE_MS;
  const phase = phaseAt(now);
  const day = phase === 'day';
  const wobble = Math.round(120 * Math.sin(frameMs / CADENCE_MS));
  const connected = MOCK_STATE !== 'error';

  return {
    version: VERSION,
    state: MOCK_STATE,
    phase,
    sunAltitudeDeg: round1(sunAltitude(now)),
    moonAltitudeDeg: round1(moonAltitude(now)),
    camera: { id: config.camera.id, name: 'Simulatore ASCOM', kind: 'sim', connected },
    lastFrame: {
      id: iso(frameMs),
      timestamp: iso(frameMs),
      exposureMs: day ? config.camera.dayExposureMs : 8000,
      gain: config.camera.gain,
      medianAdu: day ? 28640 + wobble * 20 : 4210 + wobble,
    },
    disk: { freeGb: 212.4, usedGb: 8.1 },
    uptimeS: Math.round((now - STARTED) / 1000) + 5321,
    error:
      MOCK_STATE === 'error'
        ? 'La camera non risponde. Controlla il cavo USB o riavvia il simulatore ASCOM.'
        : null,
    jobs: activeJobs(),
  };
}

// ----- Validazione della configurazione (essenziale, come da contratto) -----

function validateConfig(body) {
  if (!body || typeof body !== 'object' || Array.isArray(body)) {
    return [{ path: '', message: 'La configurazione deve essere un oggetto JSON.' }];
  }
  const errors = [];
  if (body.schemaVersion !== 1) {
    errors.push({ path: 'schemaVersion', message: 'Versione dello schema non supportata.' });
  }
  if (!['it', 'en'].includes(body.language)) {
    errors.push({ path: 'language', message: 'Scegli italiano o inglese.' });
  }
  if (!['auto', 'day', 'night', 'red'].includes(body.ui?.theme)) {
    errors.push({ path: 'ui.theme', message: 'Tema non riconosciuto: usa auto, day, night o red.' });
  }
  const lat = body.site?.latitude;
  if (typeof lat !== 'number' || lat < -90 || lat > 90) {
    errors.push({ path: 'site.latitude', message: 'La latitudine va da -90 a 90.' });
  }
  const lon = body.site?.longitude;
  if (typeof lon !== 'number' || lon < -180 || lon > 180) {
    errors.push({ path: 'site.longitude', message: 'La longitudine va da -180 a 180.' });
  }
  return errors;
}

// ----- Risposte HTTP -----

function sendJson(res, code, body) {
  const data = JSON.stringify(body);
  res.writeHead(code, {
    'Content-Type': 'application/json; charset=utf-8',
    'Content-Length': Buffer.byteLength(data),
    'Cache-Control': 'no-store',
  });
  res.end(data);
}

// Invio di un file binario con supporto alle richieste Range (serve al <video> per lo spostamento).
function sendBuffer(req, res, data, type, cache) {
  const range = req.headers.range;
  const m = range ? /^bytes=(\d*)-(\d*)$/.exec(range) : null;
  if (m && (m[1] !== '' || m[2] !== '')) {
    let start;
    let end;
    if (m[1] === '') {
      start = Math.max(0, data.length - Number(m[2]));
      end = data.length - 1;
    } else {
      start = Number(m[1]);
      end = m[2] === '' ? data.length - 1 : Math.min(Number(m[2]), data.length - 1);
    }
    if (start > end || start >= data.length) {
      res.writeHead(416, { 'Content-Range': `bytes */${data.length}` });
      return res.end();
    }
    res.writeHead(206, {
      'Content-Type': type,
      'Content-Length': end - start + 1,
      'Content-Range': `bytes ${start}-${end}/${data.length}`,
      'Accept-Ranges': 'bytes',
      'Cache-Control': cache,
    });
    return res.end(data.subarray(start, end + 1));
  }
  res.writeHead(200, {
    'Content-Type': type,
    'Content-Length': data.length,
    'Accept-Ranges': 'bytes',
    'Cache-Control': cache,
  });
  res.end(data);
}

function sendSvg(res, name, cache) {
  const data = asset(name);
  res.writeHead(200, {
    'Content-Type': 'image/svg+xml',
    'Content-Length': data.length,
    'Cache-Control': cache,
  });
  res.end(data);
}

// ----- Colore di prova (solo per la schermata Colore: NON è elaborazione reale) -----
// stage=raw  -> variante frame-raw-*.svg: più verde, e di notte con il velo dell'inquinamento luminoso.
// senza stage -> il frame di prova elaborato, ricalcolato da config.color a ogni PUT /api/config:
//   whiteBalance manual: moltiplica R,G,B per manualGains (auto: correzione fissa solo sulle notti);
//   saturation: saturazione SVG; smoothing: sfocatura leggera (stand-in visivo, nel timelapse vero
//   è la stabilità fra un frame e l'altro, che un singolo frame non mostra);
//   gradientRemoval: il velo dell'orizzonte resta a 0.7 * (1 - gradientRemoval).
const processedCache = new Map();

function sendSvgText(res, text, cache) {
  const data = Buffer.from(text, 'utf8');
  res.writeHead(200, {
    'Content-Type': 'image/svg+xml',
    'Content-Length': data.length,
    'Cache-Control': cache,
  });
  res.end(data);
}

function colorParams() {
  const c = config.color || {};
  const num = (v, d, lo, hi) => (Number.isFinite(v) ? Math.min(hi, Math.max(lo, v)) : d);
  const gains = Array.isArray(c.manualGains) ? c.manualGains : [];
  return {
    manual: c.whiteBalance === 'manual',
    gains: [0, 1, 2].map((i) => num(gains[i], 1, 0.5, 2)),
    smoothing: num(c.smoothing, 0.85, 0, 1),
    gradient: num(c.gradientRemoval, 0.5, 0, 1),
    saturation: num(c.saturation, 1.1, 0, 2),
  };
}

function processedSvg(name) {
  const p = colorParams();
  const key = `${name}|${JSON.stringify(p)}`;
  if (processedCache.has(key)) return processedCache.get(key);

  const src = asset(name).toString('utf8');
  const start = src.indexOf('>') + 1;
  const end = src.lastIndexOf('</svg>');
  const night = name.startsWith('frame-night');
  // Automatico: le notti di prova hanno una dominante blu; una correzione fissa la porta vicino al neutro.
  const auto = night ? [1.25, 1, 0.7] : [1, 1, 1];
  const g = p.manual ? p.gains : auto;
  const defs =
    `<defs><filter id="mock-color" color-interpolation-filters="sRGB">` +
    `<feGaussianBlur stdDeviation="${(p.smoothing * 1.5).toFixed(2)}"/>` +
    `<feColorMatrix type="saturate" values="${p.saturation}"/>` +
    `<feColorMatrix type="matrix" values="${g[0]} 0 0 0 0  0 ${g[1]} 0 0 0  0 0 ${g[2]} 0 0  0 0 0 1 0"/>` +
    `</filter>` +
    (night
      ? '<radialGradient id="mock-glow" cx="50%" cy="100%" r="75%">' +
        '<stop offset="0%" stop-color="#d6e3a6" stop-opacity="0.9"/>' +
        '<stop offset="45%" stop-color="#b9c98c" stop-opacity="0.35"/>' +
        '<stop offset="100%" stop-color="#b9c98c" stop-opacity="0"/></radialGradient>'
      : '') +
    '</defs>';
  const glow = night
    ? `<circle cx="500" cy="500" r="480" fill="url(#mock-glow)" opacity="${(0.7 * (1 - p.gradient)).toFixed(3)}"/>`
    : '';
  const out = `${src.slice(0, start)}${defs}<g filter="url(#mock-color)">${src.slice(start, end)}${glow}</g>${src.slice(end)}`;

  if (processedCache.size > 50) processedCache.clear();
  processedCache.set(key, out);
  return out;
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    let size = 0;
    const chunks = [];
    req.on('data', (chunk) => {
      size += chunk.length;
      if (size > 1_000_000) {
        reject(new Error('too large'));
        req.destroy();
        return;
      }
      chunks.push(chunk);
    });
    req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    req.on('error', reject);
  });
}

// ----- Eventi (SSE): status ogni 5 s, frame quando ne arriva uno nuovo -----

function handleEvents(req, res) {
  res.writeHead(200, {
    'Content-Type': 'text/event-stream; charset=utf-8',
    'Cache-Control': 'no-cache',
    Connection: 'keep-alive',
  });
  res.write('retry: 3000\n\n');

  let lastFrameId = null;
  const sendStatus = () => res.write(`event: status\ndata: ${JSON.stringify(status())}\n\n`);
  const checkFrame = () => {
    const s = status();
    if (s.lastFrame.id !== lastFrameId) {
      lastFrameId = s.lastFrame.id;
      const payload = { id: s.lastFrame.id, timestamp: s.lastFrame.timestamp };
      res.write(`event: frame\ndata: ${JSON.stringify(payload)}\n\n`);
    }
  };

  sendStatus();
  checkFrame();
  let tick = 0;
  const timer = setInterval(() => {
    tick += 1;
    checkFrame();
    if (tick % 5 === 0) sendStatus();
  }, 1000);

  req.on('close', () => clearInterval(timer));
}

// ----- Router dell'API -----

function nightExists(id) {
  return nightsList().some((n) => n.id === id);
}

function framesPage(url, nightId) {
  const limit = Math.min(1000, Math.max(1, Number(url.searchParams.get('limit')) || 200));
  const offset = Math.max(0, Number(url.searchParams.get('offset')) || 0);
  const all = framesFor(nightId);
  return all.slice(offset, offset + limit).map((f) => ({ id: f.id, timestamp: f.timestamp, phase: f.phase }));
}

async function handleApi(req, res, url) {
  const p = url.pathname;
  const method = req.method;
  let m;

  if (p === '/api/status' && method === 'GET') return sendJson(res, 200, status());
  if (p === '/api/events' && method === 'GET') return handleEvents(req, res);

  if (p === '/api/config') {
    if (method === 'GET') return sendJson(res, 200, config);
    if (method === 'PUT') {
      let body;
      try {
        body = JSON.parse(await readBody(req));
      } catch {
        return sendJson(res, 400, { error: 'La configurazione non è un JSON valido. Controlla le modifiche e riprova.' });
      }
      const errors = validateConfig(body);
      if (errors.length) return sendJson(res, 200, { ok: false, errors });
      config = body;
      processedCache.clear(); // il frame di prova elaborato si ricalcola con i nuovi valori di color
      return sendJson(res, 200, { ok: true, errors: [] });
    }
  }

  if (p === '/api/cameras/discover' && method === 'GET') {
    return sendJson(res, 200, [
      { id: 'sim-1', name: 'Simulatore ASCOM', kind: 'sim', detail: 'ASCOM.Simulator.Camera' },
      { id: 'alpaca-1', name: 'Camera ASCOM Alpaca in rete', kind: 'ascom-alpaca', detail: '192.168.1.40:11111' },
    ]);
  }

  if (p === '/api/camera/connect' && method === 'POST') {
    let body = {};
    try {
      body = JSON.parse((await readBody(req)) || '{}');
    } catch {
      return sendJson(res, 400, { error: 'Richiesta non valida. Scegli una camera dall\'elenco e riprova.' });
    }
    if (body.id === 'sim-1') return sendJson(res, 200, { ok: true });
    return sendJson(res, 200, {
      ok: false,
      error: 'Non trovo questa camera. Controlla che sia accesa e in rete, poi riprova.',
    });
  }

  if (p === '/api/frame/latest.jpg' && method === 'GET') {
    const s = status();
    const name = frameAssetFor(s.phase);
    if (url.searchParams.get('stage') === 'raw') {
      return sendSvg(res, name.replace(/^frame-/, 'frame-raw-'), 'no-store');
    }
    return sendSvgText(res, processedSvg(name), 'no-store');
  }

  if (p === '/api/nights' && method === 'GET') return sendJson(res, 200, nightsList());

  if ((m = p.match(/^\/api\/nights\/([^/]+)\/frames$/)) && method === 'GET') {
    const id = decodeURIComponent(m[1]);
    if (!nightExists(id)) return sendJson(res, 404, { error: `Notte ${id} non trovata. Scegli un'altra notte dall'elenco.` });
    return sendJson(res, 200, framesPage(url, id));
  }

  if ((m = p.match(/^\/api\/nights\/([^/]+)\/(?:frames|thumb)\/([^/]+)\.jpg$/)) && method === 'GET') {
    const nightId = decodeURIComponent(m[1]);
    const frameId = decodeURIComponent(m[2]);
    if (!nightExists(nightId)) return sendJson(res, 404, { error: `Notte ${nightId} non trovata.` });
    const phase = phaseAt(Date.parse(frameId));
    return sendSvg(res, frameAssetFor(phase, nightId), 'public, max-age=3600');
  }

  if ((m = p.match(/^\/api\/nights\/([^/]+)\/keogram\.jpg$/)) && method === 'GET') {
    return sendSvg(res, 'keogram.svg', 'public, max-age=3600');
  }

  if ((m = p.match(/^\/api\/nights\/([^/]+)\/startrails\.jpg$/)) && method === 'GET') {
    return sendSvg(res, 'startrails.svg', 'public, max-age=3600');
  }

  if ((m = p.match(/^\/api\/nights\/([^/]+)\/timelapse\.mp4$/)) && method === 'GET') {
    const id = decodeURIComponent(m[1]);
    if (nightExists(id) && timelapseOf(id) === 'ready') {
      return sendBuffer(req, res, asset('timelapse.mp4'), 'video/mp4', 'no-cache');
    }
    return sendJson(res, 404, {
      error: 'Il timelapse di questa notte non è ancora pronto. Avvialo dalla scheda Timelapse.',
    });
  }

  if ((m = p.match(/^\/api\/nights\/([^/]+)\/timelapse$/)) && method === 'POST') {
    const id = decodeURIComponent(m[1]);
    if (!nightExists(id)) return sendJson(res, 404, { error: `Notte ${id} non trovata.` });
    if (!timelapseJobs.has(id)) timelapseJobs.set(id, { kind: 'timelapse', nightId: id, startedAt: Date.now() });
    timelapseOverride.delete(id);
    timelapseDone.delete(id);
    nightsCache = { bucket: -1, list: null };
    return sendJson(res, 200, { ok: true });
  }

  // Solo per le prove (vedi intestazione del file).
  if (p === '/api/__mock/timelapse' && method === 'POST') {
    let body = {};
    try {
      body = JSON.parse((await readBody(req)) || '{}');
    } catch {
      return sendJson(res, 400, { error: 'Richiesta di prova non valida.' });
    }
    const { nightId, state } = body;
    if (!nightExists(nightId)) return sendJson(res, 404, { error: `Notte ${nightId} non trovata.` });
    if (state === null || state === undefined) {
      timelapseOverride.delete(nightId);
    } else if (['none', 'ready', 'failed', 'generating'].includes(state)) {
      timelapseOverride.set(nightId, state);
    } else {
      return sendJson(res, 400, { error: 'Stato di prova non riconosciuto.' });
    }
    nightsCache = { bucket: -1, list: null };
    return sendJson(res, 200, { ok: true, state: timelapseOf(nightId) });
  }

  if (p === '/api/__mock/reset' && method === 'POST') {
    deletedNights.clear();
    timelapseOverride.clear();
    timelapseJobs.clear();
    timelapseDone.clear();
    nightsCache = { bucket: -1, list: null };
    return sendJson(res, 200, { ok: true });
  }

  if ((m = p.match(/^\/api\/nights\/([^/]+)$/)) && method === 'DELETE') {
    const id = decodeURIComponent(m[1]);
    if (!nightExists(id)) return sendJson(res, 404, { error: `Notte ${id} non trovata.` });
    deletedNights.add(id);
    nightsCache = { bucket: -1, list: null };
    return sendJson(res, 200, { ok: true });
  }

  return sendJson(res, 404, { error: 'Percorso non previsto dal motore di prova.' });
}

// ----- File statici di web/ -----

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.ico': 'image/x-icon',
};

function serveStatic(req, res, pathname) {
  if (req.method !== 'GET' && req.method !== 'HEAD') {
    res.writeHead(405, { Allow: 'GET, HEAD' });
    return res.end();
  }
  let rel;
  try {
    rel = decodeURIComponent(pathname);
  } catch {
    res.writeHead(400);
    return res.end();
  }
  if (rel === '/') rel = '/index.html';

  const file = path.normalize(path.join(WEB_ROOT, rel));
  const inside = file.startsWith(WEB_ROOT + path.sep);
  const isMock = file.startsWith(HERE + path.sep);
  if (!inside || isMock) {
    res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
    return res.end('Non trovato');
  }

  fs.stat(file, (err, stat) => {
    if (err || !stat.isFile()) {
      res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
      return res.end('Non trovato');
    }
    res.writeHead(200, {
      'Content-Type': MIME[path.extname(file)] || 'application/octet-stream',
      'Content-Length': stat.size,
      'Cache-Control': 'no-cache',
    });
    if (req.method === 'HEAD') return res.end();
    fs.createReadStream(file).pipe(res);
  });
}

// ----- Server -----

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, `http://${HOST}:${PORT}`);
  try {
    if (url.pathname.startsWith('/api/') || url.pathname === '/api') {
      await handleApi(req, res, url);
    } else {
      serveStatic(req, res, url.pathname);
    }
  } catch (err) {
    console.error(err);
    if (!res.headersSent) {
      sendJson(res, 500, { error: 'Errore interno del motore di prova. Guarda il terminale per i dettagli.' });
    } else {
      res.end();
    }
  }
});

// Verifica subito che le immagini di prova ci siano.
for (const name of ['frame-day.svg', 'frame-dusk.svg', 'frame-night-a.svg', 'frame-night-b.svg', 'keogram.svg', 'startrails.svg']) {
  asset(name);
}

server.listen(PORT, HOST, () => {
  console.log(`Motore di prova in ascolto su http://${HOST}:${PORT}  (stato: ${MOCK_STATE})`);
});
