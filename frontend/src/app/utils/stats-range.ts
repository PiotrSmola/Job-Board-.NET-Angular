import { StatsRange } from '../models/stats.model';

/** Maksymalna długość zakresu (dni, włącznie) akceptowana przez API. */
export const MAX_RANGE_DAYS = 366;
export const DEFAULT_RANGE_DAYS = 30;

export type RangePreset = '7d' | '30d' | '90d' | '12m';

export const RANGE_PRESETS: readonly { id: RangePreset; label: string }[] = [
  { id: '7d', label: '7 dni' },
  { id: '30d', label: '30 dni' },
  { id: '90d', label: '90 dni' },
  { id: '12m', label: '12 miesięcy' }
];

const DAY_MS = 24 * 60 * 60 * 1000;
const DAY_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

/** Dzień (YYYY-MM-DD) jako data UTC o północy; null dla niepoprawnego/nieistniejącego dnia (np. 2026-02-30). */
export function parseDay(day: string | null | undefined): Date | null {
  const match = day ? DAY_PATTERN.exec(day) : null;
  if (!match) return null;

  const [year, month, date] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const parsed = new Date(Date.UTC(year, month - 1, date));
  const valid = parsed.getUTCFullYear() === year && parsed.getUTCMonth() === month - 1 && parsed.getUTCDate() === date;
  return valid ? parsed : null;
}

export function formatDay(date: Date): string {
  return date.toISOString().slice(0, 10);
}

/** Dzień kalendarzowy wybrany przez użytkownika w kontrolce (składowe lokalne – to, co widzi w kalendarzu). */
export function formatLocalDay(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${String(date.getFullYear()).padStart(4, '0')}-${month}-${day}`;
}

/** Liczba dni zakresu włącznie (from = to → 1); NaN dla niepoprawnych dni. */
export function rangeLengthDays(range: StatsRange): number {
  const from = parseDay(range.from);
  const to = parseDay(range.to);
  if (!from || !to) return NaN;
  return Math.round((to.getTime() - from.getTime()) / DAY_MS) + 1;
}

export function isValidRange(range: StatsRange): boolean {
  const length = rangeLengthDays(range);
  return Number.isFinite(length) && length >= 1 && length <= MAX_RANGE_DAYS;
}

/** Zakres kończący się dziś (UTC – dni statystyk są liczone w UTC). */
export function presetRange(preset: RangePreset, now: Date = new Date()): StatsRange {
  const today = parseDay(formatDay(now)) as Date;
  const from = new Date(today);

  if (preset === '12m') {
    from.setUTCFullYear(from.getUTCFullYear() - 1);
    from.setUTCDate(from.getUTCDate() + 1);
  } else {
    const days = preset === '7d' ? 7 : preset === '30d' ? 30 : 90;
    from.setUTCDate(from.getUTCDate() - (days - 1));
  }
  return { from: formatDay(from), to: formatDay(today) };
}

export function defaultRange(now: Date = new Date()): StatsRange {
  return presetRange('30d', now);
}

/** Preset odpowiadający zakresowi (kończącemu się dziś) albo null dla zakresu własnego. */
export function matchPreset(range: StatsRange, now: Date = new Date()): RangePreset | null {
  const found = RANGE_PRESETS.find(preset => {
    const candidate = presetRange(preset.id, now);
    return candidate.from === range.from && candidate.to === range.to;
  });
  return found?.id ?? null;
}

/** Zakres z parametrów URL; przy braku lub niepoprawnych wartościach (też > 366 dni, from > to) – zakres domyślny. */
export function rangeFromParams(from: string | null, to: string | null, now: Date = new Date()): StatsRange {
  if (from && to && isValidRange({ from, to })) {
    return { from, to };
  }
  return defaultRange(now);
}

/** Wszystkie dni zakresu (włącznie) – do uzupełniania brakujących punktów szeregu. */
export function enumerateDays(range: StatsRange): string[] {
  const length = rangeLengthDays(range);
  const start = parseDay(range.from);
  if (!start || !Number.isFinite(length) || length < 1 || length > MAX_RANGE_DAYS) return [];

  return Array.from({ length }, (_, index) => formatDay(new Date(start.getTime() + index * DAY_MS)));
}

const SHORT_MONTHS = ['sty', 'lut', 'mar', 'kwi', 'maj', 'cze', 'lip', 'sie', 'wrz', 'paź', 'lis', 'gru'];

/** „5 lis” – krótka etykieta osi (bez zależności od ICU w środowisku testowym). */
export function formatShortDay(day: string): string {
  const parsed = parseDay(day);
  return parsed ? `${parsed.getUTCDate()} ${SHORT_MONTHS[parsed.getUTCMonth()]}` : day;
}

/** „05.11.2026” – pełna data w tabelach i opisach. */
export function formatFullDay(day: string): string {
  const parsed = parseDay(day);
  if (!parsed) return day;
  const dd = String(parsed.getUTCDate()).padStart(2, '0');
  const mm = String(parsed.getUTCMonth() + 1).padStart(2, '0');
  return `${dd}.${mm}.${parsed.getUTCFullYear()}`;
}

/** Data lokalna (północ) dla dnia YYYY-MM-DD – wartość dla kontrolki MatDatepicker. */
export function dayToLocalDate(day: string): Date | null {
  const parsed = parseDay(day);
  return parsed ? new Date(parsed.getUTCFullYear(), parsed.getUTCMonth(), parsed.getUTCDate()) : null;
}
