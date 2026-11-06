// Czyste funkcje do skalowania i budowy ścieżek wykresów SVG (bez bibliotek wykresów).

export interface NiceScale {
  /** Górna granica osi (wielokrotność kroku, co najmniej maksimum danych). */
  max: number;
  /** Wartości linii siatki od 0 do `max` (całkowite – statystyki to liczniki). */
  ticks: number[];
}

/**
 * Skala osi Y dla liczników: krok z ciągu 1-2-5 × 10^k, zawsze całkowity.
 * Puste dane, same zera i wartości niepoprawne dają skalę 0–1 (linia płaska na dole, bez dzielenia przez zero).
 */
export function niceScale(maxValue: number, targetTicks = 4): NiceScale {
  if (!Number.isFinite(maxValue) || maxValue <= 0) {
    return { max: 1, ticks: [0, 1] };
  }

  const rawStep = maxValue / Math.max(1, targetTicks);
  const magnitude = Math.pow(10, Math.floor(Math.log10(rawStep)));
  const normalized = rawStep / magnitude;
  const factor = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
  const step = Math.max(1, factor * magnitude);
  const max = Math.ceil(maxValue / step) * step;

  const ticks: number[] = [];
  for (let value = 0; value <= max; value += step) {
    ticks.push(value);
  }
  return { max, ticks };
}

/** Funkcja liniowo odwzorowująca [d0, d1] na [r0, r1]; przy d0 = d1 zwraca środek zakresu docelowego. */
export function linearScale(domain: readonly [number, number], range: readonly [number, number]): (value: number) => number {
  const [d0, d1] = domain;
  const [r0, r1] = range;
  if (d0 === d1) {
    return () => (r0 + r1) / 2;
  }
  return value => r0 + ((value - d0) / (d1 - d0)) * (r1 - r0);
}

/** Pozycje X `count` punktów rozłożonych równo w [left, right]; pojedynczy punkt ląduje na środku. */
export function pointPositions(count: number, left: number, right: number): number[] {
  if (count <= 0) return [];
  if (count === 1) return [(left + right) / 2];
  const step = (right - left) / (count - 1);
  return Array.from({ length: count }, (_, index) => left + index * step);
}

function fmt(value: number): string {
  return String(Math.round(value * 100) / 100);
}

/** Ścieżka SVG łamanej przez punkty (`M x y L x y …`); pusta lista daje pusty napis. */
export function buildLinePath(xs: readonly number[], ys: readonly number[]): string {
  const count = Math.min(xs.length, ys.length);
  if (count === 0) return '';

  let path = `M${fmt(xs[0])} ${fmt(ys[0])}`;
  for (let index = 1; index < count; index++) {
    path += ` L${fmt(xs[index])} ${fmt(ys[index])}`;
  }
  return path;
}

/**
 * Indeksy równomiernie rozłożone po `count` punktach, z pierwszym i ostatnim, nie więcej niż `max`.
 * Służy do etykiet osi X (bez nakładania się przy 366 punktach) i do znaczników serii.
 */
export function pickSpreadIndexes(count: number, max: number): number[] {
  if (count <= 0 || max <= 0) return [];
  if (count === 1 || max === 1) return [0];
  if (count <= max) return Array.from({ length: count }, (_, index) => index);

  const indexes = new Set<number>();
  for (let slot = 0; slot < max; slot++) {
    indexes.add(Math.round((slot * (count - 1)) / (max - 1)));
  }
  return [...indexes];
}

export type MarkerShape = 'circle' | 'square' | 'diamond' | 'triangle';

/** Kontur znacznika (zamknięta ścieżka) o promieniu `r` wokół (cx, cy). */
export function markerPath(shape: MarkerShape, cx: number, cy: number, r: number): string {
  switch (shape) {
    case 'square':
      return `M${fmt(cx - r)} ${fmt(cy - r)} h${fmt(2 * r)} v${fmt(2 * r)} h${fmt(-2 * r)} Z`;
    case 'diamond':
      return `M${fmt(cx)} ${fmt(cy - r * 1.2)} L${fmt(cx + r * 1.2)} ${fmt(cy)} L${fmt(cx)} ${fmt(cy + r * 1.2)} L${fmt(cx - r * 1.2)} ${fmt(cy)} Z`;
    case 'triangle':
      return `M${fmt(cx)} ${fmt(cy - r * 1.15)} L${fmt(cx + r * 1.15)} ${fmt(cy + r)} L${fmt(cx - r * 1.15)} ${fmt(cy + r)} Z`;
    default:
      return `M${fmt(cx - r)} ${fmt(cy)} a${fmt(r)} ${fmt(r)} 0 1 0 ${fmt(2 * r)} 0 a${fmt(r)} ${fmt(r)} 0 1 0 ${fmt(-2 * r)} 0 Z`;
  }
}
