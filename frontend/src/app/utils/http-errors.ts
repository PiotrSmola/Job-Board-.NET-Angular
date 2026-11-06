import { HttpErrorResponse } from '@angular/common/http';
import { FORBIDDEN_API_MESSAGES } from './forbidden-messages';

/** 401 i 403 obsługuje globalnie authInterceptor (wylogowanie / komunikat) – komponenty nie dublują komunikatu. */
export function isHandledByInterceptor(error: unknown): boolean {
  return error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403);
}

const TOO_MANY_REQUESTS = 'Too many requests - try again later';
const TOO_MANY_REQUESTS_PL = 'Zbyt wiele żądań – spróbuj ponownie za kilka minut';

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
  'Application status was changed by another request': 'Status aplikacji został w międzyczasie zmieniony – odśwież dane',
  'Email is already taken': 'Ten adres email jest już zajęty',
  'Company name is already taken': 'Ta nazwa firmy jest już zajęta',
  'Company has job offers or members': 'Nie można usunąć firmy, która ma oferty lub członków zespołu',
  'Company not found': 'Firma nie została znaleziona',
  'Job offer has applications - close it instead': 'Oferta ma aplikacje – zamiast usuwać, zamknij ją.',
  // załączniki aplikacji
  'Unsupported file type - upload a PDF, DOCX, PNG or JPEG file': 'Nieobsługiwany typ pliku – prześlij plik PDF, DOCX, PNG lub JPEG',
  'File is too large - maximum size is 5 MB': 'Plik jest za duży – maksymalny rozmiar to 5 MB',
  'File is empty': 'Plik jest pusty',
  'Total size of attachments cannot exceed 25 MB': 'Łączny rozmiar załączników nie może przekroczyć 25 MB',
  'An application can have at most 10 attachments': 'Aplikacja może mieć maksymalnie 10 załączników',
  "No file uploaded - send files in the 'files' form field": 'Nie wybrano plików do przesłania',
  'Invalid multipart form data': 'Nie udało się odczytać przesłanych plików',
  'Attachments cannot be added in the current application status': 'Na tym etapie rekrutacji nie można już dodawać załączników',
  'Attachment not found': 'Nie znaleziono załącznika',
  // statystyki
  'Date range cannot exceed 366 days': 'Zakres dat nie może przekraczać 366 dni',
  'Invalid date range': 'Nieprawidłowy zakres dat',
  [TOO_MANY_REQUESTS]: TOO_MANY_REQUESTS_PL,
  // m.in. 'Company must be approved before publishing offers'
  ...FORBIDDEN_API_MESSAGES
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
  // Limit rozmiaru żądania serwera (Kestrel / nginx) – odpowiedź bez treści albo HTML z nginx
  if (error.status === 413) return 'Przesyłane pliki są za duże – maksymalnie 5 MB na plik i 25 MB łącznie';
  // Rate limiting – treść może być pusta (np. odrzucenie przez proxy)
  if (error.status === 429) return TOO_MANY_REQUESTS_PL;

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
