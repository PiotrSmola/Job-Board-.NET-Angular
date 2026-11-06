import { HttpErrorResponse } from '@angular/common/http';
import { getApiErrorMessage, isHandledByInterceptor } from './http-errors';

const FALLBACK = 'Coś poszło nie tak';

function httpError(status: number, error: unknown = null): HttpErrorResponse {
  return new HttpErrorResponse({ status, statusText: 'Error', url: '/api/x', error });
}

describe('http-errors', () => {
  describe('isHandledByInterceptor', () => {
    it.each([401, 403])('is true for %i', status => {
      expect(isHandledByInterceptor(httpError(status))).toBe(true);
    });

    it.each([0, 400, 404, 500])('is false for %i', status => {
      expect(isHandledByInterceptor(httpError(status))).toBe(false);
    });

    it('is false for non-HTTP errors', () => {
      expect(isHandledByInterceptor(new Error('x'))).toBe(false);
    });
  });

  describe('getApiErrorMessage', () => {
    it.each([
      ['You have already applied to this job', 'Już aplikowałeś na tę ofertę'],
      ['Job offer not found', 'Oferta nie została znaleziona'],
      ['Application not found', 'Aplikacja nie została znaleziona'],
      ['This job offer is not accepting applications', 'Ta oferta nie przyjmuje już aplikacji'],
      ['Expiration date must be in the future to publish', 'Aby opublikować ofertę, ustaw datę wygaśnięcia w przyszłości'],
      ['Application has been withdrawn by the candidate', 'Kandydat wycofał tę aplikację'],
      ['Only the candidate can withdraw an application', 'Tylko kandydat może wycofać aplikację'],
      ['Application already has this status - add a comment to record a note', 'Aplikacja ma już ten status – dodaj komentarz, aby zapisać notatkę'],
      ['Application cannot be withdrawn in its current status', 'Tej aplikacji nie można już wycofać'],
      ['Application status was changed by another request', 'Status aplikacji został w międzyczasie zmieniony – odśwież dane'],
      ['Email is already taken', 'Ten adres email jest już zajęty'],
      ['Company name is already taken', 'Ta nazwa firmy jest już zajęta'],
      ['Company must be approved before publishing offers', 'Firma musi zostać zatwierdzona, zanim opublikujesz ofertę'],
      ['Company has job offers or members', 'Nie można usunąć firmy, która ma oferty lub członków zespołu'],
      ['Job offer has applications - close it instead', 'Oferta ma aplikacje – zamiast usuwać, zamknij ją.'],
      ['Unsupported file type - upload a PDF, DOCX, PNG or JPEG file', 'Nieobsługiwany typ pliku – prześlij plik PDF, DOCX, PNG lub JPEG'],
      ['File is too large - maximum size is 5 MB', 'Plik jest za duży – maksymalny rozmiar to 5 MB'],
      ['File is empty', 'Plik jest pusty'],
      ['Total size of attachments cannot exceed 25 MB', 'Łączny rozmiar załączników nie może przekroczyć 25 MB'],
      ['An application can have at most 10 attachments', 'Aplikacja może mieć maksymalnie 10 załączników'],
      ["No file uploaded - send files in the 'files' form field", 'Nie wybrano plików do przesłania'],
      ['Invalid multipart form data', 'Nie udało się odczytać przesłanych plików'],
      ['Attachments cannot be added in the current application status', 'Na tym etapie rekrutacji nie można już dodawać załączników'],
      ['Attachment not found', 'Nie znaleziono załącznika'],
      ['Date range cannot exceed 366 days', 'Zakres dat nie może przekraczać 366 dni'],
      ['Invalid date range', 'Nieprawidłowy zakres dat'],
      ['Too many requests - try again later', 'Zbyt wiele żądań – spróbuj ponownie za kilka minut']
    ])('translates the known API message "%s"', (message, expected) => {
      expect(getApiErrorMessage(httpError(400, { message }), FALLBACK)).toBe(expected);
    });

    it('translates a known message also for a 409 conflict', () => {
      const error = httpError(409, { message: 'Application status was changed by another request' });
      expect(getApiErrorMessage(error, FALLBACK)).toBe('Status aplikacji został w międzyczasie zmieniony – odśwież dane');
    });

    it('returns an unknown API message from a 4xx response as is', () => {
      expect(getApiErrorMessage(httpError(400, { message: 'Something specific' }), FALLBACK)).toBe('Something specific');
    });

    it('returns a generic text for ValidationProblemDetails', () => {
      const body = { title: 'One or more validation errors occurred.', status: 400, errors: { SalaryMin: ['bad'] } };
      expect(getApiErrorMessage(httpError(400, body), FALLBACK)).toBe('Przesłane dane są nieprawidłowe');
    });

    it.each([
      [413, 'Request body too large', 'Przesyłane dane są za duże'],
      [408, 'Request timed out', 'Przekroczono czas wysyłania danych – spróbuj ponownie'],
      [400, 'Invalid request', 'Nieprawidłowe żądanie']
    ])('translates the %i client request error {"message":"%s"} from the backend', (status, message, expected) => {
      expect(getApiErrorMessage(httpError(status, { message }), FALLBACK)).toBe(expected);
    });

    it('does not treat inherited object keys as known messages', () => {
      expect(getApiErrorMessage(httpError(400, { message: 'constructor' }), FALLBACK)).toBe('constructor');
      expect(getApiErrorMessage(httpError(413, { message: 'toString' }), FALLBACK))
        .toBe('Przesyłane pliki są za duże – maksymalnie 5 MB na plik i 25 MB łącznie');
    });

    it('returns the request size message for 413 Payload Too Large without a body (e.g. from nginx)', () => {
      expect(getApiErrorMessage(httpError(413), FALLBACK)).toBe('Przesyłane pliki są za duże – maksymalnie 5 MB na plik i 25 MB łącznie');
      expect(getApiErrorMessage(httpError(413, '<html>413 Request Entity Too Large</html>'), FALLBACK))
        .toBe('Przesyłane pliki są za duże – maksymalnie 5 MB na plik i 25 MB łącznie');
    });

    it.each([null, '', { message: 'Too many requests - try again later' }, '<html>429</html>'])(
      'returns the rate limit message for 429 Too Many Requests with the body %j', body => {
        expect(getApiErrorMessage(httpError(429, body), FALLBACK)).toBe('Zbyt wiele żądań – spróbuj ponownie za kilka minut');
      });

    it('does not leak the unexpected error message of a 500 response', () => {
      expect(getApiErrorMessage(httpError(500, { message: 'An unexpected error occurred' }), FALLBACK)).toBe(FALLBACK);
    });

    it('returns a connection message for status 0', () => {
      expect(getApiErrorMessage(httpError(0), FALLBACK)).toBe('Brak połączenia z serwerem');
    });

    it('returns the fallback for server errors even with a message', () => {
      expect(getApiErrorMessage(httpError(500, { message: 'Internal details' }), FALLBACK)).toBe(FALLBACK);
    });

    it('never returns the raw Angular "Http failure response" text', () => {
      const error = httpError(404, 'Not Found');
      expect(error.message).toContain('Http failure response');
      expect(getApiErrorMessage(error, FALLBACK)).toBe(FALLBACK);
    });

    it.each([null, '', { message: '   ' }, { message: 42 }])('returns the fallback for an unusable body %j', body => {
      expect(getApiErrorMessage(httpError(400, body), FALLBACK)).toBe(FALLBACK);
    });

    it('returns the fallback for non-HTTP errors', () => {
      expect(getApiErrorMessage(new Error('boom'), FALLBACK)).toBe(FALLBACK);
    });
  });
});
