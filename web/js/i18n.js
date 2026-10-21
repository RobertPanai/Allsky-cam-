// Caricamento delle stringhe, formattazione di numeri e orari, messaggi d'errore.
// Tutte le stringhe visibili stanno in web/i18n/*.json. Per ogni lingua si carica il file base
// {lang}.json e, unite in profondità, le parti opzionali {lang}.forms.json, {lang}.nights.json,
// {lang}.color.json, {lang}.publish.json. Una parte mancante (404) si ignora.

const LOCALE_TAGS = { it: 'it-IT', en: 'en-GB' };
const SUPPORTED = Object.keys(LOCALE_TAGS);
const OPTIONAL_PARTS = ['forms', 'nights', 'color', 'publish'];

let lang = 'it';
let dict = {};
let fallback = {};

export function normalizeLang(code) {
  return SUPPORTED.includes(code) ? code : 'it';
}

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

// Unione profonda: i valori di `extra` vincono su `base`, le sezioni con lo stesso nome si fondono.
export function mergeDeep(base, extra) {
  const out = { ...base };
  for (const [key, value] of Object.entries(extra)) {
    out[key] = isObject(value) && isObject(out[key]) ? mergeDeep(out[key], value) : value;
  }
  return out;
}

async function fetchJson(file, { optional }) {
  let res;
  try {
    res = await fetch(`i18n/${file}`, { cache: 'no-cache' });
  } catch (err) {
    if (optional) return null;
    throw err;
  }
  if (!res.ok) {
    if (optional) return null;
    throw new Error(`i18n ${file}: HTTP ${res.status}`);
  }
  try {
    return await res.json();
  } catch (err) {
    if (optional) {
      console.warn(`i18n ${file}: JSON non valido, parte ignorata`, err);
      return null;
    }
    throw err;
  }
}

async function fetchBundle(code) {
  const base = await fetchJson(`${code}.json`, { optional: false });
  const parts = await Promise.all(
    OPTIONAL_PARTS.map((part) => fetchJson(`${code}.${part}.json`, { optional: true })),
  );
  return parts.reduce((acc, part) => (isObject(part) ? mergeDeep(acc, part) : acc), base);
}

export async function loadLocale(code) {
  lang = normalizeLang(code);
  if (!Object.keys(fallback).length) fallback = await fetchBundle('it');
  if (lang === 'it') {
    dict = fallback;
  } else {
    try {
      dict = await fetchBundle(lang);
    } catch (err) {
      console.warn(err);
      dict = fallback;
      lang = 'it';
    }
  }
  document.documentElement.lang = lang;
  return lang;
}

export function locale() {
  return lang;
}

function lookup(obj, key) {
  return key.split('.').reduce((o, k) => (o && typeof o === 'object' ? o[k] : undefined), obj);
}

export function t(key, vars) {
  let s = lookup(dict, key);
  if (typeof s !== 'string') s = lookup(fallback, key);
  if (typeof s !== 'string') return key;
  if (vars) {
    s = s.replace(/\{(\w+)\}/g, (match, name) => (name in vars ? String(vars[name]) : match));
  }
  return s;
}

// Applica le stringhe agli elementi statici: data-i18n (testo), data-i18n-aria (aria-label).
export function applyI18n(root = document) {
  root.querySelectorAll('[data-i18n]').forEach((el) => {
    el.textContent = t(el.dataset.i18n);
  });
  root.querySelectorAll('[data-i18n-aria]').forEach((el) => {
    el.setAttribute('aria-label', t(el.dataset.i18nAria));
  });
}

// Messaggio leggibile per un ApiError: quello del motore se c'è, altrimenti la stringa per tipo.
export function errorMessage(err) {
  if (err && err.hasMessage && err.message) return err.message;
  const kind = err && err.kind ? err.kind : 'unknown';
  return t(`errors.${kind}`);
}

function tag() {
  return LOCALE_TAGS[lang];
}

// Etichetta BCP-47 della lingua corrente, per Intl.
export function localeTag() {
  return tag();
}

function safeTz(timeZone) {
  try {
    new Intl.DateTimeFormat('en-US', { timeZone });
    return timeZone;
  } catch {
    return undefined;
  }
}

export const fmt = {
  // Numero con cifre fisse e separatori locali, senza l'ambiguità del trattino: usa il segno meno vero.
  number(value, { digits = 0, grouping = true } = {}) {
    if (value === null || value === undefined || Number.isNaN(value)) return '–';
    return new Intl.NumberFormat(tag(), {
      minimumFractionDigits: digits,
      maximumFractionDigits: digits,
      useGrouping: grouping,
    }).format(value);
  },

  signed(value, digits = 1) {
    return fmt.number(value, { digits }).replace('-', '−');
  },

  // Orario locale dell'osservatorio: HH:MM oppure HH:MM:SS.
  time(date, timeZone, { seconds = false } = {}) {
    return new Intl.DateTimeFormat(tag(), {
      hour: '2-digit',
      minute: '2-digit',
      second: seconds ? '2-digit' : undefined,
      hourCycle: 'h23',
      timeZone: safeTz(timeZone),
    }).format(date);
  },

  date(date, timeZone) {
    return new Intl.DateTimeFormat(tag(), {
      day: 'numeric',
      month: 'long',
      timeZone: safeTz(timeZone),
    }).format(date);
  },
};
