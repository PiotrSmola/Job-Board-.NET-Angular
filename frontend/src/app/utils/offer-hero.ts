import { MAX_BACKGROUND_DIM, MIN_BACKGROUND_DIM } from '../models/company-media.model';

/**
 * Najsłabsze efektywne krycie maski nagłówka oferty (%), liczone dla najgorszego przypadku – całkowicie białego obrazu.
 * Kolor strony (#121417) z kryciem 82 % nad bielą daje tło, na którym najjaśniejszy token tekstu w nagłówku,
 * `--color-text-secondary` (#a7adb5, np. ikona „Zapisz”), ma 4,73:1, `--color-accent` (link „Wróć do wyników”,
 * link firmy) 6,13:1, `--color-text` 8,95:1 – wszystkie ≥ 4,5:1 (WCAG 1.4.3). W dół gradient tylko ciemnieje.
 * `--color-text-tertiary` wymagałby 91 % – nie używaj go w nagłówku z obrazem.
 */
export const HERO_MIN_OPACITY = 82;
/** Krycie przy najmocniejszym ustawieniu suwaka (95) – bez zmian względem ustawienia. */
export const HERO_MAX_OPACITY = MAX_BACKGROUND_DIM;

/**
 * Efektywne krycie maski dla przyciemnienia `dim` z ustawień firmy (API: 55–95): suwak przeskalowany liniowo
 * na 82–95 % (więcej = ciemniej, jak dotąd), środek gradientu 40 % drogi do pełnego tła (makieta: góra → mocniej → tło).
 */
export function offerHeroOpacity(dim: number): { top: number; middle: number } {
  const raw = Number.isFinite(dim) ? dim : MIN_BACKGROUND_DIM;
  const clamped = Math.min(MAX_BACKGROUND_DIM, Math.max(MIN_BACKGROUND_DIM, raw));
  const share = (clamped - MIN_BACKGROUND_DIM) / (MAX_BACKGROUND_DIM - MIN_BACKGROUND_DIM);
  const top = Math.round(HERO_MIN_OPACITY + share * (HERO_MAX_OPACITY - HERO_MIN_OPACITY));
  const middle = Math.round(top + (100 - top) * 0.4);
  return { top, middle };
}

/**
 * Maska nagłówka oferty (jedyny dozwolony gradient): kolor strony z efektywnym kryciem → mocniej → pełny kolor strony.
 * Wspólna dla strony oferty i podglądu w panelu pracodawcy – podgląd wygląda dokładnie jak nagłówek.
 */
export function offerHeroMask(dim: number): string {
  const { top, middle } = offerHeroOpacity(dim);
  return `linear-gradient(180deg, color-mix(in srgb, var(--color-bg) ${top}%, transparent) 0%, ` +
    `color-mix(in srgb, var(--color-bg) ${middle}%, transparent) 55%, var(--color-bg) 100%)`;
}
