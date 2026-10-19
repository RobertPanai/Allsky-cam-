// Schermata Colore: confronto fra frame originale ed elaborato, cursori del colore e istogramma del fondo cielo.
// Le modifiche si salvano con PUT /api/config (dopo una breve pausa); il frame elaborato si ricarica
// con un parametro di versione. Tutto il testo sta in web/i18n/*.color.json, sotto la chiave "color".
import { api, ApiError } from '../api.js';
import { t, fmt, applyI18n, errorMessage } from '../i18n.js';

// Valori consigliati: gli stessi di docs/API.md e del motore di prova.
const RECOMMENDED = {
  whiteBalance: 'auto',
  manualGains: [1, 1, 1],
  smoothing: 0.85,
  gradientRemoval: 0.5,
  stretch: 'mtf',
  saturation: 1.1,
};

const GAIN_MIN = 0.5;
const GAIN_MAX = 2;
const SAVE_DELAY_MS = 500;
const HIST_BINS = 64;
const SAMPLE = 128;
const SKY_RADIUS = 0.47; // raggio utile del cerchio del cielo, in frazione del lato

// Cursori con intervallo, passo e cifre decimali. La chiave è il campo di AppConfig.color.
const SLIDERS = [
  { key: 'smoothing', role: 'smoothing', out: 'outSmoothing', min: 0, max: 1, step: 0.01, digits: 2 },
  { key: 'gradientRemoval', role: 'gradient', out: 'outGradient', min: 0, max: 1, step: 0.01, digits: 2 },
  { key: 'saturation', role: 'saturation', out: 'outSaturation', min: 0, max: 2, step: 0.05, digits: 2 },
];

const GAINS = [
  { role: 'gainR', out: 'outR', label: 'color.balance.r' },
  { role: 'gainG', out: 'outG', label: 'color.balance.g' },
  { role: 'gainB', out: 'outB', label: 'color.balance.b' },
];

const CHANNELS = ['r', 'g', 'b'];

let viewSeq = 0;

function clamp(v, lo, hi) {
  return Math.min(hi, Math.max(lo, v));
}

function num(v, fallback) {
  return Number.isFinite(v) ? v : fallback;
}

// Copia pulita della sezione color: valori fuori intervallo o mancanti tornano ai consigliati.
function normalizeColor(c = {}) {
  const gains = Array.isArray(c.manualGains) ? c.manualGains : [];
  return {
    whiteBalance: c.whiteBalance === 'manual' ? 'manual' : 'auto',
    manualGains: [0, 1, 2].map((i) => clamp(num(gains[i], 1), GAIN_MIN, GAIN_MAX)),
    smoothing: clamp(num(c.smoothing, RECOMMENDED.smoothing), 0, 1),
    gradientRemoval: clamp(num(c.gradientRemoval, RECOMMENDED.gradientRemoval), 0, 1),
    stretch: c.stretch === 'asinh' ? 'asinh' : 'mtf',
    saturation: clamp(num(c.saturation, RECOMMENDED.saturation), 0, 2),
  };
}

function template(uid) {
  const id = (name) => `${uid}-${name}`;
  return `
    <section class="color-view" aria-labelledby="${id('title')}">
      <header class="color-head">
        <h1 id="${id('title')}" class="display" tabindex="-1" data-i18n="color.title">Colore</h1>
        <p class="lede" data-i18n="color.lede"></p>
      </header>

      <div class="color-layout">
        <div class="color-preview">
          <div class="color-modes" role="group" data-i18n-aria="color.modes.label">
            <button type="button" class="btn" data-role="onlyRaw" aria-pressed="false" data-i18n="color.modes.onlyRaw">Solo originale</button>
            <button type="button" class="btn" data-role="onlyDone" aria-pressed="false" data-i18n="color.modes.onlyDone">Solo elaborata</button>
          </div>

          <div class="compare" role="group" data-role="compare" data-i18n-aria="color.compare.label">
            <img class="compare-img" data-role="doneImg" alt="" draggable="false" hidden>
            <img class="compare-img is-raw" data-role="rawImg" alt="" draggable="false" hidden>
            <span class="compare-tag is-raw" data-role="tagRaw" data-i18n="color.compare.rawTag" hidden>Originale</span>
            <span class="compare-tag is-done" data-role="tagDone" data-i18n="color.compare.doneTag" hidden>Elaborata</span>
            <div class="compare-divider" data-role="divider">
              <div class="compare-handle" data-role="handle" role="slider" tabindex="0"
                   aria-valuemin="0" aria-valuemax="100" aria-valuenow="50"
                   data-i18n-aria="color.compare.divider">
                <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M9.5 7.5v9M14.5 7.5v9"/></svg>
              </div>
            </div>
            <p class="compare-msg" data-role="msg" hidden></p>
          </div>

          <figure class="hist">
            <figcaption class="hist-title" data-i18n="color.hist.title">Fondo del cielo</figcaption>
            <p class="hist-help" data-i18n="color.hist.help"></p>
            <canvas class="hist-canvas" data-role="hist" role="img"></canvas>
            <ul class="hist-legend" aria-hidden="true">
              <li class="is-r"><span class="hist-line"></span><span data-i18n="color.hist.r">R</span></li>
              <li class="is-g"><span class="hist-line"></span><span data-i18n="color.hist.g">G</span></li>
              <li class="is-b"><span class="hist-line"></span><span data-i18n="color.hist.b">B</span></li>
            </ul>
            <p class="hist-note" data-role="histNote"></p>
          </figure>
        </div>

        <div class="color-panel">
          <fieldset class="ctl">
            <legend class="ctl-legend" data-i18n="color.balance.label">Bilanciamento del bianco</legend>
            <div class="choice-row">
              <label class="choice">
                <input type="radio" name="${id('wb')}" value="auto" data-role="wbAuto">
                <span data-i18n="color.balance.auto">Automatico</span>
              </label>
              <label class="choice">
                <input type="radio" name="${id('wb')}" value="manual" data-role="wbManual">
                <span data-i18n="color.balance.manual">Manuale</span>
              </label>
            </div>
            <p class="ctl-help" data-i18n="color.balance.help"></p>

            <div class="gains" data-role="gains" hidden>
              <div class="ctl">
                <div class="ctl-head">
                  <label for="${id('gr')}" data-i18n="color.balance.r">Rosso</label>
                  <output for="${id('gr')}" data-role="outR" class="num"></output>
                </div>
                <input type="range" id="${id('gr')}" data-role="gainR">
              </div>
              <div class="ctl">
                <div class="ctl-head">
                  <label for="${id('gg')}" data-i18n="color.balance.g">Verde</label>
                  <output for="${id('gg')}" data-role="outG" class="num"></output>
                </div>
                <input type="range" id="${id('gg')}" data-role="gainG">
              </div>
              <div class="ctl">
                <div class="ctl-head">
                  <label for="${id('gb')}" data-i18n="color.balance.b">Blu</label>
                  <output for="${id('gb')}" data-role="outB" class="num"></output>
                </div>
                <input type="range" id="${id('gb')}" data-role="gainB">
              </div>
              <p class="ctl-help" data-i18n="color.balance.gainHelp"></p>
              <div class="swatch">
                <span class="swatch-chip" data-role="chip" aria-hidden="true"></span>
                <p class="swatch-text" data-role="swatchText"></p>
              </div>
            </div>
          </fieldset>

          <div class="ctl">
            <div class="ctl-head">
              <label for="${id('smoothing')}" data-i18n="color.stability.label">Stabilità fra un frame e l'altro</label>
              <output for="${id('smoothing')}" data-role="outSmoothing" class="num"></output>
            </div>
            <input type="range" id="${id('smoothing')}" data-role="smoothing">
            <p class="ctl-help" data-i18n="color.stability.help"></p>
          </div>

          <div class="ctl">
            <div class="ctl-head">
              <label for="${id('gradient')}" data-i18n="color.gradient.label">Rimozione del gradiente</label>
              <output for="${id('gradient')}" data-role="outGradient" class="num"></output>
            </div>
            <input type="range" id="${id('gradient')}" data-role="gradient">
            <p class="ctl-help" data-i18n="color.gradient.help"></p>
          </div>

          <fieldset class="ctl">
            <legend class="ctl-legend" data-i18n="color.curve.label">Curva</legend>
            <div class="choice-row">
              <label class="choice">
                <input type="radio" name="${id('curve')}" value="mtf" data-role="stretchMtf">
                <span data-i18n="color.curve.mtf">MTF</span>
              </label>
              <label class="choice">
                <input type="radio" name="${id('curve')}" value="asinh" data-role="stretchAsinh">
                <span data-i18n="color.curve.asinh">Asinh</span>
              </label>
            </div>
            <p class="ctl-help" data-i18n="color.curve.help"></p>
          </fieldset>

          <div class="ctl">
            <div class="ctl-head">
              <label for="${id('saturation')}" data-i18n="color.saturation.label">Saturazione</label>
              <output for="${id('saturation')}" data-role="outSaturation" class="num"></output>
            </div>
            <input type="range" id="${id('saturation')}" data-role="saturation">
            <p class="ctl-help" data-i18n="color.saturation.help"></p>
          </div>

          <div class="color-actions">
            <button type="button" class="btn is-primary" data-role="save" data-i18n="color.actions.save">Salva</button>
            <button type="button" class="btn" data-role="reset" data-i18n="color.actions.reset">Ripristina i valori consigliati</button>
          </div>

          <div class="reset-confirm" data-role="confirm" hidden>
            <p data-i18n="color.actions.confirmText"></p>
            <div class="color-actions">
              <button type="button" class="btn is-primary" data-role="confirmYes" data-i18n="color.actions.confirm">Ripristina</button>
              <button type="button" class="btn" data-role="confirmNo" data-i18n="color.actions.cancel">Annulla</button>
            </div>
          </div>

          <div class="save-status">
            <p data-role="status" role="status" aria-live="polite"></p>
            <button type="button" class="btn" data-role="retry" data-i18n="color.status.retry" hidden>Riprova</button>
          </div>
        </div>
      </div>
    </section>
  `;
}

// Istogramma R/G/B della zona del cielo (il cerchio) del frame elaborato, campionato a bassa risoluzione.
function sampleSky(img) {
  const canvas = document.createElement('canvas');
  canvas.width = SAMPLE;
  canvas.height = SAMPLE;
  const ctx = canvas.getContext('2d', { willReadFrequently: true });
  ctx.drawImage(img, 0, 0, SAMPLE, SAMPLE);
  const px = ctx.getImageData(0, 0, SAMPLE, SAMPLE).data;

  const hist = { r: new Uint32Array(256), g: new Uint32Array(256), b: new Uint32Array(256) };
  const rad = SAMPLE * SKY_RADIUS;
  const half = SAMPLE / 2;
  let n = 0;
  for (let y = 0; y < SAMPLE; y++) {
    for (let x = 0; x < SAMPLE; x++) {
      const dx = x + 0.5 - half;
      const dy = y + 0.5 - half;
      if (dx * dx + dy * dy > rad * rad) continue;
      const i = (y * SAMPLE + x) * 4;
      hist.r[px[i]]++;
      hist.g[px[i + 1]]++;
      hist.b[px[i + 2]]++;
      n++;
    }
  }
  if (!n) return null;

  const median = (bins) => {
    let acc = 0;
    for (let v = 0; v < 256; v++) {
      acc += bins[v];
      if (acc * 2 >= n) return v;
    }
    return 255;
  };
  return { hist, n, median: { r: median(hist.r), g: median(hist.g), b: median(hist.b) } };
}

export async function mount(root, ctx) {
  const config = ctx.config ?? (await api.config());
  const tz = config?.site?.timezone || Intl.DateTimeFormat().resolvedOptions().timeZone;
  const uid = `color-${++viewSeq}`;
  root.innerHTML = template(uid);
  applyI18n(root);

  const el = {};
  root.querySelectorAll('[data-role]').forEach((node) => {
    el[node.dataset.role] = node;
  });
  el.rawImg.alt = t('color.compare.rawAlt');
  el.doneImg.alt = t('color.compare.doneAlt');
  el.handle.setAttribute('aria-label', t('color.compare.divider'));

  const s = {
    alive: true,
    color: normalizeColor(config?.color),
    frameId: ctx.getStatus()?.lastFrame?.id ?? null,
    rev: 0,
    split: 50,
    mode: null, // 'raw' | 'done' | null: pulsanti "Solo originale" / "Solo elaborata"
    dragging: false,
    dirty: false,
    saving: false,
    timer: null,
    stats: null,
    msg: null,
    statusKind: 'idle',
  };

  // ----- Frame -----

  function frameUrl(raw) {
    const version = `${s.frameId}-${s.rev}`;
    return `/api/frame/latest.jpg?${raw ? 'stage=raw&' : ''}v=${encodeURIComponent(version)}`;
  }

  function setMessage(kind, text) {
    s.msg = kind ? { kind, text } : null;
    el.msg.hidden = !s.msg;
    el.msg.textContent = s.msg ? s.msg.text : '';
    el.msg.dataset.kind = s.msg ? s.msg.kind : '';
    el.compare.setAttribute('data-empty', String(!s.frameId));
  }

  function loadImages() {
    if (!s.frameId) {
      el.rawImg.hidden = true;
      el.doneImg.hidden = true;
      el.tagRaw.hidden = true;
      el.tagDone.hidden = true;
      el.histNote.textContent = t('color.hist.none');
      setMessage('empty', t('color.preview.empty'));
      drawHist();
      return;
    }
    el.rawImg.hidden = false;
    el.doneImg.hidden = false;
    el.rawImg.src = frameUrl(true);
    el.doneImg.src = frameUrl(false);
    setMessage(null);
    applySplit();
  }

  el.doneImg.addEventListener('load', () => {
    if (s.msg?.kind === 'error') setMessage(null);
    analyse();
  });
  el.doneImg.addEventListener('error', () => setMessage('error', t('color.preview.error')));
  el.rawImg.addEventListener('load', () => {
    if (s.msg?.kind === 'error') setMessage(null);
  });
  el.rawImg.addEventListener('error', () => setMessage('error', t('color.preview.error')));

  // ----- Confronto: divisore con puntatore e tastiera -----

  // split = percentuale di larghezza in cui si vede l'originale (a sinistra).
  function applySplit() {
    const split = Math.round(clamp(s.split, 0, 100));
    s.split = split;
    el.rawImg.style.clipPath = `inset(0 ${100 - split}% 0 0)`;
    el.divider.style.left = `${split}%`;
    el.handle.setAttribute('aria-valuenow', String(split));
    el.handle.setAttribute('aria-valuetext', t('color.compare.valueText', { raw: split, done: 100 - split }));
    el.tagRaw.hidden = split <= 0 || !s.frameId;
    el.tagDone.hidden = split >= 100 || !s.frameId;
    el.onlyRaw.setAttribute('aria-pressed', String(s.mode === 'raw'));
    el.onlyDone.setAttribute('aria-pressed', String(s.mode === 'done'));
  }

  function setSplitFromX(clientX) {
    const rect = el.compare.getBoundingClientRect();
    if (!rect.width) return;
    s.split = clamp(((clientX - rect.left) / rect.width) * 100, 0, 100);
    s.mode = null;
    applySplit();
  }

  const onComparePointerDown = (event) => {
    if (event.pointerType === 'mouse' && event.button !== 0) return;
    if (!s.frameId) return;
    s.dragging = true;
    el.compare.setPointerCapture(event.pointerId);
    setSplitFromX(event.clientX);
  };
  const onComparePointerMove = (event) => {
    if (s.dragging) setSplitFromX(event.clientX);
  };
  const onComparePointerEnd = () => {
    s.dragging = false;
  };
  el.compare.addEventListener('pointerdown', onComparePointerDown);
  el.compare.addEventListener('pointermove', onComparePointerMove);
  el.compare.addEventListener('pointerup', onComparePointerEnd);
  el.compare.addEventListener('pointercancel', onComparePointerEnd);

  el.handle.addEventListener('keydown', (event) => {
    let next;
    switch (event.key) {
      case 'ArrowLeft':
      case 'ArrowDown':
        next = s.split - (event.shiftKey ? 10 : 1);
        break;
      case 'ArrowRight':
      case 'ArrowUp':
        next = s.split + (event.shiftKey ? 10 : 1);
        break;
      case 'PageDown':
        next = s.split - 10;
        break;
      case 'PageUp':
        next = s.split + 10;
        break;
      case 'Home':
        next = 0;
        break;
      case 'End':
        next = 100;
        break;
      default:
        return;
    }
    event.preventDefault();
    s.split = clamp(next, 0, 100);
    s.mode = null;
    applySplit();
  });

  el.onlyRaw.addEventListener('click', () => {
    s.mode = s.mode === 'raw' ? null : 'raw';
    s.split = s.mode === 'raw' ? 100 : 50;
    applySplit();
  });
  el.onlyDone.addEventListener('click', () => {
    s.mode = s.mode === 'done' ? null : 'done';
    s.split = s.mode === 'done' ? 0 : 50;
    applySplit();
  });

  // ----- Istogramma -----

  function analyse() {
    try {
      s.stats = sampleSky(el.doneImg);
    } catch {
      s.stats = null;
    }
    updateHistNote();
    drawHist();
  }

  function updateHistNote() {
    if (!s.stats) {
      el.histNote.textContent = t('color.hist.none');
      el.hist.setAttribute('aria-label', t('color.hist.label'));
      return;
    }
    const m = s.stats.median;
    const values = CHANNELS.map((c) => m[c]);
    const spread = Math.max(...values) - Math.min(...values);
    let verdict;
    if (spread <= 4) {
      verdict = t('color.hist.neutral');
    } else {
      const top = m.r >= m.g && m.r >= m.b ? 'r' : m.g >= m.b ? 'g' : 'b';
      verdict = t('color.hist.tint', { channel: t(`color.hist.${top === 'r' ? 'redName' : top === 'g' ? 'greenName' : 'blueName'}`) });
    }
    const medians = t('color.hist.medians', { r: m.r, g: m.g, b: m.b });
    el.histNote.textContent = `${verdict} ${medians}`;
    el.hist.setAttribute('aria-label', `${t('color.hist.label')}. ${verdict} ${medians}`);
  }

  function drawHist() {
    const canvas = el.hist;
    const cssW = canvas.clientWidth;
    const cssH = canvas.clientHeight;
    if (!cssW || !cssH) return;
    const dpr = window.devicePixelRatio || 1;
    canvas.width = Math.round(cssW * dpr);
    canvas.height = Math.round(cssH * dpr);
    const g = canvas.getContext('2d');
    g.setTransform(dpr, 0, 0, dpr, 0, 0);
    g.clearRect(0, 0, cssW, cssH);

    const style = getComputedStyle(canvas);
    const line = style.getPropertyValue('--line').trim() || 'rgba(128,128,128,0.3)';
    const muted = style.getPropertyValue('--text-2').trim() || '#888';
    const font = style.fontFamily || 'sans-serif';
    const colors = { r: style.getPropertyValue('--ch-r').trim(), g: style.getPropertyValue('--ch-g').trim(), b: style.getPropertyValue('--ch-b').trim() };
    const dashes = { r: [], g: [8, 4], b: [2, 3] };

    const padX = 8;
    const padTop = 12;
    const padBottom = 22;
    const pw = cssW - padX * 2;
    const ph = cssH - padTop - padBottom;
    const base = padTop + ph;

    // Griglia e asse con i valori 0, 128, 255.
    g.strokeStyle = line;
    g.lineWidth = 1;
    g.beginPath();
    for (const f of [0.25, 0.5, 0.75]) {
      const x = Math.round(padX + pw * f) + 0.5;
      g.moveTo(x, padTop);
      g.lineTo(x, base);
    }
    g.moveTo(padX, base + 0.5);
    g.lineTo(padX + pw, base + 0.5);
    g.stroke();

    g.fillStyle = muted;
    g.font = `12px ${font}`;
    g.textBaseline = 'alphabetic';
    g.textAlign = 'left';
    g.fillText('0', padX, cssH - 6);
    g.textAlign = 'center';
    g.fillText('128', padX + pw * 0.5, cssH - 6);
    g.textAlign = 'right';
    g.fillText('255', padX + pw, cssH - 6);

    if (!s.stats) return;

    // Istogrammi a 64 barre (somma di 4 livelli), scala lineare sul massimo dei tre canali.
    const per = 256 / HIST_BINS;
    const series = {};
    let max = 1;
    for (const c of CHANNELS) {
      const bins = new Array(HIST_BINS).fill(0);
      const src = s.stats.hist[c];
      for (let v = 0; v < 256; v++) bins[Math.floor(v / per)] += src[v];
      series[c] = bins;
      max = Math.max(max, ...bins);
    }

    for (const c of CHANNELS) {
      const bins = series[c];
      g.save();
      g.strokeStyle = colors[c];
      g.lineWidth = 2;
      g.lineJoin = 'round';
      g.setLineDash(dashes[c]);
      g.beginPath();
      let peak = 0;
      bins.forEach((count, i) => {
        if (count > bins[peak]) peak = i;
        const x = padX + ((i + 0.5) / HIST_BINS) * pw;
        const y = base - (count / max) * ph;
        if (i === 0) g.moveTo(x, y);
        else g.lineTo(x, y);
      });
      g.stroke();
      g.restore();

      // Etichetta del canale vicino al picco, così si legge anche senza colore.
      const px = padX + ((peak + 0.5) / HIST_BINS) * pw;
      g.fillStyle = colors[c];
      g.font = `600 12px ${font}`;
      // Su righe fisse in alto (R, G, B dall'alto), così i picchi vicini non si sovrappongono.
      const row = CHANNELS.indexOf(c);
      g.textAlign = px > padX + pw * 0.8 ? 'right' : 'left';
      g.fillText(t(`color.hist.${c}`), px + (g.textAlign === 'left' ? 4 : -4), padTop + 4 + row * 14);
    }
  }

  // ----- Pannello: cursori, radio, etichette -----

  function updateSwatch() {
    const [r, gg, b] = s.color.manualGains;
    const byte = (v) => Math.round(clamp(128 * v, 0, 255));
    el.chip.style.background = `rgb(${byte(r)}, ${byte(gg)}, ${byte(b)})`;
    el.swatchText.textContent = t('color.balance.swatch', {
      r: fmt.number(r, { digits: 2 }),
      g: fmt.number(gg, { digits: 2 }),
      b: fmt.number(b, { digits: 2 }),
    });
  }

  function syncControls() {
    const c = s.color;
    el.wbAuto.checked = c.whiteBalance !== 'manual';
    el.wbManual.checked = c.whiteBalance === 'manual';
    el.gains.hidden = c.whiteBalance !== 'manual';
    el.stretchMtf.checked = c.stretch !== 'asinh';
    el.stretchAsinh.checked = c.stretch === 'asinh';

    GAINS.forEach((gain, i) => {
      el[gain.role].value = String(c.manualGains[i]);
      el[gain.out].textContent = fmt.number(c.manualGains[i], { digits: 2 });
    });
    for (const sl of SLIDERS) {
      el[sl.role].value = String(c[sl.key]);
      el[sl.out].textContent = fmt.number(c[sl.key], { digits: sl.digits });
    }
    updateSwatch();
  }

  function setupInputs() {
    for (const sl of SLIDERS) {
      el[sl.role].min = String(sl.min);
      el[sl.role].max = String(sl.max);
      el[sl.role].step = String(sl.step);
    }
    for (const gain of GAINS) {
      el[gain.role].min = String(GAIN_MIN);
      el[gain.role].max = String(GAIN_MAX);
      el[gain.role].step = '0.01';
    }
  }

  // Ogni modifica cambia la copia locale, riallinea i controlli e programma il salvataggio.
  function edit(fn) {
    fn(s.color);
    syncControls();
    scheduleSave();
  }

  el.wbAuto.addEventListener('change', () => edit((c) => (c.whiteBalance = 'auto')));
  el.wbManual.addEventListener('change', () => edit((c) => (c.whiteBalance = 'manual')));
  el.stretchMtf.addEventListener('change', () => edit((c) => (c.stretch = 'mtf')));
  el.stretchAsinh.addEventListener('change', () => edit((c) => (c.stretch = 'asinh')));

  GAINS.forEach((gain, i) => {
    el[gain.role].addEventListener('input', () => {
      edit((c) => {
        const next = [...c.manualGains];
        next[i] = Number(el[gain.role].value);
        c.manualGains = next;
      });
    });
  });

  for (const sl of SLIDERS) {
    el[sl.role].addEventListener('input', () => {
      edit((c) => (c[sl.key] = Number(el[sl.role].value)));
    });
  }

  // ----- Salvataggio: PUT /api/config con la configurazione aggiornata, una per volta -----

  function setStatus(kind, detail) {
    s.statusKind = kind;
    let text = '';
    if (kind === 'applying') text = t('color.status.applying');
    if (kind === 'saved') text = t('color.status.saved', { time: fmt.time(new Date(), tz) });
    if (kind === 'error') text = t('color.status.error', { message: detail });
    el.status.textContent = text;
    el.status.dataset.kind = kind;
    el.retry.hidden = kind !== 'error';
    el.compare.setAttribute('aria-busy', String(kind === 'applying'));
  }

  function scheduleSave() {
    s.dirty = true;
    clearTimeout(s.timer);
    s.timer = setTimeout(() => {
      s.timer = null;
      save();
    }, SAVE_DELAY_MS);
  }

  function errorFromResponse(res) {
    const list = Array.isArray(res?.errors) ? res.errors : [];
    const text = list.map((e) => e?.message).filter(Boolean).join(' ');
    return text || t('errors.unknown');
  }

  async function save() {
    clearTimeout(s.timer);
    s.timer = null;
    if (s.saving) return; // il ciclo in corso ripete il salvataggio se nel frattempo è cambiato qualcosa
    s.saving = true;
    try {
      while (s.dirty) {
        s.dirty = false;
        if (s.alive) setStatus('applying');
        try {
          // Si legge la configurazione corrente, così non si sovrascrivono le sezioni modificate altrove.
          const latest = await api.config();
          const body = {
            ...latest,
            color: { ...(latest.color || {}), ...s.color, manualGains: [...s.color.manualGains] },
          };
          const res = await api.saveConfig(body);
          if (res && res.ok === false) {
            if (s.alive) setStatus('error', errorFromResponse(res));
          } else {
            s.rev += 1;
            if (s.alive) {
              loadImages();
              setStatus('saved');
            }
          }
        } catch (err) {
          if (!(err instanceof ApiError) && s.alive) console.error(err);
          if (s.alive) setStatus('error', errorMessage(err));
        }
      }
    } finally {
      s.saving = false;
    }
  }

  el.save.addEventListener('click', () => {
    s.dirty = true;
    save();
  });
  el.retry.addEventListener('click', () => {
    s.dirty = true;
    save();
  });

  // ----- Ripristino dei valori consigliati, con conferma -----

  function openConfirm() {
    el.confirm.hidden = false;
    el.reset.setAttribute('aria-expanded', 'true');
    el.confirmYes.focus();
  }

  function closeConfirm(focusReset) {
    el.confirm.hidden = true;
    el.reset.setAttribute('aria-expanded', 'false');
    if (focusReset) el.reset.focus();
  }

  el.reset.addEventListener('click', openConfirm);
  el.confirmNo.addEventListener('click', () => closeConfirm(true));
  el.confirmYes.addEventListener('click', () => {
    closeConfirm(false);
    s.color = normalizeColor(RECOMMENDED);
    syncControls();
    s.dirty = true;
    save();
  });
  el.confirm.addEventListener('keydown', (event) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      closeConfirm(true);
    }
  });

  // ----- Eventi dal motore: nuovo frame -----

  const offStatus = ctx.on('status', (status) => {
    if (!s.alive) return;
    const id = status?.lastFrame?.id ?? null;
    if (id !== s.frameId) {
      s.frameId = id;
      loadImages();
    }
  });

  // Il disegno dell'istogramma dipende dal tema e dalla larghezza.
  const themeObserver = new MutationObserver(() => drawHist());
  themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
  const resizeObserver = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(() => drawHist()) : null;
  resizeObserver?.observe(el.hist);

  // ----- Primo disegno -----

  setupInputs();
  syncControls();
  applySplit();
  setStatus('idle');
  loadImages();
  if (!s.frameId) drawHist();

  return {
    unmount() {
      s.alive = false;
      offStatus();
      themeObserver.disconnect();
      resizeObserver?.disconnect();
      // Le modifiche in attesa non si perdono: partono subito.
      if (s.timer) {
        s.dirty = true;
        save();
      }
    },
  };
}
