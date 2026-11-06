import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AccountService } from './account.service';
import { AuthService } from './auth.service';
import { NotificationPreferences, Profile, ProfileDocument, ProfileDocumentUploadEvent } from '../models/account.model';
import { AttachmentKind } from '../models/job-application.model';
import { UserDto } from '../models/user.model';
import { getApiErrorMessage } from '../utils/http-errors';

const PROFILE: Profile = {
  email: 'jan@example.com',
  firstName: 'Jan',
  lastName: 'Kowalski',
  phoneNumber: null,
  headline: null,
  location: 'Rzeszów',
  linkedInUrl: null,
  about: null,
  emailConfirmed: true,
  roles: ['User'],
  createdAt: '2026-01-01T10:00:00Z'
};

const DOCUMENT: ProfileDocument = { id: 3, kind: AttachmentKind.Cv, fileName: 'cv.pdf', size: 1000, uploadedAt: '2026-01-02T10:00:00Z' };

describe('AccountService', () => {
  let service: AccountService;
  let http: HttpTestingController;
  let renewSession: ReturnType<typeof vi.fn>;
  let updateCurrentUser: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    renewSession = vi.fn();
    updateCurrentUser = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { renewSession, updateCurrentUser } }
      ]
    });
    service = TestBed.inject(AccountService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  describe('profile', () => {
    it('getProfile requests GET /api/account/profile', () => {
      let result: Profile | undefined;
      service.getProfile().subscribe(p => (result = p));

      const req = http.expectOne('/api/account/profile');
      expect(req.request.method).toBe('GET');
      req.flush(PROFILE);
      expect(result).toEqual(PROFILE);
    });

    it('updateProfile sends PUT with the body and syncs the session name and e-mail status', () => {
      const body = { firstName: 'Janek', lastName: 'Nowak', phoneNumber: null, headline: null, location: null, linkedInUrl: null, about: null };
      service.updateProfile(body).subscribe();

      const req = http.expectOne('/api/account/profile');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(body);
      req.flush({ ...PROFILE, firstName: 'Janek', lastName: 'Nowak', emailConfirmed: false });

      expect(updateCurrentUser).toHaveBeenCalledWith({ firstName: 'Janek', lastName: 'Nowak', emailConfirmed: false });
    });

    it('does not touch the session when saving fails', () => {
      service.updateProfile({ ...PROFILE }).subscribe({ error: () => undefined });
      http.expectOne('/api/account/profile').flush({ errors: { FirstName: ['x'] } }, { status: 400, statusText: 'Bad Request' });

      expect(updateCurrentUser).not.toHaveBeenCalled();
    });
  });

  describe('changePassword', () => {
    it('posts both passwords and stores the new token from the response', () => {
      const user: UserDto = { email: 'jan@example.com', firstName: 'Jan', lastName: 'K', token: 'new-token' };
      let result: UserDto | undefined;
      service.changePassword({ currentPassword: 'Old123', newPassword: 'New12345' }).subscribe(u => (result = u));

      const req = http.expectOne('/api/account/change-password');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ currentPassword: 'Old123', newPassword: 'New12345' });
      req.flush(user);

      expect(renewSession).toHaveBeenCalledWith(user);
      expect(result).toEqual(user);
    });

    it('keeps the session and translates a wrong current password', () => {
      let error: unknown;
      service.changePassword({ currentPassword: 'bad', newPassword: 'New12345' }).subscribe({ error: e => (error = e) });
      http.expectOne('/api/account/change-password')
        .flush({ message: 'Current password is incorrect' }, { status: 400, statusText: 'Bad Request' });

      expect(renewSession).not.toHaveBeenCalled();
      expect(getApiErrorMessage(error, 'x')).toBe('Obecne hasło jest nieprawidłowe');
    });
  });

  describe('deleteAccount', () => {
    it('sends DELETE /api/account with the password in the body', () => {
      let done = false;
      service.deleteAccount('Secret1').subscribe(() => (done = true));

      const req = http.expectOne('/api/account');
      expect(req.request.method).toBe('DELETE');
      expect(req.request.body).toEqual({ password: 'Secret1' });
      req.flush(null, { status: 204, statusText: 'No Content' });
      expect(done).toBe(true);
    });

    it.each([
      ['Administrator accounts cannot be deleted here', 'Konta administratora nie można usunąć w tym miejscu'],
      ['You are the last member of a company with job offers', 'Jesteś ostatnim członkiem firmy, która ma oferty pracy – nie możesz teraz usunąć konta'],
      ['Current password is incorrect', 'Obecne hasło jest nieprawidłowe']
    ])('translates the 400 "%s"', (message, expected) => {
      let error: unknown;
      service.deleteAccount('x').subscribe({ error: e => (error = e) });
      http.expectOne('/api/account').flush({ message }, { status: 400, statusText: 'Bad Request' });

      expect(getApiErrorMessage(error, 'fallback')).toBe(expected);
    });
  });

  describe('documents', () => {
    it('getDocuments requests the profile documents', () => {
      let result: ProfileDocument[] | undefined;
      service.getDocuments().subscribe(d => (result = d));
      const req = http.expectOne('/api/account/documents');
      expect(req.request.method).toBe('GET');
      req.flush([DOCUMENT]);
      expect(result).toEqual([DOCUMENT]);
    });

    it('uploadDocuments sends files and parallel kinds as multipart and reports progress, then the list', () => {
      const cv = new File(['%PDF-'], 'cv.pdf');
      const letter = new File(['PK'], 'list.docx');
      const events: ProfileDocumentUploadEvent[] = [];
      service.uploadDocuments([{ file: cv, kind: AttachmentKind.Cv }, { file: letter, kind: AttachmentKind.CoverLetter }])
        .subscribe(e => events.push(e));

      const req = http.expectOne('/api/account/documents');
      expect(req.request.method).toBe('POST');
      expect(req.request.reportProgress).toBe(true);
      const body = req.request.body as FormData;
      expect(body.getAll('files').map(f => (f as File).name)).toEqual(['cv.pdf', 'list.docx']);
      expect(body.getAll('kinds')).toEqual(['0', '1']);

      req.event({ type: HttpEventType.UploadProgress, loaded: 50, total: 200 });
      req.flush([DOCUMENT], { status: 201, statusText: 'Created' });

      expect(events).toEqual([{ type: 'progress', percent: 25 }, { type: 'done', documents: [DOCUMENT] }]);
    });

    it('cancels the upload when the subscriber unsubscribes', () => {
      const subscription = service.uploadDocuments([{ file: new File(['x'], 'a.pdf'), kind: AttachmentKind.Cv }]).subscribe();
      const req = http.expectOne('/api/account/documents');

      subscription.unsubscribe();

      expect(req.cancelled).toBe(true);
    });

    it('translates the profile limits', () => {
      let error: unknown;
      service.uploadDocuments([{ file: new File(['x'], 'a.pdf'), kind: AttachmentKind.Cv }]).subscribe({ error: e => (error = e) });
      http.expectOne('/api/account/documents')
        .flush({ message: 'A profile can have at most 10 documents' }, { status: 400, statusText: 'Bad Request' });

      expect(getApiErrorMessage(error, 'x')).toBe('Profil może mieć maksymalnie 10 dokumentów');
    });

    it('deleteDocument sends DELETE /api/account/documents/{id}', () => {
      service.deleteDocument(3).subscribe();
      const req = http.expectOne('/api/account/documents/3');
      expect(req.request.method).toBe('DELETE');
      req.flush(null, { status: 204, statusText: 'No Content' });
    });

    describe('downloadDocument', () => {
      let originalCreate: typeof URL.createObjectURL;
      let originalRevoke: typeof URL.revokeObjectURL;
      let createObjectURL: ReturnType<typeof vi.fn>;

      beforeEach(() => {
        originalCreate = URL.createObjectURL;
        originalRevoke = URL.revokeObjectURL;
        createObjectURL = vi.fn().mockReturnValue('blob:doc');
        Object.assign(URL, { createObjectURL, revokeObjectURL: vi.fn() });
        vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
      });

      afterEach(() => {
        Object.assign(URL, { createObjectURL: originalCreate, revokeObjectURL: originalRevoke });
        vi.restoreAllMocks();
      });

      it('downloads the file as a blob and saves it', () => {
        const blob = new Blob(['%PDF-']);
        service.downloadDocument(DOCUMENT).subscribe();

        const req = http.expectOne('/api/account/documents/3');
        expect(req.request.method).toBe('GET');
        expect(req.request.responseType).toBe('blob');
        req.flush(blob, { headers: { 'Content-Disposition': 'attachment; filename="moje-cv.pdf"' } });

        expect(createObjectURL).toHaveBeenCalledWith(blob);
      });

      it('parses the JSON error body of a blob response', async () => {
        const errorPromise = new Promise<unknown>(resolve => service.downloadDocument(DOCUMENT).subscribe({ error: resolve }));
        http.expectOne('/api/account/documents/3').flush(
          new Blob([JSON.stringify({ message: 'Document not found' })], { type: 'application/json' }),
          { status: 404, statusText: 'Not Found' });

        const error = await errorPromise;
        expect(error).toBeInstanceOf(HttpErrorResponse);
        expect(getApiErrorMessage(error, 'x')).toBe('Nie znaleziono dokumentu');
      });
    });
  });

  describe('notification preferences', () => {
    const PREFS: NotificationPreferences = { applicationStatusChanged: false, newApplication: true, companyReview: true };

    it('reads and saves the preferences', () => {
      let read: NotificationPreferences | undefined;
      service.getNotificationPreferences().subscribe(p => (read = p));
      const get = http.expectOne('/api/account/notifications');
      expect(get.request.method).toBe('GET');
      get.flush(PREFS);
      expect(read).toEqual(PREFS);

      service.updateNotificationPreferences(PREFS).subscribe();
      const put = http.expectOne('/api/account/notifications');
      expect(put.request.method).toBe('PUT');
      expect(put.request.body).toEqual(PREFS);
      put.flush(PREFS);
    });
  });
});
