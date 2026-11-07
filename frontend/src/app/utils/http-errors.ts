import { HttpErrorResponse } from '@angular/common/http';
import { FORBIDDEN_API_MESSAGES } from './forbidden-messages';

/** 401 i 403 obsługuje globalnie authInterceptor (wylogowanie / komunikat) – komponenty nie dublują komunikatu. */
export function isHandledByInterceptor(error: unknown): boolean {
  return error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403);
}

/** Zasób nie istnieje (404) – oczekiwany wynik (np. usunięta oferta), nie błąd do logowania w konsoli. */
export function isNotFound(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status === 404;
}

const TOO_MANY_REQUESTS = 'Too many requests - try again later';
const TOO_MANY_REQUESTS_PL = 'Zbyt wiele żądań – spróbuj ponownie za kilka minut';
/** 413 bez komunikatu API – limit nginx dla uploadu załączników (strona HTML). */
const FILES_TOO_LARGE_PL = 'Przesyłane pliki są za duże – maksymalnie 5 MB na plik i 25 MB łącznie';

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
  // kategorie ofert
  'Category name or slug is already taken': 'Kategoria o takiej nazwie lub slugu już istnieje',
  'Category not found': 'Kategoria nie została znaleziona',
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
  'Invalid attachment kind': 'Nieprawidłowy rodzaj załącznika',
  // statystyki
  'Date range cannot exceed 366 days': 'Zakres dat nie może przekraczać 366 dni',
  'Invalid date range': 'Nieprawidłowy zakres dat',
  // błędy odczytu żądania (BadHttpRequestException w backendzie)
  'Request body too large': 'Przesyłane dane są za duże',
  'Request timed out': 'Przekroczono czas wysyłania danych – spróbuj ponownie',
  'Invalid request': 'Nieprawidłowe żądanie',
  // konto, hasło i e-mail
  'Current password is incorrect': 'Obecne hasło jest nieprawidłowe',
  'Administrator accounts cannot be deleted here': 'Konta administratora nie można usunąć w tym miejscu',
  'You are the last member of a company with job offers':
    'Jesteś ostatnim członkiem firmy, która ma oferty pracy – nie możesz teraz usunąć konta',
  'Invalid or expired confirmation link': 'Link potwierdzający jest nieprawidłowy lub wygasł',
  'Email is already confirmed': 'Adres e-mail jest już potwierdzony',
  'Invalid or expired reset link': 'Link do zmiany hasła jest nieprawidłowy lub wygasł',
  // 423 – zbyt wiele błędnych haseł (logowanie, zmiana hasła, usunięcie konta)
  'Account is temporarily locked due to too many failed login attempts':
    'Konto zostało tymczasowo zablokowane po zbyt wielu nieudanych próbach. Spróbuj ponownie za kilka minut.',
  // dokumenty profilu i dołączanie ich do aplikacji
  'A profile can have at most 10 documents': 'Profil może mieć maksymalnie 10 dokumentów',
  'Total size of profile documents cannot exceed 25 MB': 'Łączny rozmiar dokumentów profilu nie może przekroczyć 25 MB',
  'Document not found': 'Nie znaleziono dokumentu',
  'No documents selected': 'Nie wybrano dokumentów z profilu',
  // galeria i tło ofert firmy
  'A company can have at most 20 photos': 'Firma może mieć maksymalnie 20 zdjęć',
  'Unsupported image type - upload a PNG or JPEG file': 'Nieobsługiwany typ obrazu – prześlij plik PNG lub JPEG',
  'Image dimensions are too large - maximum is 12000 px per side and 40 megapixels':
    'Obraz ma za duże wymiary – maksymalnie 12000 px na bok i 40 megapikseli',
  "No file uploaded - send images in the 'files' form field": 'Nie wybrano zdjęć do przesłania',
  "No file uploaded - send the image in the 'file' form field": 'Nie wybrano obrazu do przesłania',
  'Request is too large - upload at most 25 MB at once': 'Przesyłane pliki są za duże – jednorazowo można wysłać do 25 MB',
  'Photo not found': 'Nie znaleziono zdjęcia',
  'Invalid photo order': 'Nieprawidłowa kolejność zdjęć – odśwież galerię',
  'Offer background not found': 'Firma nie ma tła ofert',
  [TOO_MANY_REQUESTS]: TOO_MANY_REQUESTS_PL,
  // m.in. 'Company must be approved before publishing offers'
  ...FORBIDDEN_API_MESSAGES
};

/** Tłumaczenie znanego komunikatu API (tylko własne klucze słownika – nie np. „constructor”). */
function knownApiMessage(message: unknown): string | undefined {
  return typeof message === 'string' && Object.hasOwn(KNOWN_API_MESSAGES, message) ? KNOWN_API_MESSAGES[message] : undefined;
}

/**
 * Komunikat błędu HTTP dla użytkownika: przetłumaczony `message` z API dla błędów 4xx,
 * ogólny tekst dla błędów walidacji (ValidationProblemDetails) i braku połączenia, w pozostałych przypadkach `fallback`.
 * Nigdy nie zwraca surowego `error.message` Angulara („Http failure response…”).
 */
export function getApiErrorMessage(error: unknown, fallback: string): string {
  if (!(error instanceof HttpErrorResponse)) return fallback;

  if (error.status === 0) return 'Brak połączenia z serwerem';
  if (error.status >= 500) return fallback;
  // Rate limiting – treść może być pusta (np. odrzucenie przez proxy)
  if (error.status === 429) return TOO_MANY_REQUESTS_PL;

  const body = error.error;
  const message = body?.message;
  // Limit rozmiaru żądania: backend zwraca JSON ze znanym komunikatem, nginx – stronę HTML (upload załączników)
  if (error.status === 413) {
    return knownApiMessage(message) ?? FILES_TOO_LARGE_PL;
  }
  if (typeof message === 'string' && message.trim()) {
    return knownApiMessage(message) ?? message;
  }

  if (error.status === 400 && body?.errors && typeof body.errors === 'object') {
    return 'Przesłane dane są nieprawidłowe';
  }

  return fallback;
}
