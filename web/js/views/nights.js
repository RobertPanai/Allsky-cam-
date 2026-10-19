// Schermata Notti: elenco delle notti (più recente per prima) e pannello di dettaglio
// con timelapse, keogram, startrails, nastro della notte ed eliminazione.
// Il pannello è un <dialog> modale: si apre con #/nights/{id} senza ricaricare l'elenco.
import { api, ApiError } from '../api.js';
import { t, fmt, applyI18n, errorMessage } from '../i18n.js';
import { currentNightId, nightWindow, sampleBands } from '../time.js';

const PHASES = ['day', 'twilight', 'night'];
const TABS = ['timelapse', 'keogram', 'startrails'];
const ID_RE = /^\d{4}-\d{2}-\d{2}$/;
const FRAMES_PAGE = 1000;
const FRAMES_MAX = 20000;
const STRIP_MAX = 72;
const POLL_MS = 2000;
const SCALE_TICKS = [0, 25, 50, 75, 100];

const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
const enc = encodeURIComponent;

function esc(value) {
  return String(value).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}

const urls = {
  keogram: (id) => api.urls.keogram(id),
  startrails: (id) => api.urls.startrails(id),
  timelapse: (id) => `/api/nights/${enc(id)}/timelapse.mp4`,
  thumb: (id, frameId) => `/api/nights/${enc(id)}/thumb/${enc(frameId)}.jpg`,
};

function validId(id) {
  return typeof id === 'string' && ID_RE.test(id);
}

// "18 ottobre" dalla data dell'id: mezzogiorno UTC evita sfasamenti di fuso.
function nightDate(id) {
  const [y, m, d] = id.split('-').map(Number);
  return fmt.date(new Date(Date.UTC(y, m - 1, d, 12)), 'UTC');
}

function sizeText(mb) {
  if (!Number.isFinite(mb)) return '–';
  if (mb >= 1024) return t('nights.units.gb', { n: fmt.number(mb / 1024, { digits: 1 }) });
  return t('nights.units.mb', { n: fmt.number(mb) });
}

function countText(n) {
  return Number.isFinite(n) ? t('nights.units.frames', { n: fmt.number(n) }) : '–';
}

function hashNightId() {
  const m = location.hash.match(/^#\/nights\/([^/?#]+)/);
  if (!m) return null;
  try {
    const id = decodeURIComponent(m[1]);
    return validId(id) ? id : null;
  } catch {
    return null;
  }
}

function normalizeFrame(f) {
  return { id: f.id, ts: Date.parse(f.timestamp), phase: PHASES.includes(f.phase) ? f.phase : 'night' };
}

function parseBands(list) {
  return list
    .filter((b) => PHASES.includes(b.phase))
    .map((b) => ({ phase: b.phase, from: new Date(b.from), to: new Date(b.to) }))
    .filter((b) => !Number.isNaN(b.from.getTime()) && !Number.isNaN(b.to.getTime()));
}

function alertMarkup(text, kind = 'warn') {
  return `<div class="notice" role="alert" tabindex="-1" data-kind="${kind}"><p>${esc(text)}</p></div>`;
}

// ----- Struttura statica -----

function listTemplate() {
  return `
    <section class="nights" aria-labelledby="nights-title">
      <header class="nights-head">
        <h1 id="nights-title" class="display" tabindex="-1" data-role="listTitle">${t('nights.title')}</h1>
        <p class="lede">${t('nights.lede')}</p>
      </header>

      <p class="nights-status" role="status" data-role="status" hidden></p>

      <div class="notice" data-role="error" role="alert" data-kind="error" hidden>
        <p data-role="errorText"></p>
        <button type="button" class="btn" data-role="retry">${t('app.retry')}</button>
      </div>

      <p class="lede" data-role="loading">${t('nights.loading')}</p>

      <div class="nights-empty" data-role="empty" hidden>
        <h2 class="nights-empty-title">${t('nights.empty.title')}</h2>
        <p class="muted">${t('nights.empty.body')}</p>
        <a class="btn" href="#/live">${t('nights.empty.action')}</a>
      </div>

      <ul class="nights-grid" data-role="grid" hidden></ul>
    </section>
  `;
}

function tabsMarkup() {
  return TABS.map(
    (key) => `<button type="button" role="tab" class="np-tab" id="np-tab-${key}" data-tab="${key}"
      aria-selected="false" aria-controls="np-tabpanel" tabindex="-1">${t(`nights.tabs.${key}`)}</button>`,
  ).join('');
}

function panelTemplate() {
  return `
    <dialog class="np" data-role="panel" aria-labelledby="np-title">
      <div class="np-body">
        <header class="np-head">
          <div class="np-heading">
            <h2 id="np-title" class="np-title" tabindex="-1" data-role="title"></h2>
            <p class="np-meta num" data-role="meta"></p>
          </div>
          <button type="button" class="btn" data-role="close">${t('nights.actions.close')}</button>
        </header>

        <div class="np-tabs" role="tablist" aria-label="${t('nights.tabs.label')}" data-role="tabs">${tabsMarkup()}</div>
        <div class="np-tabpanel" role="tabpanel" id="np-tabpanel" tabindex="0" data-role="tabpanel"></div>

        <section class="np-ribbon" aria-labelledby="np-ribbon-title" data-role="ribbonSection">
          <h3 id="np-ribbon-title" class="np-section">${t('nights.ribbon.title')}</h3>
          <div class="np-pick">
            <figure class="np-preview">
              <img class="np-preview-img" data-role="previewImg" alt="" hidden>
              <p class="np-preview-empty" data-role="previewEmpty" hidden></p>
            </figure>
            <div class="np-pick-text">
              <p class="np-pick-time num" data-role="pickTime"></p>
              <p class="np-pick-phase" data-role="pickPhase"></p>
              <p class="np-hint" data-role="hint">${t('nights.ribbon.hint')}</p>
            </div>
          </div>
          <div class="np-track" role="slider" tabindex="0" data-role="track"
               aria-label="${t('nights.ribbon.label')}" aria-valuemin="0" aria-valuemax="0" aria-valuenow="0"
               aria-disabled="true">
            <div class="np-bands" data-role="bands"></div>
            <div class="np-cursor" data-role="cursor" hidden></div>
          </div>
          <div class="np-axis" data-role="axis" aria-hidden="true"></div>
          <div class="np-strip" role="group" aria-label="${t('nights.ribbon.strip')}" data-role="strip" hidden></div>
          <p class="np-note" data-role="note" hidden></p>
        </section>

        <section class="np-danger" aria-label="${t('nights.actions.delete')}" data-role="dangerSection">
          <p>${t('nights.danger.intro')}</p>
          <button type="button" class="btn np-danger-btn" data-role="deleteBtn">${t('nights.actions.delete')}</button>
        </section>
      </div>

      <dialog class="np-confirm" aria-labelledby="np-confirm-title" aria-describedby="np-confirm-body" data-role="confirm">
        <h2 id="np-confirm-title" class="np-section" data-role="confirmTitle"></h2>
        <p id="np-confirm-body" class="np-confirm-body" data-role="confirmBody"></p>
        <p class="notice" role="alert" data-kind="error" data-role="confirmError" hidden></p>
        <div class="np-actions np-actions-end">
          <button type="button" class="btn" data-role="confirmCancel" autofocus>${t('nights.actions.cancel')}</button>
          <button type="button" class="btn np-danger-btn" data-role="confirmOk">${t('nights.actions.delete')}</button>
        </div>
      </dialog>

      <dialog class="np-zoom" aria-labelledby="np-zoom-title" data-role="zoom">
        <div class="np-zoom-bar">
          <h2 id="np-zoom-title" class="np-section" data-role="zoomTitle"></h2>
          <div class="np-actions">
            <a class="btn" data-role="zoomDownload">${t('nights.actions.download')}</a>
            <button type="button" class="btn" data-role="zoomClose">${t('nights.actions.close')}</button>
          </div>
        </div>
        <img class="np-zoom-img" data-role="zoomImg" alt="">
      </dialog>
    </dialog>
  `;
}

// ----- Elenco -----

function flagMarkup(product, state, value) {
  return `<span class="night-flag" data-state="${state}"><span class="night-flag-name">${t(`nights.products.${product}`)}</span> <span class="night-flag-value">${esc(value)}</span></span>`;
}

function flagsMarkup(n) {
  const tl = n.timelapse || 'none';
  const tlValue = t(`nights.states.timelapse.${tl}`);
  const kg = n.keogram ? 'ready' : 'none';
  const st = n.startrails ? 'ready' : 'none';
  return [
    flagMarkup('timelapse', tl, tlValue),
    flagMarkup('keogram', kg, t(`nights.states.keogram.${kg}`)),
    flagMarkup('startrails', st, t(`nights.states.startrails.${st}`)),
  ].join('');
}

function cardMarkup(n, { latest, current }) {
  let kicker = '';
  if (current) kicker = t('nights.card.current');
  else if (latest) kicker = t('nights.card.latest');
  const date = nightDate(n.id);
  return `
    <li class="night-card${latest ? ' is-latest' : ''}">
      <a class="night-link" href="#/nights/${esc(n.id)}" data-night="${esc(n.id)}">
        <span class="night-thumb"><img src="${esc(urls.keogram(n.id))}" alt="" loading="lazy" decoding="async"></span>
        <span class="night-body">
          ${kicker ? `<span class="night-kicker">${esc(kicker)}</span>` : ''}
          <span class="night-title"><span class="night-title-text">${esc(t('nights.night.title', { date }))}</span></span>
          <span class="night-meta num">${esc(countText(n.frames))} · ${esc(sizeText(n.sizeMb))}</span>
          <span class="night-flags">${flagsMarkup(n)}</span>
        </span>
      </a>
    </li>`;
}

// ----- Montaggio -----

export function mount(root, ctx) {
  const tz = ctx.config?.site?.timezone || Intl.DateTimeFormat().resolvedOptions().timeZone;
  root.innerHTML = listTemplate() + panelTemplate();
  applyI18n(root);

  const el = {};
  root.querySelectorAll('[data-role]').forEach((node) => {
    el[node.dataset.role] = node;
  });

  const s = {
    alive: true,
    nights: [],
    listReady: false,
    listError: null,
    listToken: 0,
    openId: null,
    tab: 'timelapse',
    frames: [],
    framesLoaded: false,
    framesError: null,
    framesToken: 0,
    picked: null,
    window: null,
    progress: null,
    tlError: null,
    busy: false,
    poll: null,
    pollBusy: false,
    zoomOpener: null,
  };

  const timeText = (ms, seconds = false) => (Number.isFinite(ms) ? fmt.time(new Date(ms), tz, { seconds }) : '–');
  const entryOf = (id) => s.nights.find((n) => n.id === id) ?? null;
  const currentId = () => currentNightId(new Date(), tz);

  // ----- Elenco -----

  function renderList() {
    const nights = s.nights.slice().sort((a, b) => (a.id < b.id ? 1 : a.id > b.id ? -1 : 0));
    const hasNights = nights.length > 0;
    const now = currentId();

    el.error.hidden = !s.listError;
    el.errorText.textContent = s.listError || '';
    el.loading.hidden = s.listReady || Boolean(s.listError);
    el.empty.hidden = !(s.listReady && !s.listError && !hasNights);
    el.grid.hidden = !hasNights;
    el.grid.innerHTML = nights
      .map((n, i) => cardMarkup(n, { latest: i === 0, current: n.id === now }))
      .join('');
  }

  function announce(message) {
    el.status.textContent = message;
    el.status.hidden = !message;
  }

  async function refreshList() {
    const token = ++s.listToken;
    let nights = null;
    try {
      const data = await api.nights();
      if (!Array.isArray(data)) throw new ApiError('parse');
      nights = data;
    } catch (err) {
      if (!s.alive || token !== s.listToken) return;
      s.listError = errorMessage(err);
    }
    if (!s.alive || token !== s.listToken) return;
    if (nights) {
      s.nights = nights;
      s.listError = null;
    }
    s.listReady = true;
    renderList();
    if (s.openId) renderHeader();
  }

  // ----- Pannello: intestazione e schede -----

  function renderHeader() {
    const entry = entryOf(s.openId);
    if (!entry) {
      el.title.textContent = t('nights.night.missing');
      el.meta.textContent = '';
      return;
    }
    el.title.textContent = t('nights.night.title', { date: nightDate(entry.id) });
    const from = Date.parse(entry.firstFrame);
    const to = Date.parse(entry.lastFrame);
    const span = Number.isFinite(from) && Number.isFinite(to)
      ? t('nights.night.span', { from: timeText(from), to: timeText(to) })
      : '';
    el.meta.textContent = [countText(entry.frames), sizeText(entry.sizeMb), span].filter(Boolean).join(' · ');
  }

  function renderTabs() {
    el.tabs.querySelectorAll('[role="tab"]').forEach((btn) => {
      const on = btn.dataset.tab === s.tab;
      btn.setAttribute('aria-selected', String(on));
      btn.tabIndex = on ? 0 : -1;
    });
    el.tabpanel.setAttribute('aria-labelledby', `np-tab-${s.tab}`);
  }

  function selectTab(name, { focus = false } = {}) {
    if (!TABS.includes(name)) return;
    s.tab = name;
    renderTabs();
    renderTabBody();
    if (focus) el.tabs.querySelector(`[data-tab="${name}"]`)?.focus();
  }

  function timelapseMarkup(entry) {
    const id = entry.id;
    const date = nightDate(id);
    const alert = s.tlError ? alertMarkup(s.tlError, 'error') : '';
    const busy = s.busy ? ' disabled' : '';

    if (entry.timelapse === 'ready') {
      return `
        <div class="np-media">
          <video class="np-video" controls preload="metadata" playsinline src="${esc(urls.timelapse(id))}"
                 aria-label="${esc(t('nights.timelapse.videoLabel', { date }))}"></video>
          <p class="np-fallback" data-fallback hidden>${t('nights.timelapse.fallback')}</p>
        </div>
        <div class="np-actions">
          <a class="btn" href="${esc(urls.timelapse(id))}" download="timelapse-${esc(id)}.mp4">${t('nights.actions.download')}</a>
        </div>`;
    }
    if (entry.timelapse === 'generating') {
      return `
        <div class="np-job">
          <p class="np-job-text" data-progress-text></p>
          <progress class="np-progress" max="100" data-progress></progress>
        </div>`;
    }
    if (entry.timelapse === 'failed') {
      return `
        ${alertMarkup(t('nights.timelapse.failed'), 'error')}
        <div class="np-actions">
          <button type="button" class="btn np-primary" data-act="retry"${busy}>${t('app.retry')}</button>
        </div>
        ${alert}`;
    }
    // 'none' (o stato sconosciuto): si può creare.
    return `
      <div class="np-block">
        <p class="muted">${t('nights.timelapse.none')}</p>
        <div class="np-actions">
          <button type="button" class="btn np-primary" data-act="create"${busy}>${t('nights.actions.create')}</button>
        </div>
      </div>
      ${alert}`;
  }

  function imageMarkup(entry, kind) {
    const id = entry.id;
    if (!entry[kind]) return `<div class="np-block"><p class="muted">${t(`nights.${kind}.none`)}</p></div>`;
    const src = kind === 'keogram' ? urls.keogram(id) : urls.startrails(id);
    return `
      <div class="np-media">
        <img class="np-image-img" src="${esc(src)}" alt="${esc(t(`nights.${kind}.alt`, { date: nightDate(id) }))}">
        <p class="np-fallback" data-fallback hidden>${t('nights.image.fallback')}</p>
      </div>
      <div class="np-actions">
        <button type="button" class="btn" data-act="zoom" data-kind="${kind}">${t('nights.actions.zoom')}</button>
        <a class="btn" href="${esc(src)}" download="${kind}-${esc(id)}.jpg">${t('nights.actions.download')}</a>
      </div>`;
  }

  function renderTabBody() {
    const entry = entryOf(s.openId);
    if (!entry) {
      el.tabpanel.replaceChildren();
      return;
    }
    el.tabpanel.innerHTML = s.tab === 'timelapse' ? timelapseMarkup(entry) : imageMarkup(entry, s.tab);
    if (s.tab === 'timelapse') updateProgress();
  }

  // Aggiorna solo la barra e il testo mentre il lavoro gira, senza ricostruire il pannello.
  function updateProgress() {
    const bar = el.tabpanel.querySelector('[data-progress]');
    const text = el.tabpanel.querySelector('[data-progress-text]');
    if (!bar || !text) return;
    if (Number.isFinite(s.progress)) {
      bar.max = 100;
      bar.value = s.progress;
      text.textContent = t('nights.timelapse.generating', { n: fmt.number(s.progress) });
    } else {
      bar.removeAttribute('value');
      text.textContent = t('nights.timelapse.generatingUnknown');
    }
  }

  // ----- Pannello: frame, nastro e miniature -----

  function pctOf(ms) {
    const span = s.window.end.getTime() - s.window.start.getTime();
    return clamp(((ms - s.window.start.getTime()) / span) * 100, 0, 100);
  }

  function renderBands(entry) {
    const hasBands = Array.isArray(entry.sunBands) && entry.sunBands.length > 0;
    s.window = nightWindow(entry.id, tz);
    const bands = hasBands ? parseBands(entry.sunBands) : sampleBands(entry.id, tz);
    el.note.hidden = hasBands;
    el.note.textContent = hasBands ? '' : t('nights.ribbon.sample');
    el.bands.innerHTML = bands
      .map((b) => {
        const from = pctOf(b.from.getTime());
        const to = pctOf(b.to.getTime());
        if (to <= from) return '';
        return `<span class="np-seg np-seg-${b.phase}" style="left:${from.toFixed(3)}%;width:${(to - from).toFixed(3)}%"></span>`;
      })
      .join('');

    const { start, end } = s.window;
    const span = end.getTime() - start.getTime();
    el.axis.innerHTML = SCALE_TICKS.map((p) =>
      `<span class="np-tick num" style="left:${p}%">${esc(timeText(start.getTime() + (span * p) / 100))}</span>`,
    ).join('');
  }

  function defaultPick(frames) {
    if (!frames.length) return null;
    const night = [];
    frames.forEach((f, i) => {
      if (f.phase === 'night') night.push(i);
    });
    if (night.length) return night[Math.floor(night.length / 2)];
    return Math.floor(frames.length / 2);
  }

  function setPick(index) {
    const n = s.frames.length;
    s.picked = n ? clamp(index, 0, n - 1) : null;
    renderPick();
  }

  function renderPick() {
    const hasFrames = s.frames.length > 0;
    el.track.setAttribute('aria-disabled', String(!hasFrames));
    el.track.tabIndex = hasFrames ? 0 : -1;

    if (!hasFrames) {
      el.cursor.hidden = true;
      el.track.removeAttribute('aria-valuetext');
      el.pickTime.textContent = '';
      el.pickPhase.textContent = '';
      el.previewImg.hidden = true;
      el.previewEmpty.hidden = false;
      el.previewEmpty.textContent = !s.framesLoaded
        ? t('nights.ribbon.loading')
        : s.framesError || t('nights.ribbon.empty');
      el.strip.innerHTML = '';
      el.strip.hidden = true;
      return;
    }

    const frame = s.frames[s.picked];
    const n = s.frames.length;
    el.track.setAttribute('aria-valuemax', String(n - 1));
    el.track.setAttribute('aria-valuenow', String(s.picked));
    el.track.setAttribute('aria-valuetext', t('nights.ribbon.picked', { time: timeText(frame.ts, true) }));
    el.cursor.hidden = false;
    el.cursor.style.left = `${pctOf(frame.ts)}%`;
    el.pickTime.textContent = timeText(frame.ts, true);
    el.pickPhase.textContent = t(`phase.${frame.phase}`);

    const src = urls.thumb(s.openId, frame.id);
    if (el.previewImg.dataset.src !== src) {
      el.previewImg.dataset.src = src;
      el.previewImg.src = src;
    }
    el.previewImg.hidden = false;
    el.previewEmpty.hidden = true;
    highlightStrip();
  }

  // Le miniature sono un campione regolare dei frame; la più vicina al frame scelto è evidenziata.
  function renderStrip() {
    const n = s.frames.length;
    el.strip.hidden = n === 0;
    if (!n) {
      el.strip.innerHTML = '';
      return;
    }
    const step = Math.max(1, Math.ceil(n / STRIP_MAX));
    let html = '';
    for (let i = 0; i < n; i += step) {
      const f = s.frames[i];
      const label = t('nights.ribbon.thumbAlt', { time: timeText(f.ts, true) });
      html += `<button type="button" class="np-thumb" tabindex="-1" data-index="${i}" aria-label="${esc(label)}">` +
        `<img src="${esc(urls.thumb(s.openId, f.id))}" alt="" loading="lazy" decoding="async"></button>`;
    }
    el.strip.innerHTML = html;
    el.strip.dataset.step = String(step);
    highlightStrip();
  }

  function highlightStrip() {
    const n = s.frames.length;
    if (!n || s.picked === null) return;
    const step = Number(el.strip.dataset.step) || 1;
    const k = Math.min(Math.round(s.picked / step), Math.floor((n - 1) / step)) * step;
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    el.strip.querySelectorAll('.np-thumb').forEach((btn) => {
      const on = Number(btn.dataset.index) === k;
      if (on) {
        btn.setAttribute('aria-current', 'true');
        const left = btn.offsetLeft - el.strip.clientWidth / 2 + btn.offsetWidth / 2;
        el.strip.scrollTo({ left: Math.max(0, left), behavior: reduce ? 'auto' : 'smooth' });
      } else {
        btn.removeAttribute('aria-current');
      }
    });
  }

  async function loadFrames(id) {
    const token = ++s.framesToken;
    s.framesLoaded = false;
    s.framesError = null;
    s.frames = [];
    s.picked = null;
    renderPick();

    const out = [];
    try {
      for (let offset = 0; offset < FRAMES_MAX; offset += FRAMES_PAGE) {
        const page = await api.frames(id, { limit: FRAMES_PAGE, offset });
        if (!Array.isArray(page)) throw new ApiError('parse');
        for (const f of page) out.push(normalizeFrame(f));
        if (page.length < FRAMES_PAGE) break;
      }
    } catch (err) {
      if (!s.alive || id !== s.openId || token !== s.framesToken) return;
      s.framesError = errorMessage(err);
    }
    if (!s.alive || id !== s.openId || token !== s.framesToken) return;

    s.frames = out.filter((f) => Number.isFinite(f.ts));
    s.framesLoaded = true;
    s.picked = defaultPick(s.frames);
    renderStrip();
    renderPick();
  }

  // ----- Apertura, caricamento e chiusura del pannello -----

  async function loadPanel(id) {
    if (!s.listReady) await refreshList();
    if (!s.alive || id !== s.openId) return;

    const entry = entryOf(id);
    renderHeader();
    renderTabs();
    el.ribbonSection.hidden = !entry;
    el.dangerSection.hidden = !entry;

    if (!entry) {
      el.tabpanel.innerHTML = alertMarkup(s.listError || t('nights.notFound'), 'error');
      return;
    }
    renderBands(entry);
    renderTabBody();
    if (entry.timelapse === 'generating') startPolling();
    await loadFrames(id);
  }

  function openPanel(id, { fromHash = false } = {}) {
    if (!validId(id) || !el.panel) return;
    if (s.openId !== id) {
      stopPolling();
      s.openId = id;
      s.progress = null;
      s.tlError = null;
      s.frames = [];
      s.framesLoaded = false;
      s.picked = null;
      s.window = null;
    }
    if (!fromHash && location.hash !== `#/nights/${id}`) {
      history.pushState(null, '', `#/nights/${id}`);
    }
    if (!el.panel.open) el.panel.showModal();
    el.title.textContent = '';
    el.meta.textContent = '';
    el.title.focus({ preventScroll: true });
    loadPanel(id);
  }

  // Un solo punto per la chiusura: Esc, il pulsante Chiudi ed eliminazione passano da qui.
  el.panel.addEventListener('close', () => {
    if (!s.alive) return;
    stopPolling();
    const id = s.openId;
    s.openId = null;
    if (location.hash.startsWith('#/nights/')) history.replaceState(null, '', '#/nights');
    const card = id ? el.grid.querySelector(`[data-night="${id}"]`) : null;
    if (card) {
      card.focus();
    } else {
      el.listTitle.focus({ preventScroll: true });
    }
  });

  el.close.addEventListener('click', () => el.panel.close());

  // ----- Scheda Timelapse: creazione, progresso, esito -----

  async function startTimelapse(btn) {
    const id = s.openId;
    if (!id || s.busy || !entryOf(id)) return;
    s.busy = true;
    s.tlError = null;
    if (btn) btn.disabled = true;

    let failure = null;
    try {
      await api.startTimelapse(id);
    } catch (err) {
      failure = errorMessage(err);
    }
    s.busy = false;
    if (!s.alive || id !== s.openId) return;

    if (failure) {
      s.tlError = failure;
      renderTabBody();
      el.tabpanel.querySelector('[role="alert"]')?.focus();
      return;
    }
    const entry = entryOf(id);
    if (entry) entry.timelapse = 'generating';
    s.progress = null;
    renderTabBody();
    el.tabpanel.focus();
    renderList();
    startPolling();
  }

  function startPolling() {
    if (s.poll) return;
    s.poll = setInterval(pollTick, POLL_MS);
    pollTick();
  }

  function stopPolling() {
    if (s.poll) clearInterval(s.poll);
    s.poll = null;
  }

  // Legge lo stato del lavoro. Quando il job non c'è più, rilegge la notte per sapere com'è finita.
  async function pollTick() {
    const id = s.openId;
    if (!id) {
      stopPolling();
      return;
    }
    if (s.pollBusy) return;
    s.pollBusy = true;
    try {
      const status = await api.status();
      if (!s.alive || id !== s.openId) return;
      const job = Array.isArray(status.jobs)
        ? status.jobs.find((j) => j.kind === 'timelapse' && j.nightId === id)
        : null;

      if (job) {
        s.progress = Number.isFinite(job.progress) ? job.progress : null;
        const entry = entryOf(id);
        if (entry && entry.timelapse !== 'generating') {
          entry.timelapse = 'generating';
          renderTabBody();
          renderList();
        }
        updateProgress();
        return;
      }

      await refreshList();
      if (!s.alive || id !== s.openId) return;
      const fresh = entryOf(id);
      if (fresh && fresh.timelapse === 'generating') {
        updateProgress();
        return;
      }
      stopPolling();
      s.progress = null;
      if (s.tab === 'timelapse') renderTabBody();
    } catch {
      // Un errore di lettura non ferma il lavoro: si riprova al giro successivo.
    } finally {
      s.pollBusy = false;
    }
  }

  // ----- Ingrandimento keogram e startrails -----

  function openZoom(kind, opener) {
    const entry = entryOf(s.openId);
    if (!entry || !entry[kind]) return;
    const src = kind === 'keogram' ? urls.keogram(entry.id) : urls.startrails(entry.id);
    el.zoomTitle.textContent = t(`nights.tabs.${kind}`);
    el.zoomImg.src = src;
    el.zoomImg.alt = t(`nights.${kind}.alt`, { date: nightDate(entry.id) });
    el.zoomDownload.href = src;
    el.zoomDownload.download = `${kind}-${entry.id}.jpg`;
    s.zoomOpener = opener;
    el.zoom.showModal();
  }

  el.zoomClose.addEventListener('click', () => el.zoom.close());
  el.zoom.addEventListener('close', () => {
    if (!s.alive) return;
    const opener = s.zoomOpener;
    s.zoomOpener = null;
    if (opener && opener.isConnected) opener.focus();
  });

  // ----- Eliminazione -----

  function openConfirm() {
    const entry = entryOf(s.openId);
    if (!entry) return;
    el.confirmTitle.textContent = t('nights.confirm.title', { date: nightDate(entry.id) });
    el.confirmBody.textContent = t('nights.confirm.body', {
      frames: countText(entry.frames),
      size: sizeText(entry.sizeMb),
    });
    el.confirmError.hidden = true;
    el.confirmError.textContent = '';
    el.confirmOk.disabled = false;
    el.confirm.showModal();
  }

  async function confirmDelete() {
    const id = s.openId;
    if (!id || s.busy) return;
    s.busy = true;
    el.confirmOk.disabled = true;
    el.confirmError.hidden = true;
    try {
      await api.deleteNight(id);
      if (!s.alive) return;
      s.nights = s.nights.filter((n) => n.id !== id);
      stopPolling();
      el.confirm.close();
      announce(t('nights.deleted', { date: nightDate(id) }));
      renderList();
      el.panel.close();
    } catch (err) {
      if (!s.alive) return;
      el.confirmError.textContent = errorMessage(err);
      el.confirmError.hidden = false;
      el.confirmOk.disabled = false;
    } finally {
      s.busy = false;
    }
  }

  // ----- Collegamenti dagli eventi -----

  el.grid.addEventListener('click', (event) => {
    const link = event.target.closest('a[data-night]');
    if (!link || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    openPanel(link.dataset.night);
  });

  el.retry.addEventListener('click', () => refreshList());

  el.tabs.addEventListener('click', (event) => {
    const btn = event.target.closest('[role="tab"]');
    if (btn) selectTab(btn.dataset.tab);
  });

  el.tabs.addEventListener('keydown', (event) => {
    const idx = TABS.indexOf(s.tab);
    let next = null;
    switch (event.key) {
      case 'ArrowRight':
        next = (idx + 1) % TABS.length;
        break;
      case 'ArrowLeft':
        next = (idx - 1 + TABS.length) % TABS.length;
        break;
      case 'Home':
        next = 0;
        break;
      case 'End':
        next = TABS.length - 1;
        break;
      default:
        return;
    }
    event.preventDefault();
    selectTab(TABS[next], { focus: true });
  });

  el.tabpanel.addEventListener('click', (event) => {
    const btn = event.target.closest('[data-act]');
    if (!btn) return;
    if (btn.dataset.act === 'create' || btn.dataset.act === 'retry') startTimelapse(btn);
    if (btn.dataset.act === 'zoom') openZoom(btn.dataset.kind, btn);
  });

  // Se un'immagine o un video non si caricano, il testo di ripiego prende il loro posto.
  el.tabpanel.addEventListener(
    'error',
    (event) => {
      const media = event.target;
      if (!(media instanceof HTMLVideoElement) && !(media instanceof HTMLImageElement)) return;
      media.hidden = true;
      const fallback = media.parentElement?.querySelector('[data-fallback]');
      if (fallback) fallback.hidden = false;
    },
    true,
  );

  el.deleteBtn.addEventListener('click', openConfirm);
  el.confirmCancel.addEventListener('click', () => el.confirm.close());
  el.confirmOk.addEventListener('click', confirmDelete);

  el.previewImg.addEventListener('load', () => {
    el.previewEmpty.hidden = true;
  });
  el.previewImg.addEventListener('error', () => {
    el.previewImg.hidden = true;
    el.previewEmpty.hidden = false;
    el.previewEmpty.textContent = t('nights.ribbon.broken');
  });

  el.strip.addEventListener('click', (event) => {
    const btn = event.target.closest('[data-index]');
    if (btn) setPick(Number(btn.dataset.index));
  });

  // ----- Nastro della notte: puntatore e tastiera -----

  function pickFromClientX(clientX) {
    if (!s.frames.length || !s.window) return;
    const rect = el.track.getBoundingClientRect();
    const ratio = clamp((clientX - rect.left) / rect.width, 0, 1);
    const span = s.window.end.getTime() - s.window.start.getTime();
    const ms = s.window.start.getTime() + ratio * span;
    setPick(nearestFrameIndex(ms));
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

  let dragging = false;
  el.track.addEventListener('pointerdown', (event) => {
    if (!s.frames.length) return;
    if (event.pointerType === 'mouse' && event.button !== 0) return;
    dragging = true;
    el.track.setPointerCapture(event.pointerId);
    el.track.focus({ preventScroll: true });
    pickFromClientX(event.clientX);
  });
  el.track.addEventListener('pointermove', (event) => {
    if (dragging) pickFromClientX(event.clientX);
  });
  const endDrag = () => {
    dragging = false;
  };
  el.track.addEventListener('pointerup', endDrag);
  el.track.addEventListener('pointercancel', endDrag);

  // Esc non è gestito qui: deve arrivare al <dialog> e chiudere il pannello.
  el.track.addEventListener('keydown', (event) => {
    if (!s.frames.length) return;
    const last = s.frames.length - 1;
    const cur = s.picked ?? 0;
    let next;
    switch (event.key) {
      case 'ArrowLeft':
      case 'ArrowDown':
        next = cur - 1;
        break;
      case 'ArrowRight':
      case 'ArrowUp':
        next = cur + 1;
        break;
      case 'PageDown':
        next = cur - 10;
        break;
      case 'PageUp':
        next = cur + 10;
        break;
      case 'Home':
        next = 0;
        break;
      case 'End':
        next = last;
        break;
      default:
        return;
    }
    event.preventDefault();
    setPick(next);
  });

  // ----- Avvio -----

  renderList();
  refreshList().then(() => {
    if (!s.alive) return;
    const id = hashNightId();
    if (id) openPanel(id, { fromHash: true });
  });

  return {
    unmount() {
      s.alive = false;
      stopPolling();
      for (const dialog of [el.confirm, el.zoom, el.panel]) {
        if (dialog.open) dialog.close();
      }
    },
  };
}
