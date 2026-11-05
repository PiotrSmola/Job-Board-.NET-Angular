import { HttpErrorResponse } from '@angular/common/http';

/** 401 i 403 obsługuje globalnie authInterceptor (wylogowanie / komunikat) – komponenty nie dublują komunikatu. */
export function isHandledByInterceptor(error: unknown): boolean {
  return error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403);
}

// Znane komunikaty API (po angielsku) tłumaczone na polski
const KNOWN_API_MESSAGES: Record<string, string> = {
  'You have already applied to this job': 'Już aplikowałeś na tę ofertę',
  'Job offer not found': 'Oferta nie została znaleziona',
  'Application not found': 'Aplikacja nie została znaleziona',
  'This job offer is not accepting applications': 'Ta oferta nie przyjmuje już aplikacji',
  'Expiration date must be in the future to publish': 'Aby opublikować ofertę, ustaw datę wygaśnięcia w przyszłości',
  'Application has been withdrawn by the candidate': 'Kandydat wycofał tę aplikację',
  'Only the candidate can withdraw an application': 'Tylko kandydat może wycofać aplikację',
  'Application already has this status - add a comment to record a note':
    'Aplikacja ma już ten status – dodaj komentarz, aby zapisać notatkę',
  'Application cannot be withdrawn in its current status': 'Tej aplikacji nie można już wycofać',
  'Application status was changed by another request': 'Status aplikacji został w międzyczasie zmieniony – odśwież dane'
};

/**
 * Komunikat błędu HTTP dla użytkownika: przetłumaczony `message` z API dla błędów 4xx,
 * ogólny tekst dla błędów walidacji (ValidationProblemDetails) i braku połączenia, w pozostałych przypadkach `fallback`.
 * Nigdy nie zwraca surowego `error.message` Angulara („Http failure response…”).
 */
export function getApiErrorMessage(error: unknown, fallback: string): string {
  if (!(error instanceof HttpErrorResponse)) return fallback;

  if (error.status === 0) return 'Brak połączenia z serwerem';
  if (error.status >= 500) return fallback;

  const body = error.error;
  const message = body?.message;
  if (typeof message === 'string' && message.trim()) {
    return KNOWN_API_MESSAGES[message] ?? message;
  }

  if (error.status === 400 && body?.errors && typeof body.errors === 'object') {
    return 'Przesłane dane są nieprawidłowe';
  }

  return fallback;
}
