// Avvio dell'interfaccia: configurazione e lingua, tema, router a hash, stato condiviso, eventi dal motore.
import { api, openEvents } from './api.js';
import { loadLocale, applyI18n, t, errorMessage } from './i18n.js';

const WIZARD_FLAG = 'allsky.wizardDone';

// Le viste si caricano solo quando servono. Una vista non ancora consegnata (file assente) mostra il segnaposto.
const ROUTES = {
  live: () => import('./views/live.js'),
  nights: () => optionalView('nights'),
  color: () => optionalView('color'),
  camera: () => import('./views/camera.js'),
  site: () => import('./views/site.js'),
  wizard: () => import('./views/wizard.js'),
};

async function optionalView(name) {
  try {
    return await import(`./views/${name}.js`);
  } catch (err) {
    // Il browser segnala un file mancante come TypeError; errori di codice (SyntaxError, ...) si mostrano.
    if (err instanceof TypeError) return placeholderModule(name);
    throw err;
  }
}

const state = {
  config: null,
  status: null,
  online: true,
  current: null,
  renderToken: 0,
  themePreview: null, // tema scelto ma non ancora salvato, oppure null
};

const listeners = {
  status: new Set(),
  frame: new Set(),
};

const viewEl = document.getElementById('view');
const noticeEl = document.getElementById('notice');
const shellEl = document.querySelector('.shell');

// ----- Contesto passato alle viste -----

function emit(kind, payload) {
  for (const fn of [...listeners[kind]]) {
    try {
      fn(payload);
    } catch (err) {
      console.error(err);
    }
  }
}

const ctx = {
  get config() {
    return state.config;
  },
  getStatus() {
    return state.status;
  },
  on(kind, fn) {
    listeners[kind].add(fn);
    return () => listeners[kind].delete(fn);
  },
  // Dopo un salvataggio riuscito: la configurazione in memoria diventa quella salvata.
  setConfig(config) {
    state.config = config;
    state.themePreview = null;
    applyTheme();
  },
  // Anteprima del tema prima del salvataggio; null annulla l'anteprima.
  previewTheme(pref) {
    state.themePreview = pref ?? null;
    applyTheme();
  },
  // Cambia la lingua dell'interfaccia subito e ridisegna la vista corrente.
  async setLanguage(code) {
    const focusId = viewEl.contains(document.activeElement) ? document.activeElement.id : '';
    await loadLocale(code);
    applyI18n(document);
    document.title = t('app.title');
    await render({ focusId });
  },
  navigate(hash) {
    location.hash = hash;
  },
  wizardDone() {
    return readWizardFlag();
  },
  setWizardDone(value) {
    writeWizardFlag(value);
  },
};

// ----- Procedura guidata: flag di completamento in localStorage (con try/catch) -----

function readWizardFlag() {
  try {
    return localStorage.getItem(WIZARD_FLAG) === '1';
  } catch {
    return false;
  }
}

function writeWizardFlag(value) {
  try {
    if (value) localStorage.setItem(WIZARD_FLAG, '1');
    else localStorage.removeItem(WIZARD_FLAG);
  } catch {
    // Archiviazione bloccata: la procedura potrà ricomparire al prossimo avvio.
  }
}

// Al primo avvio, con il nome predefinito e nessuna camera scelta (o solo il simulatore), si apre la procedura.
function needsWizard(config) {
  if (!config || readWizardFlag()) return false;
  const cameraUnset = !config.camera?.id || config.camera.id === 'sim-1';
  return config.site?.name === 'Osservatorio' && cameraUnset;
}

// ----- Tema -----

// auto: giorno quando il Sole è sopra l'orizzonte, altrimenti notte (crepuscolo compreso).
function resolveTheme() {
  const pref = state.themePreview ?? state.config?.ui?.theme ?? 'auto';
  if (pref === 'day' || pref === 'night' || pref === 'red') return pref;
  return state.status?.phase === 'day' ? 'day' : 'night';
}

function applyTheme() {
  document.documentElement.dataset.theme = resolveTheme();
}

// ----- Avvisi di connessione -----

function setOnline(ok) {
  state.online = ok;
  noticeEl.hidden = ok;
  noticeEl.textContent = ok ? '' : t('app.offline');
}

// ----- Segnaposto -----

function placeholderModule(key) {
  return {
    mount(root) {
      const section = document.createElement('section');
      section.className = 'placeholder';
      section.setAttribute('aria-labelledby', `${key}-title`);

      const heading = document.createElement('h1');
      heading.id = `${key}-title`;
      heading.className = 'display';
      heading.tabIndex = -1;
      heading.textContent = t(`placeholder.${key}.title`);

      const body = document.createElement('p');
      body.className = 'lede';
      body.textContent = t(`placeholder.${key}.body`);

      section.append(heading, body);
      root.replaceChildren(section);
      return { unmount() {} };
    },
  };
}

// ----- Router a hash -----

function routeFromHash() {
  const name = location.hash.replace(/^#\/?/, '').split(/[?/]/)[0];
  return Object.hasOwn(ROUTES, name) ? name : null;
}

function setActiveNav(name) {
  document.querySelectorAll('[data-route]').forEach((link) => {
    if (link.dataset.route === name) {
      link.setAttribute('aria-current', 'page');
    } else {
      link.removeAttribute('aria-current');
    }
  });
}

function showViewError(err) {
  const p = document.createElement('p');
  p.className = 'notice';
  p.setAttribute('role', 'alert');
  p.textContent = errorMessage(err);
  viewEl.replaceChildren(p);
}

// focusId: campo da rimettere a fuoco dopo il ridisegno (per esempio il menu della lingua), se la vista lo ha ancora.
async function render({ focusId = '' } = {}) {
  const token = ++state.renderToken;

  let name = routeFromHash();
  if (!name) {
    history.replaceState(null, '', '#/live');
    name = 'live';
  }
  setActiveNav(name);
  shellEl.dataset.route = name;

  state.current?.unmount?.();
  state.current = null;

  let mod;
  try {
    mod = await ROUTES[name]();
  } catch (err) {
    console.error(err);
    if (token === state.renderToken) showViewError(err);
    return;
  }
  if (token !== state.renderToken) return;

  viewEl.replaceChildren();
  try {
    state.current = (await mod.mount(viewEl, ctx)) || null;
  } catch (err) {
    console.error(err);
    showViewError(err);
    return;
  }

  // Il titolo riceve il fuoco, così chi usa la tastiera o lo screen reader sa dove è arrivato.
  const keep = focusId ? document.getElementById(focusId) : null;
  (keep && viewEl.contains(keep) ? keep : viewEl.querySelector('h1'))?.focus({ preventScroll: true });
}

// ----- Dati dal motore -----

function onStatus(status) {
  state.status = status;
  applyTheme();
  setOnline(true);
  emit('status', status);
}

function onFrame(frame) {
  emit('frame', frame);
}

// ----- Avvio -----

async function boot() {
  let reachable = true;

  try {
    state.config = await api.config();
  } catch (err) {
    console.warn('Configurazione non letta:', err);
    reachable = false;
  }

  await loadLocale(state.config?.language ?? 'it');
  applyI18n(document);
  document.title = t('app.title');

  try {
    state.status = await api.status();
  } catch (err) {
    console.warn('Stato non letto:', err);
    reachable = false;
  }
  applyTheme();
  setOnline(reachable);

  if (needsWizard(state.config)) {
    history.replaceState(null, '', '#/wizard');
  }

  openEvents({
    onStatus,
    onFrame,
    onConnection: setOnline,
  });

  window.addEventListener('hashchange', () => render());
  await render();
}

boot().catch((err) => {
  console.error(err);
  const p = document.createElement('p');
  p.className = 'notice';
  p.setAttribute('role', 'alert');
  p.textContent = t('errors.unknown');
  viewEl.replaceChildren(p);
});
