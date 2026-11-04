import { HttpErrorResponse } from '@angular/common/http';

/** 401 i 403 obsługuje globalnie authInterceptor (wylogowanie / komunikat) – komponenty nie dublują komunikatu. */
export function isHandledByInterceptor(error: unknown): boolean {
  return error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403);
}

// Znane komunikaty API (po angielsku) tłumaczone na polski
const KNOWN_API_MESSAGES: Record<string, string> = {
  'You have already applied to this job': 'Już aplikowałeś na tę ofertę',
  'Job offer not found': 'Oferta nie została znaleziona',
  'Application not found': 'Aplikacja nie została znaleziona'
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
