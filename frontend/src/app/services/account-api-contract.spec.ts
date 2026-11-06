import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { AccountService } from './account.service';
import { AuthService } from './auth.service';
import { SavedJobsService } from './saved-jobs.service';
import { getApiErrorMessage } from '../utils/http-errors';
import { getPasswordErrorMessages } from '../utils/password';
import { makeJwtExpiringIn } from '../../testing/jwt-helper';

/**
 * Kontrakt z backendem etapu 4: odpowiedzi w dokładnie takim kształcie, jaki zwraca API (ASP.NET Core 9, JSON camelCase,
 * ValidationProblem z `errors`, daty UTC z „Z”, nagłówek `Pagination` w PascalCase) – sprawdzone na żywym backendzie.
 */
describe('Account API contract (backend response shapes)', () => {
  let http: HttpTestingController;
  const accountService = () => TestBed.inject(AccountService);
  const authService = () => TestBed.inject(AuthService);

  const FALLBACK = 'fallback';

  // Opisy Identity (IdentityErrorDescriber) dokładnie jak w odpowiedzi API
  const WEAK_PASSWORD_PROBLEM = {
    type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
    title: 'One or more validation errors occurred.',
    status: 400,
    errors: {
      NewPassword: [
        'Passwords must have at least one digit (\'0\'-\'9\').',
        'Passwords must have at least one uppercase (\'A\'-\'Z\').'
      ]
    },
    traceId: '00-4d3c1f5f1a0e5d2b8a3f6c7e9b1d2a40-5e6f7a8b9c0d1e2f-00'
  };

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatSnackBar, useValue: { open: vi.fn() } }
      ]
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  function problem(status: number, body: unknown): HttpErrorResponse {
    return new HttpErrorResponse({ status, error: body });
  }

  describe('responses', () => {
    it('maps the user response of change-password (with emailConfirmed) onto the session and keeps the user id claim', () => {
      const oldToken = makeJwtExpiringIn(3600, { nameid: 'u-1' });
      const newToken = makeJwtExpiringIn(7200, { nameid: 'u-1' });
      localStorage.setItem('jobboard_token', oldToken);
      localStorage.setItem('jobboard_user', JSON.stringify({ email: 'jan@example.com', firstName: 'Jan', lastName: 'K', token: oldToken, emailConfirmed: false }));
      const fresh = authService();
      expect(fresh.emailConfirmationPending()).toBe(true);

      accountService().changePassword({ currentPassword: 'a', newPassword: 'b' }).subscribe();
      http.expectOne('/api/account/change-password').flush(
        { email: 'jan@example.com', firstName: 'Jan', lastName: 'K', token: newToken, emailConfirmed: true });

      expect(fresh.getToken()).toBe(newToken);
      expect(fresh.emailConfirmationPending()).toBe(false);
    });

    it('parses the profile response with explicit nulls and UTC dates', () => {
      let profile: unknown;
      accountService().getProfile().subscribe(p => (profile = p));

      http.expectOne('/api/account/profile').flush({
        email: 'jan@example.com', firstName: 'Jan', lastName: 'Kowalski', phoneNumber: null, headline: null, location: null,
        linkedInUrl: null, about: null, emailConfirmed: true, roles: ['User'], createdAt: '2026-11-06T14:21:45.123456Z'
      });

      expect(profile).toMatchObject({ phoneNumber: null, roles: ['User'], emailConfirmed: true });
      expect(Date.parse((profile as { createdAt: string }).createdAt)).toBe(Date.UTC(2026, 10, 6, 14, 21, 45, 123));
    });

    it('lists the profile documents as the API returns them (kind as a number, no storage data)', () => {
      let documents: { id: number; kind: number }[] = [];
      accountService().getDocuments().subscribe(d => (documents = d));

      http.expectOne('/api/account/documents').flush([
        { id: 4, kind: 0, fileName: 'Życiorys.pdf', size: 2000, uploadedAt: '2026-11-06T12:07:00.860369Z' },
        { id: 5, kind: 2, fileName: 'foto.png', size: 16, uploadedAt: '2026-11-06T12:07:00.860369Z' }
      ]);

      expect(documents.map(d => d.kind)).toEqual([0, 2]);
      expect(Object.keys(documents[0]).sort()).toEqual(['fileName', 'id', 'kind', 'size', 'uploadedAt']);
    });

    it('accepts the 201 response of an upload with a full list and the 204 of a deletion without a body', () => {
      let done: unknown;
      accountService().uploadDocuments([{ file: new File(['%PDF-1.4'], 'a.pdf'), kind: 0 }]).subscribe(e => { if (e.type === 'done') done = e.documents; });
      http.expectOne('/api/account/documents').flush(
        [{ id: 1, kind: 0, fileName: 'a.pdf', size: 8, uploadedAt: '2026-11-06T12:07:00Z' }], { status: 201, statusText: 'Created' });
      expect(done).toHaveLength(1);

      let deleted = false;
      accountService().deleteDocument(1).subscribe({ complete: () => (deleted = true) });
      http.expectOne('/api/account/documents/1').flush(null, { status: 204, statusText: 'No Content' });
      expect(deleted).toBe(true);
    });

    it('reads a page of saved jobs with the PascalCase Pagination header', async () => {
      localStorage.setItem('jobboard_token', makeJwtExpiringIn(3600));
      localStorage.setItem('jobboard_user', JSON.stringify({ email: 'jan@example.com', firstName: 'J', lastName: 'K', token: makeJwtExpiringIn(3600), emailConfirmed: true }));
      localStorage.setItem('jobboard_token', JSON.parse(localStorage.getItem('jobboard_user')!).token);
      const saved = TestBed.inject(SavedJobsService);
      TestBed.tick();
      http.expectOne('/api/saved-jobs/ids').flush([]);

      const page = firstValueFrom(saved.getSavedJobs(2, 10));
      const req = http.expectOne(r => r.url === '/api/saved-jobs');
      expect(req.request.params.get('pageNumber')).toBe('2');
      expect(req.request.params.get('pageSize')).toBe('10');
      req.flush([{
        savedAt: '2026-11-06T12:07:00.5Z',
        job: { id: 7, title: 'Dev', company: 'Acme', companyId: 1, description: 'x', salaryMin: 1, salaryMax: 2, location: 'Rzeszów', workType: 0,
          createdAt: '2026-11-01T10:00:00Z', status: 3, expiresAt: '2026-11-02T10:00:00Z', isExpired: true }
      }], { headers: { Pagination: '{"CurrentPage":2,"ItemsPerPage":10,"TotalItems":11,"TotalPages":2}' } });

      const result = await page;
      expect(result.pagination).toEqual({ CurrentPage: 2, ItemsPerPage: 10, TotalItems: 11, TotalPages: 2 });
      expect(result.result[0].job.isExpired).toBe(true);
      // oferta z odpowiedzi jest zapisana po stronie serwera - serduszko jest pełne także dla wygasłej
      expect(saved.isSaved(7)).toBe(true);
    });

    it('treats the 204 answers of PUT/DELETE saved-jobs (no body) as success', async () => {
      const token = makeJwtExpiringIn(3600);
      localStorage.setItem('jobboard_token', token);
      localStorage.setItem('jobboard_user', JSON.stringify({ email: 'jan@example.com', firstName: 'J', lastName: 'K', token, emailConfirmed: true }));
      const saved = TestBed.inject(SavedJobsService);
      TestBed.tick();
      http.expectOne('/api/saved-jobs/ids').flush([3]);

      const removed = firstValueFrom(saved.toggle(3));
      http.expectOne('/api/saved-jobs/3').flush(null, { status: 204, statusText: 'No Content' });

      expect(await removed).toBe(false);
      expect(authService().currentUser()).not.toBeNull();
    });
  });

  describe('error bodies', () => {
    it('recognises a weak password in a ValidationProblem (key NewPassword) in either endpoint', () => {
      const error = problem(400, WEAK_PASSWORD_PROBLEM);

      expect(getPasswordErrorMessages(error)).toEqual(['Hasło musi zawierać cyfrę', 'Hasło musi zawierać wielką literę']);
      // bez rozpoznanych opisów (np. błąd długości z DataAnnotations) zostaje ogólny komunikat
      expect(getApiErrorMessage(problem(400, { ...WEAK_PASSWORD_PROBLEM, errors: { NewPassword: ['The field NewPassword must be a string or array type with a maximum length of \'100\'.'] } }), FALLBACK))
        .toBe('Przesłane dane są nieprawidłowe');
      expect(getPasswordErrorMessages(problem(400, { ...WEAK_PASSWORD_PROBLEM, errors: { NewPassword: ['The field NewPassword must be a string or array type with a maximum length of \'100\'.'] } })))
        .toEqual([]);
    });

    it.each([
      [400, { message: 'Current password is incorrect' }, 'Obecne hasło jest nieprawidłowe'],
      [400, { message: 'Invalid or expired reset link' }, 'Link do zmiany hasła jest nieprawidłowy lub wygasł'],
      [400, { message: 'Invalid or expired confirmation link' }, 'Link potwierdzający jest nieprawidłowy lub wygasł'],
      [400, { message: 'Email is already confirmed' }, 'Adres e-mail jest już potwierdzony'],
      [400, { message: 'No documents selected' }, 'Nie wybrano dokumentów z profilu'],
      [400, { message: 'A profile can have at most 10 documents' }, 'Profil może mieć maksymalnie 10 dokumentów'],
      [400, { message: 'Total size of profile documents cannot exceed 25 MB' }, 'Łączny rozmiar dokumentów profilu nie może przekroczyć 25 MB'],
      [404, { message: 'Document not found' }, 'Nie znaleziono dokumentu'],
      [404, { message: 'Job offer not found' }, 'Oferta nie została znaleziona'],
      [404, { message: 'Application not found' }, 'Aplikacja nie została znaleziona'],
      [413, { message: 'Request body too large' }, 'Przesyłane dane są za duże'],
      [423, { message: 'Account is temporarily locked due to too many failed login attempts' },
        'Konto zostało tymczasowo zablokowane po zbyt wielu nieudanych próbach. Spróbuj ponownie za kilka minut.'],
      [429, { message: 'Too many requests - try again later' }, 'Zbyt wiele żądań – spróbuj ponownie za kilka minut']
    ])('translates %i %j', (status, body, expected) => {
      expect(getApiErrorMessage(problem(status, body), FALLBACK)).toBe(expected);
    });

    it('does not leak anything for a 415 or a JSON binding error (ProblemDetails without "message")', () => {
      expect(getApiErrorMessage(problem(415, { type: 'https://tools.ietf.org/html/rfc9110#section-15.5.16', title: 'Unsupported Media Type', status: 415 }), FALLBACK))
        .toBe(FALLBACK);
      expect(getApiErrorMessage(problem(400, {
        title: 'One or more validation errors occurred.', status: 400,
        errors: { '$.documentIds[0]': ['The JSON value could not be converted to System.Int32. Path: $.documentIds[0] | LineNumber: 0 | BytePositionInLine: 18.'] }
      }), FALLBACK)).toBe('Przesłane dane są nieprawidłowe');
    });

    it('handles a 429 with an empty body and a 413 HTML page from nginx', () => {
      expect(getApiErrorMessage(problem(429, null), FALLBACK)).toBe('Zbyt wiele żądań – spróbuj ponownie za kilka minut');
      expect(getApiErrorMessage(problem(413, '<html><head><title>413 Request Entity Too Large</title></head></html>'), FALLBACK))
        .toBe('Przesyłane pliki są za duże – maksymalnie 5 MB na plik i 25 MB łącznie');
    });

    it('never shows the message of a 500', () => {
      expect(getApiErrorMessage(problem(500, { message: 'An unexpected error occurred' }), FALLBACK)).toBe(FALLBACK);
    });
  });

  describe('request shapes', () => {
    it('sends the DELETE /api/account password as a JSON object body', () => {
      accountService().deleteAccount('Haslo123').subscribe();

      const req = http.expectOne('/api/account');
      expect(req.request.method).toBe('DELETE');
      expect(req.request.body).toEqual({ password: 'Haslo123' });
      expect(req.request.serializeBody()).toBe('{"password":"Haslo123"}');
      expect(req.request.detectContentTypeHeader()).toBe('application/json');
      req.flush(null, { status: 204, statusText: 'No Content' });
    });

    it('sends POST resend-confirmation without a body', () => {
      authService().resendConfirmation().subscribe();
      const resend = http.expectOne('/api/auth/resend-confirmation');
      expect(resend.request.body).toBeNull();
      resend.flush(null, { status: 204, statusText: 'No Content' });
    });

    it('sends the profile with explicit nulls for empty optional fields', () => {
      accountService().updateProfile({
        firstName: 'Jan', lastName: 'Kowalski', phoneNumber: null, headline: null, location: null, linkedInUrl: null, about: null
      }).subscribe();

      const req = http.expectOne('/api/account/profile');
      expect(req.request.serializeBody()).toBe(
        '{"firstName":"Jan","lastName":"Kowalski","phoneNumber":null,"headline":null,"location":null,"linkedInUrl":null,"about":null}');
      req.flush({ email: 'jan@example.com', firstName: 'Jan', lastName: 'Kowalski', phoneNumber: null, headline: null, location: null,
        linkedInUrl: null, about: null, emailConfirmed: true, roles: ['User'], createdAt: '2026-11-06T14:21:45Z' });
    });

    it('sends all three notification flags', () => {
      accountService().updateNotificationPreferences({ applicationStatusChanged: false, newApplication: true, companyReview: true }).subscribe();

      const req = http.expectOne('/api/account/notifications');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ applicationStatusChanged: false, newApplication: true, companyReview: true });
      req.flush({ applicationStatusChanged: false, newApplication: true, companyReview: true });
    });
  });
});
