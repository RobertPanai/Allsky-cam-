// Pezzi di form condivisi da Procedura guidata, Camera e Sito: campi con etichetta, aiuto ed errore,
// interruttori, scelte, barra di salvataggio, selezione camera con anteprima, posizione del dispositivo.
// Il DOM si costruisce con h(): nessun innerHTML con dati dell'utente. Le stringhe stanno in i18n/*.forms.json.
import { api } from './api.js';
import { t, errorMessage, localeTag } from './i18n.js';

const PREVIEW_MS = 10000;
const TIME_ZONES_FALLBACK = [
  'UTC', 'Europe/London', 'Europe/Paris', 'Europe/Rome', 'Europe/Berlin',
  'America/New_York', 'America/Los_Angeles', 'Asia/Tokyo', 'Australia/Sydney',
];

let seq = 0;
const uid = (base) => `${base}-${(seq += 1)}`;

// Costruttore DOM minimo. Chiavi: class, text, on<evento>, altri attributi. false/null/undefined si saltano.
export function h(tag, props = {}, ...children) {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(props)) {
    if (value === undefined || value === null || value === false) continue;
    if (key === 'class') node.className = value;
    else if (key === 'text') node.textContent = String(value);
    else if (key.startsWith('on')) node.addEventListener(key.slice(2), value);
    else if (value === true) node.setAttribute(key, '');
    else node.setAttribute(key, String(value));
  }
  node.append(...children.filter((c) => c !== null && c !== undefined && c !== false));
  return node;
}

// ----- Dati e percorsi -----

export const clone = (value) => JSON.parse(JSON.stringify(value));
export const sameValue = (a, b) => JSON.stringify(a) === JSON.stringify(b);

// Copia della configurazione con i valori che il motore può lasciare vuoti.
export function withDefaults(config) {
  const cfg = clone(config);
  cfg.site = cfg.site || {};
  if (!cfg.site.timezone) {
    cfg.site.timezone = Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  }
  return cfg;
}

export function getPath(obj, path) {
  return path.split('.').reduce((o, k) => (o === null || o === undefined ? undefined : o[k]), obj);
}

export function setPath(obj, path, value) {
  const keys = path.split('.');
  const last = keys.pop();
  let target = obj;
  for (const k of keys) {
    if (!target[k] || typeof target[k] !== 'object') target[k] = {};
    target = target[k];
  }
  target[last] = value;
}

// ----- Numeri nella lingua corrente: virgola in italiano, punto in inglese -----

export function numberText(value, maxDigits = 6) {
  if (!Number.isFinite(value)) return '';
  return new Intl.NumberFormat(localeTag(), { useGrouping: false, maximumFractionDigits: maxDigits }).format(value);
}

// Legge un numero scritto a mano: accetta virgola o punto e il segno meno tipografico. Null se non è un numero.
export function parseNumber(text) {
  const s = String(text ?? '').trim().replace('−', '-').replace(',', '.');
  if (!/^[-+]?(\d+\.?\d*|\.\d+)$/.test(s)) return null;
  return Number(s);
}

export function isTimeZone(zone) {
  try {
    new Intl.DateTimeFormat('en-US', { timeZone: zone });
    return true;
  } catch {
    return false;
  }
}

// ----- Gestione di errore e stato di un campo -----

function errorHandles(node, input, errEl) {
  const showError = (message) => {
    errEl.textContent = message;
    errEl.hidden = false;
    node.classList.add('is-invalid');
    input.setAttribute('aria-invalid', 'true');
  };
  const clearError = () => {
    errEl.textContent = '';
    errEl.hidden = true;
    node.classList.remove('is-invalid');
    input.removeAttribute('aria-invalid');
  };
  const isShowing = () => !errEl.hidden;
  return { showError, clearError, isShowing };
}

// Campo di testo o numero. Il valore va in draft[path]; un numero è mostrato diviso per `scale`
// (per esempio millisecondi mostrati in secondi con scale 1000).
export function inputField({
  draft, path, label, help, kind = 'text', unit, min, max, integer = false, scale = 1,
  maxLength, required = false, autocomplete = 'off', onChange,
}) {
  const id = uid('in');
  const helpId = `${id}-help`;
  const errId = `${id}-error`;
  const input = h('input', {
    id,
    class: 'input',
    type: 'text',
    autocomplete,
    inputmode: kind === 'number' ? (integer ? 'numeric' : 'decimal') : undefined,
    maxlength: maxLength,
    'aria-required': required ? 'true' : undefined,
    'aria-describedby': [help ? helpId : null, errId].filter(Boolean).join(' '),
    spellcheck: 'false',
  });
  const errEl = h('p', { class: 'field-error', id: errId, hidden: true });
  const node = h('div', { class: 'field' },
    h('label', { class: 'field-label', for: id, text: label }),
    h('div', { class: 'input-wrap' }, input, unit ? h('span', { class: 'input-unit', text: unit }) : null),
    help ? h('p', { class: 'field-help', id: helpId, text: help }) : null,
    errEl,
  );
  const { showError, clearError, isShowing } = errorHandles(node, input, errEl);

  const toText = () => {
    const v = getPath(draft, path);
    if (kind !== 'number') return v ?? '';
    return Number.isFinite(v) ? numberText(v / scale, integer ? 0 : 6) : '';
  };

  // Restituisce { value } oppure { error }.
  const read = () => {
    const raw = input.value.trim();
    if (kind !== 'number') {
      if (!raw && required) return { error: t('forms.required') };
      if (maxLength && raw.length > maxLength) return { error: t('forms.tooLong', { max: maxLength }) };
      return { value: raw };
    }
    if (!raw) return { error: t('forms.required') };
    const n = parseNumber(raw);
    if (n === null) return { error: t(integer ? 'forms.integer' : 'forms.number') };
    if (integer && !Number.isInteger(n)) return { error: t('forms.integer') };
    if ((min !== undefined && n < min) || (max !== undefined && n > max)) {
      return { error: t('forms.range', { min: numberText(min), max: numberText(max) }) };
    }
    return { value: Number((n * scale).toPrecision(12)) };
  };

  input.value = toText();

  const validate = () => {
    const r = read();
    if (r.error) {
      showError(r.error);
      return false;
    }
    clearError();
    setPath(draft, path, r.value);
    return true;
  };

  input.addEventListener('input', () => {
    const r = read();
    if (!r.error) {
      setPath(draft, path, r.value);
      clearError();
    } else if (isShowing()) {
      showError(r.error);
    }
    onChange?.();
  });
  input.addEventListener('change', () => {
    validate();
    onChange?.();
  });

  return {
    path,
    node,
    validate,
    showError,
    focus: () => input.focus(),
    refresh() {
      input.value = toText();
      clearError();
    },
  };
}

// Menu a tendina. `check(valore)` restituisce un messaggio d'errore oppure null.
// `numeric`: il valore scelto va salvato come numero. `id`: id fisso, per ritrovare il campo dopo un ridisegno.
export function selectField({ draft, path, label, help, options, check, onChange, numeric = false, id: fixedId }) {
  const id = fixedId ?? uid('sel');
  const helpId = `${id}-help`;
  const errId = `${id}-error`;
  const select = h('select', {
    id,
    class: 'input input-select',
    'aria-describedby': [help ? helpId : null, errId].filter(Boolean).join(' '),
  }, ...options.map((o) => h('option', { value: o.value, text: o.label })));
  const errEl = h('p', { class: 'field-error', id: errId, hidden: true });
  const node = h('div', { class: 'field' },
    h('label', { class: 'field-label', for: id, text: label }),
    select,
    help ? h('p', { class: 'field-help', id: helpId, text: help }) : null,
    errEl,
  );
  const { showError, clearError } = errorHandles(node, select, errEl);

  const message = (value) => (check ? check(value) : null);

  const validate = () => {
    const msg = message(select.value);
    if (msg) {
      showError(msg);
      return false;
    }
    clearError();
    setPath(draft, path, numeric ? Number(select.value) : select.value);
    return true;
  };

  select.value = getPath(draft, path) ?? '';
  select.addEventListener('change', () => {
    validate();
    onChange?.();
  });

  return {
    path,
    node,
    validate,
    showError,
    focus: () => select.focus(),
    refresh() {
      select.value = getPath(draft, path) ?? '';
      clearError();
    },
  };
}

// Interruttore accessibile (checkbox con role=switch). `disabled` mostra `disabledHelp` al posto dell'aiuto.
// `warning` compare solo quando l'interruttore è acceso.
export function switchField({ draft, path, label, help, disabled = false, disabledHelp, warning, onChange }) {
  const id = uid('sw');
  const input = h('input', { id, class: 'switch-input', type: 'checkbox', role: 'switch', disabled });
  const helpText = disabled ? disabledHelp : help;
  const helpEl = helpText ? h('span', { class: 'field-help', text: helpText }) : null;
  const warnEl = warning ? h('p', { class: 'field-warn', text: warning, hidden: true }) : null;
  const node = h('div', { class: 'field' },
    h('div', { class: `field-switch${disabled ? ' is-disabled' : ''}` },
      h('label', { class: 'switch-label', for: id },
        h('span', { class: 'field-label', text: label }),
        helpEl,
      ),
      input,
    ),
    warnEl,
  );

  const sync = () => {
    input.checked = Boolean(getPath(draft, path));
    if (warnEl) warnEl.hidden = !(input.checked && !disabled);
  };
  input.addEventListener('change', () => {
    setPath(draft, path, input.checked);
    sync();
    onChange?.();
  });
  sync();

  return {
    path,
    node,
    validate: () => true,
    showError: () => {},
    focus: () => input.focus(),
    refresh: sync,
  };
}

// Gruppo di scelte esclusive (radio), a elenco o a griglia. Ogni opzione: { value, label, help? }.
export function choiceField({ draft, path, legend, help, options, layout = 'list', onChange }) {
  const name = uid('ch');
  const helpId = `${name}-help`;
  const list = h('div', { class: layout === 'grid' ? 'choices is-grid' : 'choices' });
  const items = options.map((opt) => {
    const id = uid('opt');
    const input = h('input', { id, type: 'radio', name, value: opt.value });
    const labelEl = h('label', { class: 'choice', for: id },
      input,
      h('span', { class: 'choice-text' },
        h('span', { class: 'choice-title', text: opt.label }),
        opt.help ? h('span', { class: 'choice-help', text: opt.help }) : null,
      ),
    );
    list.append(labelEl);
    return { value: opt.value, input, labelEl };
  });
  const node = h('fieldset', { class: 'field choice-group' },
    h('legend', { class: 'field-label', text: legend }),
    help ? h('p', { class: 'field-help', id: helpId, text: help }) : null,
    list,
  );

  const sync = () => {
    const current = getPath(draft, path);
    for (const item of items) {
      item.input.checked = item.value === current;
      item.labelEl.classList.toggle('is-checked', item.input.checked);
    }
  };
  for (const item of items) {
    item.input.addEventListener('change', () => {
      setPath(draft, path, item.value);
      sync();
      onChange?.();
    });
  }
  sync();

  return {
    path,
    node,
    validate: () => true,
    showError: () => {},
    focus: () => items[0]?.input.focus(),
    refresh: sync,
  };
}

// ----- Verifica dell'insieme dei campi -----

// Controlla tutti i campi (mostra tutti gli errori) e porta il fuoco sul primo non valido.
export function validateAll(fields) {
  let first = null;
  for (const f of fields) {
    if (!f.validate()) first = first ?? f;
  }
  first?.focus();
  return !first;
}

// Mostra sotto ai campi gli errori del motore ({path, message}). Restituisce i messaggi senza campo.
export function applyServerErrors(fields, errors = []) {
  const rest = [];
  for (const e of errors) {
    const field = fields.find((f) => f.path === e.path);
    if (field) field.showError(e.message);
    else rest.push(e.message);
  }
  return rest;
}

// ----- Blocchi di layout -----

export function formSection({ title, lede }, ...children) {
  const id = uid('sec');
  return h('section', { class: 'form-section', 'aria-labelledby': id },
    h('h2', { class: 'section-title', id, text: title }),
    lede ? h('p', { class: 'section-lede', text: lede }) : null,
    ...children,
  );
}

export function fieldGrid(...children) {
  return h('div', { class: 'field-grid' }, ...children);
}

// Barra in fondo al form: stato (modifiche non salvate, Salvato, errori) e pulsante Salva.
export function saveBar({ onSave }) {
  const dirtyEl = h('p', { class: 'dirty', text: t('forms.unsaved'), hidden: true });
  const status = h('p', { class: 'form-status', role: 'status' });
  const button = h('button', { type: 'button', class: 'btn btn-primary' }, t('forms.save'));
  button.addEventListener('click', () => onSave());
  const node = h('div', { class: 'save-bar' },
    h('div', { class: 'save-state' }, dirtyEl, status),
    button,
  );
  let kind = null;
  const say = (text, k = null) => {
    kind = k;
    status.textContent = text;
    status.classList.toggle('is-error', k === 'error');
    status.classList.toggle('is-ok', k === 'ok');
  };
  return {
    node,
    say,
    setDirty(on) {
      dirtyEl.hidden = !on;
      if (on && kind === 'ok') say('');
    },
    setBusy(on) {
      button.disabled = on;
      button.textContent = on ? t('forms.saving') : t('forms.save');
    },
  };
}

// ----- Gruppi di campi ricorrenti -----

export function timeZoneOptions(current) {
  let zones = [];
  try {
    if (typeof Intl.supportedValuesOf === 'function') zones = Intl.supportedValuesOf('timeZone');
  } catch {
    zones = [];
  }
  if (!zones.length) zones = TIME_ZONES_FALLBACK;
  if (current && !zones.includes(current)) zones = [current, ...zones];
  return zones.map((z) => ({ value: z, label: z }));
}

// Dati del sito: nome, latitudine, longitudine, quota, fuso orario.
export function siteFields(draft, onChange) {
  const f = (key) => t(`forms.fields.${key}.label`);
  const help = (key) => t(`forms.fields.${key}.help`);
  return [
    inputField({ draft, path: 'site.name', label: f('siteName'), help: help('siteName'), maxLength: 60, required: true, onChange }),
    inputField({ draft, path: 'site.latitude', kind: 'number', label: f('latitude'), help: help('latitude'), unit: '°', min: -90, max: 90, onChange }),
    inputField({ draft, path: 'site.longitude', kind: 'number', label: f('longitude'), help: help('longitude'), unit: '°', min: -180, max: 180, onChange }),
    inputField({ draft, path: 'site.elevationM', kind: 'number', label: f('elevation'), help: help('elevation'), unit: 'm', min: -500, max: 9000, onChange }),
    selectField({
      draft,
      path: 'site.timezone',
      label: f('timezone'),
      help: help('timezone'),
      options: timeZoneOptions(getPath(draft, 'site.timezone')),
      check: (v) => (v && isTimeZone(v) ? null : t('forms.timezone')),
      onChange,
    }),
  ];
}

// Cartella di salvataggio e notti da conservare.
export function archiveFields(draft, onChange) {
  return [
    inputField({ draft, path: 'storage.folder', label: t('forms.fields.folder.label'), help: t('forms.fields.folder.help'), maxLength: 260, required: true, onChange }),
    inputField({ draft, path: 'storage.keepNights', kind: 'number', integer: true, label: t('forms.fields.keepNights.label'), help: t('forms.fields.keepNights.help'), min: 1, max: 3650, onChange }),
  ];
}

// Prodotti da creare, con un interruttore ciascuno.
export function productFields(draft, onChange) {
  return ['timelapse', 'keogram', 'startrails'].map((key) => switchField({
    draft,
    path: `products.${key}`,
    label: t(`forms.fields.products.${key}.label`),
    help: t(`forms.fields.products.${key}.help`),
    onChange,
  }));
}

// Pulsante per usare la posizione del dispositivo. Richiede `onLocated({latitude, longitude, elevation?})`.
export function locationTool({ onLocated }) {
  const status = h('p', { class: 'form-status', role: 'status' });
  const button = h('button', { type: 'button', class: 'btn' }, t('forms.location.button'));
  const supported = typeof navigator !== 'undefined' && 'geolocation' in navigator;
  if (!supported) {
    button.disabled = true;
    status.textContent = t('forms.location.unsupported');
  }
  button.addEventListener('click', () => {
    button.disabled = true;
    status.classList.remove('is-error');
    status.textContent = t('forms.location.busy');
    navigator.geolocation.getCurrentPosition(
      (pos) => {
        const { latitude, longitude, altitude } = pos.coords;
        onLocated({
          latitude: Math.round(latitude * 1e5) / 1e5,
          longitude: Math.round(longitude * 1e5) / 1e5,
          elevation: Number.isFinite(altitude) ? Math.round(altitude) : undefined,
        });
        button.disabled = false;
        status.textContent = t('forms.location.done');
      },
      () => {
        button.disabled = false;
        status.classList.add('is-error');
        status.textContent = t('forms.location.error');
      },
      { enableHighAccuracy: false, timeout: 15000, maximumAge: 600000 },
    );
  });
  const node = h('div', { class: 'locate' },
    h('p', { class: 'field-help', text: t('forms.location.help') }),
    button,
    status,
  );
  return { node };
}

// ----- Anteprima dal vivo e scelta della camera -----

export function livePreview() {
  const img = h('img', {
    class: 'cam-img',
    alt: t('forms.cameras.previewAlt'),
    width: 320,
    height: 320,
  });
  const empty = h('p', { class: 'cam-empty', hidden: true, text: t('forms.cameras.previewEmpty') });
  img.addEventListener('load', () => {
    img.hidden = false;
    empty.hidden = true;
  });
  img.addEventListener('error', () => {
    img.hidden = true;
    empty.hidden = false;
  });
  const node = h('figure', { class: 'cam-preview' },
    h('figcaption', { class: 'cam-caption', text: t('forms.cameras.previewTitle') }),
    img,
    empty,
  );
  let timer = null;
  const reload = () => {
    img.src = api.urls.latestFrame(String(Date.now()));
  };
  return {
    node,
    start() {
      if (timer) return;
      reload();
      timer = setInterval(reload, PREVIEW_MS);
    },
    stop() {
      clearInterval(timer);
      timer = null;
    },
  };
}

function kindLabel(kind) {
  const key = `forms.cameras.kind.${kind}`;
  const label = t(key);
  return label === key ? String(kind) : label;
}

// Elenco delle camere trovate, pulsanti Connetti camera e Cerca di nuovo, anteprima dopo la connessione.
// `connected`: { id, name } della camera già connessa, oppure null.
export function cameraPicker({ selectedId = '', connected = null, onConnected, preview = true }) {
  let cameras = [];
  let chosen = selectedId || '';
  let busy = false;
  const live = preview ? livePreview() : null;

  const status = h('p', { class: 'form-status', role: 'status' });
  const listHost = h('div', { class: 'cam-list' });
  const connectBtn = h('button', { type: 'button', class: 'btn btn-primary', disabled: true }, t('forms.cameras.connect'));
  const searchBtn = h('button', { type: 'button', class: 'btn' }, t('forms.cameras.search'));
  const previewHost = h('div', { class: 'cam-preview-host' }, live ? live.node : null);
  const root = h('div', { class: 'cam-picker' },
    listHost,
    h('div', { class: 'btn-row' }, connectBtn, searchBtn),
    status,
    previewHost,
  );

  const say = (text, kind = null) => {
    status.textContent = text;
    status.classList.toggle('is-error', kind === 'error');
    status.classList.toggle('is-ok', kind === 'ok');
  };

  const setBusy = (on) => {
    busy = on;
    connectBtn.disabled = on || !chosen;
    searchBtn.disabled = on;
  };

  const renderList = () => {
    listHost.replaceChildren();
    if (!cameras.length) {
      listHost.append(h('p', { class: 'cam-empty', text: t('forms.cameras.empty') }));
      return;
    }
    const name = uid('cam');
    const choices = h('div', { class: 'choices' });
    const items = cameras.map((cam) => {
      const id = uid('camopt');
      const input = h('input', { id, type: 'radio', name, value: cam.id, checked: cam.id === chosen });
      const labelEl = h('label', { class: 'choice', for: id },
        input,
        h('span', { class: 'choice-text' },
          h('span', { class: 'choice-title', text: cam.name }),
          h('span', { class: 'choice-help', text: [kindLabel(cam.kind), cam.detail].filter(Boolean).join(' · ') }),
        ),
      );
      input.addEventListener('change', () => {
        chosen = cam.id;
        items.forEach((x) => x.labelEl.classList.toggle('is-checked', x.labelEl === labelEl));
        connectBtn.disabled = busy;
      });
      choices.append(labelEl);
      return { labelEl, cam };
    });
    items.forEach((x) => x.labelEl.classList.toggle('is-checked', x.cam.id === chosen));
    listHost.append(h('fieldset', { class: 'choice-group' },
      h('legend', { class: 'field-label', text: t('forms.cameras.found') }),
      choices,
    ));
    connectBtn.disabled = busy || !chosen;
  };

  async function load() {
    setBusy(true);
    say(t('forms.cameras.searching'));
    try {
      const list = await api.cameras();
      cameras = Array.isArray(list) ? list : [];
    } catch (err) {
      cameras = [];
      say(errorMessage(err), 'error');
      renderList();
      setBusy(false);
      return;
    }
    if (cameras.length === 1) chosen = cameras[0].id;
    if (!cameras.some((c) => c.id === chosen)) chosen = '';
    renderList();
    if (connected && connected.id === chosen) {
      const cam = cameras.find((c) => c.id === chosen);
      say(t('forms.cameras.connected', { name: cam?.name ?? connected.name ?? chosen }), 'ok');
      live?.start();
    } else {
      say('');
      live?.stop();
    }
    setBusy(false);
  }

  async function connect() {
    if (!chosen || busy) return;
    const cam = cameras.find((c) => c.id === chosen);
    setBusy(true);
    say(t('forms.cameras.connecting'));
    try {
      const res = await api.connectCamera(chosen);
      if (res && res.ok === false) {
        say(res.error || t('forms.cameras.failed'), 'error');
        return;
      }
      say(t('forms.cameras.connected', { name: cam?.name ?? chosen }), 'ok');
      onConnected?.(chosen);
      live?.start();
    } catch (err) {
      say(errorMessage(err), 'error');
    } finally {
      setBusy(false);
    }
  }

  connectBtn.addEventListener('click', connect);
  searchBtn.addEventListener('click', load);

  return {
    node: root,
    init: load,
    destroy() {
      live?.stop();
    },
  };
}
