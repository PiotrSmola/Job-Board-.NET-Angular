/** Nazwa aplikacji – druga część tytułu każdej strony („Oferty pracy – Job Board”). */
export const APP_NAME = 'Job Board';

/**
 * Klucz w `data` trasy: tytuł zależy od wczytanych danych (np. tytuł oferty). Tytuł z `title` trasy jest wtedy tylko
 * zastępczy i nie jest ogłaszany po nawigacji – widok ustawia i ogłasza właściwy przez `PageTitleService.set()`.
 */
export const DYNAMIC_TITLE = 'dynamicTitle';

/** Pełny tytuł dokumentu: „<część> – Job Board” albo sama nazwa aplikacji. */
export function formatPageTitle(part: string | null | undefined): string {
  const trimmed = part?.trim();
  return trimmed ? `${trimmed} – ${APP_NAME}` : APP_NAME;
}
