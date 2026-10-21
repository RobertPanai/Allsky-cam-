// Schermata Pubblicazione: invio periodico dell'ultima immagine a un server web.
// L'interruttore si salva subito; il resto del form si salva con Salva. Token e valore dell'header
// sono segreti: il motore non li restituisce mai, quindi il campo mostra «salvato» e si svuota dopo il salvataggio.
import { api } from '../api.js';
import { t, errorMessage, localeTag, fmt } from '../i18n.js';
import {
  h, clone, sameValue, setPath, inputField, validateAll, applyServerErrors, formSection, fieldGrid, saveBar,
} from '../form-kit.js';

const POLL_MS = 10000;
const SVG_NS = 'http://www.w3.org/2000/svg';
const STATE_TONE = { disabled: 'muted', idle: 'ok', sending: 'accent', backoff: 'warn', suspended: 'err' };
const ERROR_CODES = ['auth', 'rejected', 'too_large', 'rate_limited', 'server_error', 'network', 'timeout', 'bad_response', 'invalid_settings'];
const TEST_CODES = ['ok', 'auth', 'no_health_endpoint', 'network', 'timeout', 'server_error', 'invalid_settings'];
const TEST_TONE = { ok: 'ok', no_health_endpoint: 'warn', timeout: 'warn', server_error: 'warn' };
const ICON_PATHS = {
  ok: ['M4.5 8.6l2.4 2.4 4.6-5'],
  warn: ['M8 4.5v4.6', 'M8 11.5v.2'],
  err: ['M5 5l6 6', 'M11 5l-6 6'],
};

// Bozza tra un ridisegno e l'altro (per esempio il cambio lingua), finché il server non cambia le impostazioni.
let session = null;

export async function mount(root, ctx) {
  root.replaceChildren(h('p', { class: 'loading muted', text: t('app.loading') }));

  let settings;
  try {
    settings = fromServer(await api.publishSettings());
  } catch {
    if (stillHere()) root.replaceChildren(h('p', { class: 'notice', role: 'alert', text: t('publish.errors.load') }));
    return null;
  }
  let status = null;
  try {
    status = await api.publishStatus();
  } catch {
    status = null;
  }
  if (!stillHere()) return null;

  if (!session || !dirtyOf(session)) {
    session = { draft: clone(settings), baseline: clone(settings) };
  } else {
    session.baseline = clone(settings);
  }
  const { draft, baseline } = session;

  const fields = [];
  const add = (field) => {
    fields.push(field);
    return field;
  };
  const isDirty = () => dirtyOf(session) || token.dirty() || extraSecret.dirty();
  const refreshDirty = () => bar.setDirty(isDirty());
  const onChange = () => refreshDirty();

  // ----- Interruttore principale -----
  const toggleErr = h('p', { class: 'field-error', role: 'alert', hidden: true });
  const toggle = h('input', {
    id: 'publish-enabled',
    class: 'publish-toggle',
    type: 'checkbox',
    role: 'switch',
    'aria-labelledby': 'publish-toggle-title',
    'aria-describedby': 'publish-toggle-state',
  });
  toggle.checked = Boolean(draft.enabled);
  const toggleDot = h('span', { class: 'pub-dot', 'aria-hidden': 'true' });
  const toggleState = h('span', { id: 'publish-toggle-state' });
  const hero = h('section', { class: 'publish-hero' },
    h('label', { class: 'publish-hero-row', for: 'publish-enabled' },
      h('span', { class: 'publish-hero-text' },
        h('span', { class: 'publish-hero-title', id: 'publish-toggle-title', text: t('publish.toggle.label') }),
        h('span', { class: 'publish-hero-state' }, toggleDot, toggleState),
        h('span', { class: 'publish-hero-help', text: t('publish.toggle.help') }),
      ),
      toggle,
    ),
    toggleErr,
  );

  // ----- Pannello di stato -----
  const nameDot = h('span', { class: 'pub-dot', 'aria-hidden': 'true' });
  const nameText = h('span');
  const helpEl = h('p', { class: 'section-lede' });
  const lastDate = h('span');
  const lastAgo = h('span', { class: 'pub-sub' });
  const errText = h('span');
  const errDetail = h('span', { class: 'pub-sub' });
  const errTime = h('span', { class: 'pub-sub' });
  const queueEl = h('span');
  const queueUnit = h('span', { class: 'pub-unit' });
  const sentEl = h('span');
  const discardedEl = h('span');
  const nextText = h('span');
  const nextAgo = h('span', { class: 'pub-sub' });
  const nextFact = fact(t('publish.facts.nextAttempt'), [nextText, nextAgo], { wide: false });
  const view = {
    dots: [nameDot, toggleDot],
    nameText,
    toggleState,
    helpEl,
    lastDate,
    lastAgo,
    errText,
    errDetail,
    errTime,
    queueEl,
    queueUnit,
    sentEl,
    discardedEl,
    nextFact,
    nextText,
    nextAgo,
  };

  const stateBlock = h('div', { class: 'publish-state' },
    h('p', { class: 'publish-state-name' }, nameDot, nameText),
    helpEl,
  );
  const facts = h('dl', { class: 'publish-facts' },
    fact(t('publish.facts.lastSuccess'), [lastDate, lastAgo], { wide: true }).node,
    fact(t('publish.facts.lastError'), [errText, errDetail, errTime], { wide: true }).node,
    fact(t('publish.facts.queue'), [queueEl, queueUnit], { count: true }).node,
    fact(t('publish.facts.sent'), [sentEl], { count: true }).node,
    fact(t('publish.facts.discarded'), [discardedEl], { count: true }).node,
    nextFact.node,
  );

  // ----- Impostazioni del server -----
  const serverUrl = add(inputField({
    draft, path: 'serverUrl', maxLength: 300, required: true, onChange,
    label: t('publish.form.serverUrl.label'), help: t('publish.form.serverUrl.help'),
  }));
  serverUrl.node.querySelector('input').type = 'url';
  serverUrl.node.querySelector('input').placeholder = 'https://example.org';

  const deviceId = add(inputField({
    draft, path: 'deviceId', maxLength: 48, required: true, onChange,
    label: t('publish.form.deviceId.label'), help: t('publish.form.deviceId.help'),
  }));
  const token = add(secretField({ path: 'token', key: 'publish.form.token', has: settings.hasToken, onChange }));
  const profileName = add(inputField({
    draft, path: 'profileName', maxLength: 60, onChange,
    label: t('publish.form.profileName.label'), help: t('publish.form.profileName.help'),
  }));

  // ----- Immagine e frequenza -----
  const interval = add(inputField({
    draft, path: 'intervalSeconds', kind: 'number', integer: true, min: 15, max: 86400, unit: t('publish.form.interval.unit'), onChange,
    label: t('publish.form.interval.label'), help: t('publish.form.interval.help'),
  }));
  const longEdge = add(inputField({
    draft, path: 'longEdgePx', kind: 'number', integer: true, min: 320, max: 4000, unit: t('publish.form.longEdge.unit'), onChange,
    label: t('publish.form.longEdge.label'), help: t('publish.form.longEdge.help'),
  }));
  const quality = add(qualityField(draft, onChange));

  // ----- Header aggiuntivo (sezione richiudibile) -----
  const extraName = add(inputField({
    draft, path: 'extraHeaderName', maxLength: 64, onChange,
    label: t('publish.form.extra.name.label'), help: t('publish.form.extra.name.help'),
  }));
  const extraSecret = add(secretField({ path: 'extraHeaderValue', key: 'publish.form.extra.value', has: settings.hasExtraHeaderValue, onChange }));
  const extra = h('details', { class: 'publish-extra' },
    h('summary', { text: t('publish.form.extra.summary') }),
    h('p', { class: 'field-help', text: t('publish.form.extra.help') }),
    fieldGrid(extraName.node, extraSecret.node),
  );

  // ----- Test di connessione (usa i valori correnti del form, anche se non salvati) -----
  const testBtn = h('button', { type: 'button', class: 'btn' }, t('publish.test.button'));
  const testOut = h('div', { class: 'publish-result', role: 'status', hidden: true });

  // ----- Salvataggio -----
  const bar = saveBar({ onSave: save });

  async function save() {
    if (!validateAll(fields)) {
      bar.say(t('forms.invalid'), 'error');
      return;
    }
    bar.setBusy(true);
    bar.say('');
    try {
      const body = pick(session.draft);
      Object.assign(body, token.payload('token', 'clearToken'), extraSecret.payload('extraHeaderValue', 'clearExtraHeaderValue'));
      const res = await api.savePublishSettings(body);
      if (res && res.ok === false) {
        const errors = Array.isArray(res.errors) ? res.errors : [];
        const rest = applyServerErrors(fields, errors);
        if (errors.some((e) => String(e.path).startsWith('extra'))) extra.open = true;
        bar.say(rest.join(' ') || t('forms.invalid'), 'error');
        return;
      }
      // Il motore conferma solo ok: si rilegge la configurazione per sapere se il token è salvato.
      const fresh = fromServer(await api.publishSettings());
      Object.assign(session.draft, fresh);
      Object.assign(session.baseline, fresh);
      toggle.checked = fresh.enabled;
      fields.forEach((f) => f.refresh?.());
      token.reset(fresh.hasToken);
      extraSecret.reset(fresh.hasExtraHeaderValue);
      bar.say(t('forms.saved'), 'ok');
      refreshDirty();
    } catch (err) {
      bar.say(errorMessage(err), 'error');
    } finally {
      bar.setBusy(false);
    }
  }

  // Corpo del test: valori del form, più i segreti solo se sono stati digitati.
  const testBody = () => {
    const body = pick(draft);
    const tok = token.typed();
    if (tok) body.token = tok;
    const hdr = extraSecret.typed();
    if (hdr) body.extraHeaderValue = hdr;
    return body;
  };

  async function runTest() {
    testBtn.disabled = true;
    testBtn.textContent = t('publish.test.running');
    try {
      const res = await api.publishTest(testBody());
      showTest(res?.ok ? 'ok' : res?.code, res?.message);
    } catch (err) {
      showTest(err?.kind === 'timeout' ? 'timeout' : 'network', errorMessage(err));
    } finally {
      testBtn.disabled = false;
      testBtn.textContent = t('publish.test.button');
    }
  }

  function showTest(code, detail) {
    const key = TEST_CODES.includes(code) ? code : 'unknown';
    const tone = key === 'ok' ? 'ok' : (TEST_TONE[key] ?? 'err');
    testOut.dataset.tone = tone;
    testOut.replaceChildren(
      h('span', { class: 'publish-result-icon' }, icon(tone)),
      h('div', { class: 'publish-result-text' },
        h('p', { class: 'publish-result-main', text: t(`publish.test.codes.${key}`) }),
        detail && detail !== t(`publish.test.codes.${key}`) ? h('p', { class: 'pub-sub', text: detail }) : null,
      ),
    );
    testOut.hidden = false;
  }

  testBtn.addEventListener('click', runTest);

  // ----- Interruttore: si salva subito, con i valori salvati del server -----
  async function setEnabled(want) {
    toggle.disabled = true;
    toggleErr.hidden = true;
    try {
      const res = await api.savePublishSettings(pick({ ...baseline, enabled: want }));
      if (res && res.ok === false) {
        applyServerErrors(fields, res.errors);
        toggle.checked = !want;
        toggleErr.textContent = t('publish.toggle.invalid');
        toggleErr.hidden = false;
        return;
      }
      baseline.enabled = want;
      draft.enabled = want;
      refreshNow();
    } catch {
      toggle.checked = !want;
      toggleErr.textContent = t('publish.toggle.failed');
      toggleErr.hidden = false;
    } finally {
      toggle.disabled = false;
    }
  }
  toggle.addEventListener('change', () => setEnabled(toggle.checked));

  // ----- Pagina -----
  const section = h('section', { class: 'form-page publish-page', 'aria-labelledby': 'publish-title' },
    h('header', { class: 'page-head' },
      h('h1', { id: 'publish-title', class: 'page-title', tabindex: '-1', text: t('publish.title') }),
      h('p', { class: 'lede', text: t('publish.lede') }),
    ),
    hero,
    formSection({ title: t('publish.state.label') }, stateBlock, facts),
    formSection({ title: t('publish.form.sections.server') },
      fieldGrid(serverUrl.node, deviceId.node),
      fieldGrid(token.node, profileName.node),
      h('div', { class: 'publish-test' }, h('div', { class: 'btn-row' }, testBtn), testOut),
    ),
    formSection({ title: t('publish.form.sections.image') },
      fieldGrid(interval.node, longEdge.node),
      quality.node,
    ),
    formSection({ title: t('publish.form.sections.advanced') }, extra),
    bar.node,
  );

  // ----- Stato in tempo reale: evento SSE publish, con rilettura ogni 10 s come riserva -----
  let alive = true;
  let lastEventAt = 0;
  const refreshNow = () => {
    api.publishStatus().then((st) => alive && paintStatus(view, st)).catch(() => alive && paintStatus(view, null));
  };
  const off = ctx.on('publish', (st) => {
    lastEventAt = Date.now();
    paintStatus(view, st);
  });
  const timer = setInterval(async () => {
    if (Date.now() - lastEventAt < POLL_MS) return;
    try {
      const st = await api.publishStatus();
      if (alive) paintStatus(view, st);
    } catch {
      if (alive) paintStatus(view, null);
    }
  }, POLL_MS);

  paintStatus(view, status);
  root.replaceChildren(section);
  refreshDirty();

  return {
    unmount() {
      alive = false;
      off();
      clearInterval(timer);
    },
  };
}

// ----- Dati -----

// Impostazioni salvabili, senza segreti e senza i flag hasToken / hasExtraHeaderValue.
function pick(src) {
  return {
    enabled: Boolean(src.enabled),
    serverUrl: src.serverUrl ?? '',
    deviceId: src.deviceId ?? '',
    profileName: src.profileName ?? '',
    intervalSeconds: src.intervalSeconds ?? 60,
    longEdgePx: src.longEdgePx ?? 1200,
    jpegQuality: src.jpegQuality ?? 75,
    extraHeaderName: src.extraHeaderName ?? '',
  };
}

function fromServer(data) {
  return { ...pick(data), hasToken: Boolean(data.hasToken), hasExtraHeaderValue: Boolean(data.hasExtraHeaderValue) };
}

const dirtyOf = (s) => !sameValue(pick(s.draft), pick(s.baseline));

const stillHere = () => location.hash.replace(/^#\/?/, '').split(/[?/]/)[0] === 'publish';

// ----- Stato: testo e colori (il colore accompagna sempre una parola) -----

function paintStatus(v, st) {
  const known = st && Object.hasOwn(STATE_TONE, st.state) ? st.state : null;
  const tone = known ? STATE_TONE[known] : 'muted';
  const name = known ? t(`publish.state.names.${known}`) : t('publish.state.names.unknown');
  for (const dot of v.dots) dot.dataset.tone = tone;
  v.nameText.textContent = name;
  v.toggleState.textContent = name;
  v.helpEl.textContent = known ? t(`publish.state.help.${known}`) : t('publish.state.unavailable');

  const ok = st?.lastSuccessUtc;
  v.lastDate.textContent = ok ? dateTime(ok) : t('publish.facts.never');
  v.lastAgo.textContent = ok ? relative(ok) : '';
  v.lastAgo.hidden = !ok;

  const code = st?.lastErrorCode;
  v.errText.textContent = code
    ? t(`publish.errors.codes.${ERROR_CODES.includes(code) ? code : 'unknown'}`)
    : t('publish.facts.none');
  v.errDetail.textContent = st?.lastError ? String(st.lastError) : '';
  v.errDetail.hidden = !st?.lastError;
  v.errTime.textContent = st?.lastErrorUtc ? dateTime(st.lastErrorUtc) : '';
  v.errTime.hidden = !st?.lastErrorUtc;

  const queued = Number.isFinite(st?.queueCount) ? st.queueCount : 0;
  v.queueEl.textContent = fmt.number(queued);
  v.queueUnit.textContent = t(queued === 1 ? 'publish.facts.count.one' : 'publish.facts.count.other');
  v.sentEl.textContent = fmt.number(st?.sentTotal ?? 0);
  v.discardedEl.textContent = fmt.number(st?.discardedTotal ?? 0);

  const next = st?.state === 'backoff' ? st.nextAttemptUtc : null;
  v.nextFact.node.hidden = !next;
  v.nextText.textContent = next ? dateTime(next) : '';
  v.nextAgo.textContent = next ? relative(next) : '';
}

function dateTime(iso) {
  if (!Number.isFinite(Date.parse(iso))) return '';
  return new Intl.DateTimeFormat(localeTag(), {
    day: 'numeric', month: 'long', year: 'numeric', hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
  }).format(new Date(iso));
}

// «12 minuti fa», «tra 3 minuti»; sotto il minuto «poco fa» o «tra poco».
function relative(iso) {
  const diff = (Date.parse(iso) - Date.now()) / 1000;
  if (!Number.isFinite(diff)) return '';
  if (Math.abs(diff) < 60) return t(diff < 0 ? 'publish.facts.justNow' : 'publish.facts.soon');
  const rtf = new Intl.RelativeTimeFormat(localeTag(), { numeric: 'always' });
  for (const [unit, seconds] of [['day', 86400], ['hour', 3600], ['minute', 60]]) {
    if (Math.abs(diff) >= seconds) return rtf.format(Math.round(diff / seconds), unit);
  }
  return '';
}

// ----- Pezzi di interfaccia -----

function fact(label, children, { wide = false, count: isCount = false } = {}) {
  const dd = h('dd', { class: isCount ? 'is-count' : undefined }, ...children);
  const node = h('div', { class: wide ? 'publish-fact is-wide' : 'publish-fact' }, h('dt', { text: label }), dd);
  return { node, dd };
}

function icon(tone) {
  const svg = document.createElementNS(SVG_NS, 'svg');
  svg.setAttribute('viewBox', '0 0 16 16');
  svg.setAttribute('aria-hidden', 'true');
  svg.setAttribute('focusable', 'false');
  for (const d of ICON_PATHS[tone] ?? ICON_PATHS.err) {
    const path = document.createElementNS(SVG_NS, 'path');
    path.setAttribute('d', d);
    svg.append(path);
  }
  return svg;
}

// Campo segreto (token, valore dell'header). Il valore non entra mai nella bozza, né come attributo nel DOM:
// resta solo nel campo di input finché non si salva, poi il campo si svuota.
function secretField({ path, key, has, onChange }) {
  const id = `pub-${path}`;
  const errId = `${id}-error`;
  const helpId = `${id}-help`;
  let hasValue = Boolean(has);
  let mode = hasValue ? 'keep' : 'edit'; // keep: salvato | edit: campo aperto | clear: da rimuovere al salvataggio

  const input = h('input', {
    id: `${id}-input`,
    class: 'input',
    type: 'password',
    autocomplete: 'off',
    autocapitalize: 'off',
    spellcheck: 'false',
    'aria-describedby': `${helpId} ${errId}`,
  });
  const savedEl = h('div', { class: 'secret-saved' },
    h('p', { class: 'secret-state', text: t(`${key}.saved`) }),
    h('button', { type: 'button', class: 'btn-text', onclick: () => { mode = 'edit'; sync(); input.focus(); } }, t(`${key}.replace`)),
    h('button', { type: 'button', class: 'btn-text', onclick: () => { mode = 'clear'; input.value = ''; sync(); onChange(); } }, t(`${key}.remove`)),
  );
  const clearEl = h('div', { class: 'secret-saved' },
    h('p', { class: 'secret-state', text: t(`${key}.removing`) }),
    h('button', { type: 'button', class: 'btn-text', onclick: () => { mode = hasValue ? 'keep' : 'edit'; sync(); onChange(); } }, t(`${key}.cancel`)),
  );
  const cancelEl = h('button', {
    type: 'button',
    class: 'btn-text',
    onclick: () => { mode = 'keep'; input.value = ''; sync(); onChange(); },
  }, t(`${key}.cancel`));
  const inputRow = h('div', { class: 'secret-input-row' }, input, cancelEl);
  const errEl = h('p', { class: 'field-error', id: errId, hidden: true });
  const node = h('div', { class: 'field secret-field' },
    h('label', { class: 'field-label', for: input.id, text: t(`${key}.label`) }),
    h('p', { class: 'field-help', id: helpId, text: t(`${key}.help`) }),
    savedEl,
    clearEl,
    inputRow,
    errEl,
  );

  const sync = () => {
    savedEl.hidden = mode !== 'keep';
    clearEl.hidden = mode !== 'clear';
    inputRow.hidden = mode !== 'edit';
    cancelEl.hidden = !hasValue;
  };
  sync();

  input.addEventListener('input', () => {
    errEl.hidden = true;
    input.removeAttribute('aria-invalid');
    node.classList.remove('is-invalid');
    onChange();
  });

  const typed = () => (mode === 'edit' ? input.value.trim() : '');

  return {
    path,
    node,
    validate: () => true,
    showError(message) {
      errEl.textContent = message;
      errEl.hidden = false;
      node.classList.add('is-invalid');
      input.setAttribute('aria-invalid', 'true');
    },
    focus: () => (mode === 'edit' ? input : savedEl.querySelector('button'))?.focus(),
    refresh: sync,
    typed,
    dirty: () => mode === 'clear' || (mode === 'edit' && input.value.trim() !== ''),
    // Campi per il corpo del salvataggio: il valore nuovo, oppure la richiesta di rimozione, oppure nulla.
    payload(valueKey, clearKey) {
      if (mode === 'clear') return { [clearKey]: true };
      const value = typed();
      return value ? { [valueKey]: value } : {};
    },
    // Dopo il salvataggio: il campo si svuota e segue ciò che dice il motore.
    reset(nowHas) {
      hasValue = Boolean(nowHas);
      mode = hasValue ? 'keep' : 'edit';
      input.value = '';
      errEl.hidden = true;
      node.classList.remove('is-invalid');
      sync();
    },
  };
}

// Qualità JPEG: cursore e numero dello stesso valore.
function qualityField(draft, onChange) {
  const field = inputField({
    draft, path: 'jpegQuality', kind: 'number', integer: true, min: 1, max: 100, onChange,
    label: t('publish.form.quality.label'), help: t('publish.form.quality.help'),
  });
  const numInput = field.node.querySelector('.input');
  const range = h('input', {
    class: 'quality-range',
    type: 'range',
    min: 1,
    max: 100,
    step: 1,
    'aria-label': t('publish.form.quality.slider'),
  });
  const syncRange = () => {
    range.value = String(draft.jpegQuality ?? 75);
  };
  syncRange();

  numInput.addEventListener('input', () => {
    const n = Number(numInput.value);
    if (Number.isInteger(n) && n >= 1 && n <= 100) range.value = String(n);
  });
  range.addEventListener('input', () => {
    setPath(draft, 'jpegQuality', Number(range.value));
    field.refresh();
    onChange();
  });
  field.node.querySelector('.input-wrap').after(range);

  return {
    path: 'jpegQuality',
    node: field.node,
    validate: field.validate,
    showError: field.showError,
    focus: field.focus,
    refresh() {
      field.refresh();
      syncRange();
    },
  };
}
