// Calcoli di orario nel fuso dell'osservatorio, senza librerie.
// Una notte va da mezzogiorno locale a mezzogiorno locale; id = data di inizio YYYY-MM-DD.

const partsFormatters = new Map();

function formatterFor(timeZone) {
  if (!partsFormatters.has(timeZone)) {
    partsFormatters.set(
      timeZone,
      new Intl.DateTimeFormat('en-US', {
        timeZone,
        hourCycle: 'h23',
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
      }),
    );
  }
  return partsFormatters.get(timeZone);
}

// Ore, minuti e data locali di un istante nel fuso indicato.
export function localParts(date, timeZone) {
  const values = {};
  for (const part of formatterFor(timeZone).formatToParts(date)) {
    if (part.type !== 'literal') values[part.type] = Number(part.value);
  }
  return {
    year: values.year,
    month: values.month,
    day: values.day,
    hour: values.hour % 24,
    minute: values.minute,
    second: values.second,
  };
}

function offsetMs(instant, timeZone) {
  const p = localParts(instant, timeZone);
  const localAsUtc = Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute, p.second);
  return localAsUtc - Math.floor(instant.getTime() / 1000) * 1000;
}

// Istante corrispondente a un'ora locale (y, m, d, h) nel fuso indicato.
export function zonedDate(y, m, d, h, timeZone) {
  const guess = Date.UTC(y, m - 1, d, h);
  const first = offsetMs(new Date(guess), timeZone);
  let ts = guess - first;
  const second = offsetMs(new Date(ts), timeZone);
  if (second !== first) ts = guess - second;
  return new Date(ts);
}

// Id della notte in corso: prima di mezzogiorno locale è ancora la notte di ieri.
export function currentNightId(now, timeZone) {
  const p = localParts(now, timeZone);
  const shift = p.hour < 12 ? -1 : 0;
  const day = new Date(Date.UTC(p.year, p.month - 1, p.day + shift));
  return day.toISOString().slice(0, 10);
}

// Estremi di una notte: da mezzogiorno locale a mezzogiorno del giorno dopo.
export function nightWindow(nightId, timeZone) {
  const [y, m, d] = nightId.split('-').map(Number);
  return {
    start: zonedDate(y, m, d, 12, timeZone),
    end: zonedDate(y, m, d + 1, 12, timeZone),
  };
}

// Fascia di esempio, usata solo se il motore non fornisce sunBands.
export function sampleBands(nightId, timeZone) {
  const [y, m, d] = nightId.split('-').map(Number);
  const at = (day, hour) => zonedDate(y, m, day, hour, timeZone);
  return [
    { phase: 'day', from: at(d, 12), to: at(d, 18) },
    { phase: 'twilight', from: at(d, 18), to: at(d, 19) },
    { phase: 'night', from: at(d, 19), to: at(d + 1, 5) },
    { phase: 'twilight', from: at(d + 1, 5), to: at(d + 1, 6) },
    { phase: 'day', from: at(d + 1, 6), to: at(d + 1, 12) },
  ];
}

// Altezza geometrica del Sole (gradi) a un istante, dalle coordinate del sito.
// Algoritmo solare semplificato (errore di pochi centesimi di grado); senza rifrazione atmosferica.
export function sunAltitude(date, latDeg, lonDeg) {
  const mod360 = (x) => ((x % 360) + 360) % 360;
  const rad = Math.PI / 180;
  const n = date.getTime() / 86400000 + 2440587.5 - 2451545.0; // giorni dal J2000
  const meanLon = mod360(280.46 + 0.9856474 * n);
  const meanAnomaly = mod360(357.528 + 0.9856003 * n) * rad;
  const eclLon = mod360(meanLon + 1.915 * Math.sin(meanAnomaly) + 0.02 * Math.sin(2 * meanAnomaly)) * rad;
  const obliquity = (23.439 - 0.0000004 * n) * rad;
  const ra = Math.atan2(Math.cos(obliquity) * Math.sin(eclLon), Math.cos(eclLon));
  const dec = Math.asin(Math.sin(obliquity) * Math.sin(eclLon));
  const sidereal = mod360(280.46061837 + 360.98564736629 * n) * rad;
  const hourAngle = sidereal + lonDeg * rad - ra;
  const phi = latDeg * rad;
  const sinAlt = Math.sin(phi) * Math.sin(dec) + Math.cos(phi) * Math.cos(dec) * Math.cos(hourAngle);
  return Math.asin(Math.max(-1, Math.min(1, sinAlt))) / rad;
}

// Altezza del Sole sull'intera notte (mezzogiorno a mezzogiorno), a passi di stepMin minuti: [{ ms, alt }].
export function sunCurve(nightId, timeZone, latDeg, lonDeg, stepMin = 5) {
  const { start, end } = nightWindow(nightId, timeZone);
  const step = stepMin * 60000;
  const out = [];
  for (let ms = start.getTime(); ms < end.getTime(); ms += step) {
    out.push({ ms, alt: sunAltitude(new Date(ms), latDeg, lonDeg) });
  }
  out.push({ ms: end.getTime(), alt: sunAltitude(end, latDeg, lonDeg) });
  return out;
}

// Istanti in cui la curva attraversa una soglia (interpolazione lineare tra due campioni).
export function sunCrossings(curve, thresholds) {
  const out = [];
  for (let i = 1; i < curve.length; i++) {
    const a = curve[i - 1];
    const b = curve[i];
    for (const deg of thresholds) {
      if ((a.alt - deg) * (b.alt - deg) < 0) {
        const f = (deg - a.alt) / (b.alt - a.alt);
        out.push({ ms: a.ms + f * (b.ms - a.ms), deg });
      }
    }
  }
  return out;
}
