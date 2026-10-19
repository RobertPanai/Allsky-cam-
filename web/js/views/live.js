// Schermata Live: vista circolare del cielo, stato in una frase, dati chiave e nastro della notte.
// Il nastro è un dato: il colore segue l'altezza reale del Sole (dalle coordinate del sito).
import { api, ApiError } from '../api.js';
import { t, fmt, applyI18n, errorMessage } from '../i18n.js';
import { currentNightId, localParts, nightWindow, sampleBands, sunAltitude, sunCurve, sunCrossings } from '../time.js';

const PHASES = ['day', 'twilight', 'night'];
const STATES = ['capturing', 'idle', 'error'];
const SCALE_TICKS = [0, 25, 50, 75, 100];
const FRAMES_PAGE = 1000;
const FRAMES_MAX = 20000;
const FRAME_DEBOUNCE_MS = 400;
const HOUR_MS = 3600000;

// Soglie dell'altezza del Sole: orizzonte, crepuscolo civile, nautico, astronomico.
const SUN_STEP_MIN = 5;
const SUN_THRESHOLDS = [0, -6, -12, -18];
// Scala verticale della curva del Sole nel riquadro 1000x100: da +40 gradi (in alto) a -60 gradi (in basso).
const SUN_TOP = 40;
const SUN_BOTTOM = -60;
// Colori di riserva, usati solo se un token non è leggibile (i token sono in forma #rrggbb).
const FALLBACK_RGB = { day: [61, 91, 143], twilight: [140, 102, 121], night: [26, 39, 80] };

// Anello del cielo, in un viewBox 480x480. Nord in alto, Est a sinistra (vista dal basso).
const RING_C = 240;

function polar(radius, deg) {
  const rad = (deg * Math.PI) / 180;
  return [RING_C + radius * Math.sin(rad), RING_C - radius * Math.cos(rad)];
}

function ringMarkup() {
  let ticks = '';
  for (let deg = 0; deg < 360; deg += 5) {
    const major = deg % 30 === 0;
    const [x1, y1] = polar(major ? 196 : 205, deg);
    const [x2, y2] = polar(212, deg);
    const cls = major ? 'tick-major' : 'tick-minor';
    ticks += `<line class="${cls}" x1="${x1.toFixed(2)}" y1="${y1.toFixed(2)}" x2="${x2.toFixed(2)}" y2="${y2.toFixed(2)}"/>`;
  }
  const letters = [
    ['N', 0],
    ['E', 270],
    ['S', 180],
    ['O', 90],
  ]
    .map(([letter, deg]) => {
      const [x, y] = polar(184, deg);
      return `<text x="${x.toFixed(2)}" y="${y.toFixed(2)}" text-anchor="middle" dominant-baseline="central">${letter}</text>`;
    })
    .join('');
  return `<svg class="sky-ring" viewBox="0 0 480 480" aria-hidden="true" focusable="false">` +
    `<circle class="rim" cx="${RING_C}" cy="${RING_C}" r="212"/>${ticks}${letters}</svg>`;
}

function template() {
  return `
    <section class="live" aria-labelledby="live-title">
      <header class="live-head">
        <h1 id="live-title" class="display" tabindex="-1" data-role="title"></h1>
        <p class="lede" data-role="lede"></p>
        <p class="camera-status" data-role="camera"></p>
      </header>

      <div class="notice" data-kind="error" data-role="problem" role="alert" hidden>
        <p data-role="problemText"></p>
        <button type="button" class="btn" data-role="toCamera" data-i18n="live.problem.action">Vai a Camera</button>
      </div>

      <div class="notice" data-role="banner" role="alert" hidden>
        <p data-role="bannerText"></p>
        <button type="button" class="btn" data-role="retry" data-i18n="app.retry">Riprova</button>
      </div>

      <div class="live-main">
        <figure class="sky">
          <div class="sky-dome">
            <img class="sky-img" data-role="skyImg" alt="" hidden>
            ${ringMarkup()}
            <p class="sky-empty" data-role="skyEmpty" hidden></p>
          </div>
          <figcaption class="sky-caption" data-role="skyCaption"></figcaption>
        </figure>

        <dl class="readouts">
          <div class="readout">
            <dt data-i18n="live.readouts.exposure">Esposizione</dt>
            <dd data-role="exposure" class="num">–</dd>
          </div>
          <div class="readout">
            <dt data-i18n="live.readouts.gain">Guadagno</dt>
            <dd data-role="gain" class="num">–</dd>
          </div>
          <div class="readout">
            <dt data-i18n="live.readouts.median">Mediana ADU</dt>
            <dd data-role="median" class="num">–</dd>
          </div>
          <div class="readout">
            <dt data-i18n="live.readouts.disk">Disco libero</dt>
            <dd data-role="disk" class="num">–</dd>
          </div>
        </dl>
      </div>

      <section class="ribbon" aria-labelledby="ribbon-title">
        <div class="ribbon-head">
          <h2 id="ribbon-title" class="ribbon-title" data-role="ribbonTitle"></h2>
          <p class="ribbon-note" data-role="ribbonNote" hidden></p>
        </div>

        <div class="ribbon-track" role="slider" tabindex="0"
             data-role="track" data-i18n-aria="live.ribbon.label"
             aria-valuemin="0" aria-valuemax="0" aria-valuenow="0" aria-disabled="true">
          <div class="ribbon-plot" data-role="plot">
            <div class="ribbon-bands" data-role="bands"></div>
            <svg class="ribbon-curve" data-role="curve" viewBox="0 0 1000 100" preserveAspectRatio="none"
                 aria-hidden="true" focusable="false"></svg>
          </div>
          <div class="ribbon-cursor" data-role="cursor" hidden></div>
        </div>
        <div class="ribbon-axis" data-role="axis" aria-hidden="true"></div>

        <ul class="ribbon-key">
          <li data-i18n="live.key.day">Giorno: Sole sopra l'orizzonte</li>
          <li data-i18n="live.key.civil">Crepuscolo civile: da 0° a −6°</li>
          <li data-i18n="live.key.nautical">Crepuscolo nautico: da −6° a −12°</li>
          <li data-i18n="live.key.astronomical">Crepuscolo astronomico: da −12° a −18°</li>
          <li data-i18n="live.key.night">Notte: Sole sotto −18°</li>
          <li data-i18n="live.key.curve">La linea è l'altezza del Sole; la linea sottile orizzontale è l'orizzonte (0°).</li>
          <li class="ribbon-key-lines" data-i18n="live.key.lines">Le linee tratteggiate segnano i passaggi di fase.</li>
        </ul>

        <div class="ribbon-foot">
          <p class="ribbon-time num" data-role="time"></p>
          <p class="ribbon-hint" data-role="hint" data-i18n="live.ribbon.hint"></p>
          <button type="button" class="btn" data-role="latest" data-i18n="live.ribbon.back" hidden>Torna all'ultimo frame</button>
        </div>
        <p class="empty" data-role="empty" hidden></p>
      </section>
    </section>
  `;
}

function clamp(v, lo, hi) {
  return Math.min(hi, Math.max(lo, v));
}

function normalizeFrame(f) {
  return { id: f.id, ts: Date.parse(f.timestamp), phase: f.phase };
}

function parseBands(list) {
  return list
    .filter((b) => PHASES.includes(b.phase))
    .map((b) => ({ phase: b.phase, from: new Date(b.from), to: new Date(b.to) }))
    .filter((b) => !Number.isNaN(b.from.getTime()) && !Number.isNaN(b.to.getTime()));
}

// Coordinate del sito, se valide: senza, il nastro usa la fascia di esempio o le fasi del motore.
function siteCoords(site) {
  if (typeof site?.latitude !== 'number' || typeof site?.longitude !== 'number') return null;
  const { latitude: lat, longitude: lon } = site;
  if (!Number.isFinite(lat) || !Number.isFinite(lon) || Math.abs(lat) > 90 || Math.abs(lon) > 180) return null;
  return { lat, lon };
}

// ----- Colori del nastro, letti dai token del tema corrente -----

function hexToRgb(value) {
  const m = /^#([0-9a-f]{6})$/i.exec(String(value).trim());
  if (!m) return null;
  const n = parseInt(m[1], 16);
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}

function mixRgb(a, b, w) {
  return a.map((v, i) => v + (b[i] - v) * w);
}

function cssColor(rgb) {
  return `rgb(${rgb.map((v) => Math.round(v)).join(',')})`;
}

// Rampa del colore in funzione dell'altezza: giorno sopra 0 gradi, poi transizione morbida fino a notte a -18.
function readRamp(node) {
  const cs = getComputedStyle(node);
  const pick = (name, fallback) => hexToRgb(cs.getPropertyValue(name)) || fallback;
  const day = pick('--phase-day', FALLBACK_RGB.day);
  const twilight = pick('--phase-twilight', FALLBACK_RGB.twilight);
  const night = pick('--phase-night', FALLBACK_RGB.night);
  const stops = [
    [0, day],
    [-6, twilight],
    [-12, mixRgb(twilight, night, 0.5)],
    [-18, night],
  ];
  return {
    day,
    twilight,
    night,
    at(alt) {
      if (alt >= stops[0][0]) return stops[0][1];
      for (let i = 0; i < stops.length - 1; i++) {
        const [hi, cHi] = stops[i];
        const [lo, cLo] = stops[i + 1];
        if (alt >= lo) return mixRgb(cHi, cLo, (hi - alt) / (hi - lo));
      }
      return stops[stops.length - 1][1];
    },
  };
}

function sunY(alt) {
  return ((SUN_TOP - clamp(alt, SUN_BOTTOM, SUN_TOP)) / (SUN_TOP - SUN_BOTTOM)) * 100;
}

export function mount(root, ctx) {
  const site = ctx.config?.site;
  const tz = site?.timezone || Intl.DateTimeFormat().resolvedOptions().timeZone;
  root.innerHTML = template();
  applyI18n(root);

  const el = {};
  root.querySelectorAll('[data-role]').forEach((node) => {
    el[node.dataset.role] = node;
  });
  el.skyImg.alt = t('live.sky.alt');

  const s = {
    alive: true,
    status: ctx.getStatus(),
    site: siteCoords(site),
    loaded: false,
    nightId: null,
    window: null,
    nightKnown: false,
    sample: false,
    curve: null, // [{ ms, alt }] con il Sole reale, oppure null
    bands: [], // fasi del motore o fascia di esempio, usate senza coordinate
    frames: [],
    picked: null, // indice dentro frames, oppure null per "ultimo frame"
    token: 0,
    skyEntered: false,
    plotDrawn: false,
    dragging: false,
    appendTimer: null,
  };

  const timeText = (ms, seconds = true) => (Number.isFinite(ms) ? fmt.time(new Date(ms), tz, { seconds }) : '–');

  // ----- Stato in alto, problemi e dati chiave -----

  function renderStatus() {
    const st = s.status;
    if (!st) {
      el.title.textContent = t('app.loading');
      el.lede.textContent = t('live.ledeLoading');
      el.camera.textContent = t('live.camera.loading');
      el.problem.hidden = true;
      return;
    }

    const phase = PHASES.includes(st.phase) ? st.phase : 'night';
    const state = STATES.includes(st.state) ? st.state : 'idle';
    el.title.textContent = state === 'idle'
      ? t('live.titleIdle', { phase: t(`phase.${phase}`) })
      : t('live.title', { phase: t(`phase.${phase}`), state: t(`state.${state}`) });

    const frame = st.lastFrame;
    if (frame?.timestamp) {
      el.lede.textContent = t('live.lede', { time: timeText(Date.parse(frame.timestamp)) });
    } else {
      el.lede.textContent = state === 'idle' ? t('live.ledeIdle') : t('live.ledeNoFrame');
    }

    el.exposure.textContent = frame && Number.isFinite(frame.exposureMs) ? formatExposure(frame.exposureMs) : '–';
    el.gain.textContent = frame && Number.isFinite(frame.gain) ? fmt.number(frame.gain) : '–';
    el.median.textContent = frame && Number.isFinite(frame.medianAdu) ? fmt.number(frame.medianAdu, { grouping: false }) : '–';
    el.disk.textContent = st.disk && Number.isFinite(st.disk.freeGb)
      ? t('live.units.gb', { n: fmt.number(st.disk.freeGb, { digits: 1 }) })
      : '–';
    renderCamera(st.camera);
    renderProblem(st);
  }

  function formatExposure(ms) {
    if (ms >= 1000) return t('live.units.s', { n: fmt.number(ms / 1000, { digits: ms % 1000 === 0 ? 0 : 1 }) });
    return t('live.units.ms', { n: fmt.number(ms) });
  }

  function renderCamera(cam) {
    el.camera.replaceChildren();
    if (!cam) {
      el.camera.textContent = t('live.camera.unknown');
      return;
    }
    const dot = document.createElement('span');
    dot.className = cam.connected ? 'dot' : 'dot is-off';
    dot.setAttribute('aria-hidden', 'true');
    const text = document.createElement('span');
    text.textContent = t(cam.connected ? 'live.camera.connected' : 'live.camera.disconnected', { name: cam.name || '' });
    el.camera.append(dot, text);
  }

  // Avviso con cosa è successo, cosa fare e un'azione verso la sezione Camera.
  function renderProblem(st) {
    const disconnected = st.camera ? st.camera.connected === false : false;
    const problem = st.state === 'error' || Boolean(st.error) || disconnected;
    el.problem.hidden = !problem;
    if (!problem) return;
    el.problemText.textContent = st.error || t(disconnected ? 'live.problem.disconnected' : 'live.problem.error');
  }

  el.toCamera.addEventListener('click', () => {
    location.hash = '#/camera';
  });

  // ----- Vista del cielo -----

  function renderSky() {
    const st = s.status;
    let src = '';
    let caption = '';

    const picked = s.picked !== null ? s.frames[s.picked] : null;
    if (picked) {
      src = api.urls.nightFrame(s.nightId, picked.id);
      caption = t('live.sky.picked', { time: timeText(picked.ts) });
    } else if (st?.lastFrame?.id) {
      src = api.urls.latestFrame(st.lastFrame.id);
      caption = t('live.sky.latest', { time: timeText(Date.parse(st.lastFrame.timestamp || st.lastFrame.id)) });
    }

    // La didascalia serve solo quando si sceglie un frame: l'ora dell'ultimo è già nel nastro e in alto.
    el.skyCaption.textContent = picked ? caption : '';
    el.skyCaption.hidden = !picked;

    if (!src) {
      el.skyImg.hidden = true;
      el.skyEmpty.hidden = false;
      el.skyEmpty.textContent = !st
        ? t('live.sky.loading')
        : st.state === 'idle' ? t('live.sky.idle') : t('live.sky.empty');
      return;
    }

    if (!s.skyEntered) {
      s.skyEntered = true;
      el.skyImg.classList.add('is-entering');
    }
    if (el.skyImg.dataset.src !== src) {
      el.skyImg.dataset.src = src;
      el.skyImg.src = src;
    }
    el.skyImg.hidden = false;
    el.skyEmpty.hidden = true;
  }

  el.skyImg.addEventListener('error', () => {
    el.skyImg.hidden = true;
    el.skyEmpty.hidden = false;
    el.skyEmpty.textContent = t('live.sky.broken');
  });

  el.skyImg.addEventListener('load', () => {
    el.skyEmpty.hidden = true;
  });

  // ----- Nastro della notte -----

  function pctOf(ms) {
    const span = s.window.end - s.window.start;
    return clamp(((ms - s.window.start) / span) * 100, 0, 100);
  }

  function sunDegAt(ms) {
    return s.site ? sunAltitude(new Date(ms), s.site.lat, s.site.lon) : null;
  }

  function renderRibbonText() {
    if (!s.window) {
      el.ribbonTitle.textContent = t('app.loading');
      return;
    }
    el.ribbonTitle.textContent = t('live.ribbon.title', { date: fmt.date(s.window.start, tz) });
    el.ribbonNote.hidden = !s.sample;
    el.ribbonNote.textContent = s.sample ? t(s.site ? 'live.ribbon.sample' : 'live.ribbon.sampleNoSite') : '';
  }

  // Disegna la fascia (gradiente), la curva del Sole, i passaggi di fase e le tacche orarie.
  function renderBands() {
    if (!s.window) return;
    const ramp = readRamp(el.bands);
    const { start } = s.window;
    const span = s.window.end.getTime() - start.getTime();
    const pct = (ms) => ((ms - start.getTime()) / span) * 100;

    if (s.curve) {
      const stops = s.curve.map((p) => `${cssColor(ramp.at(p.alt))} ${pct(p.ms).toFixed(3)}%`);
      el.bands.style.background = `linear-gradient(to right, ${stops.join(', ')})`;
      el.curve.innerHTML = sunMarkup(pct);
    } else {
      // Senza coordinate: fasi a gradini (fascia di esempio o fasi del motore).
      const color = { day: ramp.day, twilight: ramp.twilight, night: ramp.night };
      const stops = s.bands
        .map((b) => {
          const c = cssColor(color[b.phase] || ramp.night);
          return `${c} ${pct(b.from.getTime()).toFixed(3)}%, ${c} ${pct(b.to.getTime()).toFixed(3)}%`;
        })
        .join(', ');
      el.bands.style.background = stops ? `linear-gradient(to right, ${stops})` : '';
      el.curve.innerHTML = '';
    }

    if (!s.plotDrawn) {
      s.plotDrawn = true;
      el.plot.classList.add('is-drawing');
    }
  }

  // Curva del Sole, passaggi di fase e tacche orarie, in un riquadro 1000x100 (stroke non scalati).
  function sunMarkup(pct) {
    const curve = s.curve;
    const x = (ms) => (pct(ms) * 10).toFixed(1);
    const d = curve
      .map((p, i) => `${i ? 'L' : 'M'}${x(p.ms)} ${sunY(p.alt).toFixed(2)}`)
      .join('');

    let out = '';
    // Passaggi di fase: tratteggio con alone, leggibile anche senza colore.
    for (const c of sunCrossings(curve, SUN_THRESHOLDS)) {
      const px = x(c.ms);
      out += `<line class="phase-halo" x1="${px}" y1="0" x2="${px}" y2="100"/>`;
      out += `<line class="phase-line" x1="${px}" y1="0" x2="${px}" y2="100"/>`;
    }

    const horizon = sunY(0).toFixed(2);
    out += `<line class="horizon" x1="0" y1="${horizon}" x2="1000" y2="${horizon}"/>`;
    out += `<path class="sun-halo" d="${d}"/><path class="sun-line" d="${d}"/>`;
    return out;
  }

  // Asse: tacche orarie sotto la fascia (sul fondo della pagina, non dentro i colori) e le etichette.
  function renderAxis() {
    if (!s.window) {
      el.axis.replaceChildren();
      return;
    }
    const { start, end } = s.window;
    const span = end.getTime() - start.getTime();
    el.axis.replaceChildren(
      ...SCALE_TICKS.map((p) => {
        const tick = document.createElement('span');
        tick.className = 'tick num';
        if (p === 0) tick.dataset.edge = 'start';
        if (p === 100) tick.dataset.edge = 'end';
        tick.style.left = `${p}%`;
        tick.textContent = timeText(start.getTime() + (span * p) / 100, false);
        return tick;
      }),
    );

    let marks = '';
    for (let ms = start.getTime() + HOUR_MS; ms < end.getTime(); ms += HOUR_MS) {
      const x = (((ms - start.getTime()) / span) * 1000).toFixed(1);
      const len = localParts(new Date(ms), tz).hour % 6 === 0 ? 8 : 4;
      marks += `<line x1="${x}" y1="0" x2="${x}" y2="${len}"/>`;
    }
    el.axis.insertAdjacentHTML(
      'afterbegin',
      `<svg class="ribbon-ticks" viewBox="0 0 1000 8" preserveAspectRatio="none" aria-hidden="true" focusable="false">${marks}</svg>`,
    );
  }

  function nearestFrameIndex(ms) {
    const list = s.frames;
    let lo = 0;
    let hi = list.length - 1;
    while (lo < hi) {
      const mid = (lo + hi) >> 1;
      if (list[mid].ts < ms) lo = mid + 1;
      else hi = mid;
    }
    if (lo > 0 && Math.abs(list[lo - 1].ts - ms) <= Math.abs(list[lo].ts - ms)) return lo - 1;
    return lo;
  }

  function setPicked(index) {
    const last = s.frames.length - 1;
    s.picked = index === null || index >= last ? null : index;
    renderSky();
    renderCursor();
  }

  function renderCursor() {
    const count = s.frames.length;
    const hasFrames = count > 0;
    const last = count - 1;
    const current = s.picked ?? last;
    const frame = hasFrames ? s.frames[current] : null;

    el.track.setAttribute('aria-disabled', hasFrames ? 'false' : 'true');
    el.track.setAttribute('aria-valuemax', String(Math.max(last, 0)));
    el.track.setAttribute('aria-valuenow', String(Math.max(current, 0)));

    if (!frame) {
      el.cursor.hidden = true;
      el.track.removeAttribute('aria-valuetext');
      el.time.textContent = '';
      el.hint.hidden = true;
      el.latest.hidden = true;
      el.empty.hidden = !s.loaded || hasFrames;
      el.empty.textContent = t('live.ribbon.empty');
      return;
    }

    el.cursor.hidden = false;
    el.cursor.style.left = `${pctOf(frame.ts)}%`;
    el.track.setAttribute('aria-valuetext', timeText(frame.ts));
    el.empty.hidden = true;
    el.hint.hidden = false;

    const picked = s.picked !== null;
    const label = picked
      ? t('live.ribbon.picked', { time: timeText(frame.ts) })
      : t('live.ribbon.latest', { time: timeText(frame.ts) });
    const sun = sunDegAt(frame.ts);
    el.time.textContent = sun === null
      ? label
      : `${label} · ${t('live.ribbon.sun', { deg: t('live.units.deg', { n: fmt.signed(sun, 1) }) })}`;
    el.latest.hidden = !picked;
  }

  function showBanner(message) {
    el.banner.hidden = !message;
    el.bannerText.textContent = message || '';
  }

  async function fetchAllFrames(nightId) {
    const out = [];
    for (let offset = 0; offset < FRAMES_MAX; offset += FRAMES_PAGE) {
      const page = await api.frames(nightId, { limit: FRAMES_PAGE, offset });
      if (!Array.isArray(page)) throw new ApiError('parse');
      for (const f of page) out.push(normalizeFrame(f));
      if (page.length < FRAMES_PAGE) break;
    }
    return out.filter((f) => Number.isFinite(f.ts));
  }

  async function reloadNight() {
    const token = ++s.token;
    const nightId = currentNightId(new Date(), tz);
    let nights = [];
    let message = null;

    try {
      nights = await api.nights();
    } catch (err) {
      message = errorMessage(err);
    }
    if (!s.alive || token !== s.token) return;

    const entry = Array.isArray(nights) ? nights.find((n) => n.id === nightId) ?? null : null;
    let frames = [];
    if (entry) {
      try {
        frames = await fetchAllFrames(nightId);
      } catch (err) {
        message = message || errorMessage(err);
      }
      if (!s.alive || token !== s.token) return;
    }

    const hasBands = Array.isArray(entry?.sunBands) && entry.sunBands.length > 0;
    s.nightId = nightId;
    s.window = nightWindow(nightId, tz);
    s.nightKnown = Boolean(entry);
    if (s.site) {
      s.curve = sunCurve(nightId, tz, s.site.lat, s.site.lon, SUN_STEP_MIN);
      s.bands = [];
      s.sample = false;
    } else if (hasBands) {
      s.curve = null;
      s.bands = parseBands(entry.sunBands);
      s.sample = false;
    } else {
      s.curve = null;
      s.bands = sampleBands(nightId, tz);
      s.sample = true;
    }
    s.frames = frames;
    s.picked = null;
    s.loaded = true;

    renderRibbonText();
    renderBands();
    renderAxis();
    setPicked(null);
    showBanner(message);
  }

  // Aggiunge in coda i frame nuovi senza ricaricare la notte.
  async function appendNewFrames() {
    if (!s.nightKnown || !s.nightId) return reloadNight();
    const token = s.token;
    try {
      const page = await api.frames(s.nightId, { limit: FRAMES_PAGE, offset: s.frames.length });
      if (!s.alive || token !== s.token || !Array.isArray(page)) return;
      if (page.length) {
        for (const f of page) s.frames.push(normalizeFrame(f));
        renderCursor();
      }
    } catch (err) {
      showBanner(errorMessage(err));
    }
  }

  function scheduleAppend() {
    clearTimeout(s.appendTimer);
    s.appendTimer = setTimeout(appendNewFrames, FRAME_DEBOUNCE_MS);
  }

  // ----- Interazione con il nastro: puntatore e tastiera -----

  function pickFromClientX(clientX) {
    if (!s.frames.length || !s.window) return;
    const rect = el.track.getBoundingClientRect();
    const ratio = clamp((clientX - rect.left) / rect.width, 0, 1);
    const span = s.window.end.getTime() - s.window.start.getTime();
    const ms = s.window.start.getTime() + ratio * span;
    setPicked(nearestFrameIndex(ms));
  }

  const onPointerDown = (event) => {
    if (!s.frames.length) return;
    if (event.pointerType === 'mouse' && event.button !== 0) return;
    s.dragging = true;
    el.track.setPointerCapture(event.pointerId);
    el.track.focus({ preventScroll: true });
    pickFromClientX(event.clientX);
  };

  const onPointerMove = (event) => {
    if (s.dragging) pickFromClientX(event.clientX);
  };

  const onPointerEnd = () => {
    s.dragging = false;
  };

  const onKeyDown = (event) => {
    if (!s.frames.length) return;
    const last = s.frames.length - 1;
    const current = s.picked ?? last;
    let next;
    switch (event.key) {
      case 'ArrowLeft':
      case 'ArrowDown':
        next = current - 1;
        break;
      case 'ArrowRight':
      case 'ArrowUp':
        next = current + 1;
        break;
      case 'PageDown':
        next = current - 10;
        break;
      case 'PageUp':
        next = current + 10;
        break;
      case 'Home':
        next = 0;
        break;
      case 'End':
        next = last;
        break;
      case 'Escape':
        next = last;
        break;
      default:
        return;
    }
    event.preventDefault();
    setPicked(clamp(next, 0, last));
  };

  el.track.addEventListener('pointerdown', onPointerDown);
  el.track.addEventListener('pointermove', onPointerMove);
  el.track.addEventListener('pointerup', onPointerEnd);
  el.track.addEventListener('pointercancel', onPointerEnd);
  el.track.addEventListener('keydown', onKeyDown);
  el.latest.addEventListener('click', () => setPicked(null));
  el.retry.addEventListener('click', () => reloadNight());

  // ----- Eventi dal motore e cambio di tema -----

  function onStatus(status) {
    if (!s.alive) return;
    s.status = status;
    renderStatus();
    renderSky();
    if (s.loaded && currentNightId(new Date(), tz) !== s.nightId) reloadNight();
  }

  function onFrame() {
    if (!s.alive) return;
    if (!s.loaded) return;
    if (!s.nightKnown) {
      reloadNight();
      return;
    }
    scheduleAppend();
  }

  const offStatus = ctx.on('status', onStatus);
  const offFrame = ctx.on('frame', onFrame);

  // I colori del nastro vengono dai token: si ridisegnano quando cambia il tema.
  const themeObserver = new MutationObserver(() => {
    if (s.alive && s.window) renderBands();
  });
  themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });

  // Primo disegno con quello che è già noto, poi i dati della notte.
  renderStatus();
  renderSky();
  renderRibbonText();
  reloadNight();

  return {
    unmount() {
      s.alive = false;
      s.token++;
      clearTimeout(s.appendTimer);
      themeObserver.disconnect();
      offStatus();
      offFrame();
    },
  };
}
