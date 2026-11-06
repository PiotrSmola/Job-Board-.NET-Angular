import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { JobApplicationService } from './job-application.service';
import { ApplicationStatus, CvUploadEvent, JobApplication } from '../models/job-application.model';
import { ApplicationParams } from '../models/application-params.model';
import { PaginatedResult } from '../models/pagination.model';
import { getApiErrorMessage } from '../utils/http-errors';

const APPLICATION = {
  id: 1,
  jobOfferId: 2,
  userId: 'u1',
  userEmail: 'jan@example.com',
  userFullName: 'Jan Kowalski',
  jobTitle: 'Programista',
  company: 'Acme',
  companyId: 1,
  note: null,
  appliedAt: '2026-01-01T10:00:00Z'
};

describe('JobApplicationService', () => {
  let service: JobApplicationService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(JobApplicationService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  describe('getAllApplications', () => {
    it('sends only paging and sorting params for default ApplicationParams', () => {
      service.getAllApplications(new ApplicationParams()).subscribe();

      const req = http.expectOne(r => r.url === '/api/jobapplications/all');
      expect(req.request.method).toBe('GET');
      expect(req.request.params.keys().sort()).toEqual(['orderBy', 'pageNumber', 'pageSize']);
      expect(req.request.params.get('pageNumber')).toBe('1');
      expect(req.request.params.get('pageSize')).toBe('10');
      expect(req.request.params.get('orderBy')).toBe('newest');
      req.flush([]);
    });

    it('sends searchTerm, jobOfferId and orderBy when provided', () => {
      const params = new ApplicationParams();
      params.pageNumber = 4;
      params.pageSize = 50;
      params.searchTerm = 'kowalski';
      params.jobOfferId = 12;
      params.orderBy = 'oldest';

      service.getAllApplications(params).subscribe();

      const req = http.expectOne(r => r.url === '/api/jobapplications/all');
      expect(req.request.params.get('pageNumber')).toBe('4');
      expect(req.request.params.get('pageSize')).toBe('50');
      expect(req.request.params.get('searchTerm')).toBe('kowalski');
      expect(req.request.params.get('jobOfferId')).toBe('12');
      expect(req.request.params.get('orderBy')).toBe('oldest');
      req.flush([]);
    });

    it('omits an empty searchTerm and a null jobOfferId', () => {
      const params = new ApplicationParams();
      params.searchTerm = '';
      params.jobOfferId = null;

      service.getAllApplications(params).subscribe();

      const req = http.expectOne(r => r.url === '/api/jobapplications/all');
      expect(req.request.params.has('searchTerm')).toBe(false);
      expect(req.request.params.has('jobOfferId')).toBe(false);
      req.flush([]);
    });

    it('maps the response body and Pagination header to a PaginatedResult', () => {
      let result: PaginatedResult<JobApplication> | undefined;
      service.getAllApplications(new ApplicationParams()).subscribe(r => (result = r));

      http.expectOne(r => r.url === '/api/jobapplications/all').flush([APPLICATION], {
        headers: { Pagination: JSON.stringify({ CurrentPage: 1, ItemsPerPage: 10, TotalItems: 1, TotalPages: 1 }) }
      });

      expect(result?.result.length).toBe(1);
      expect(result?.result[0].id).toBe(1);
      expect(result?.pagination).toEqual({ CurrentPage: 1, ItemsPerPage: 10, TotalItems: 1, TotalPages: 1 });
    });

    it('falls back to defaults when the Pagination header is missing', () => {
      const params = new ApplicationParams();
      params.pageSize = 25;
      let result: PaginatedResult<JobApplication> | undefined;
      service.getAllApplications(params).subscribe(r => (result = r));

      http.expectOne(r => r.url === '/api/jobapplications/all').flush([]);

      expect(result?.result).toEqual([]);
      expect(result?.pagination).toEqual({ CurrentPage: 1, ItemsPerPage: 25, TotalItems: 0, TotalPages: 1 });
    });
  });

  describe('other endpoints', () => {
    it('getMyApplications requests /my-applications', () => {
      service.getMyApplications().subscribe();
      const req = http.expectOne('/api/jobapplications/my-applications');
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });

    it('applyToJob posts the application', () => {
      const body = { jobOfferId: 3, note: 'Cześć' };
      service.applyToJob(body).subscribe();
      const req = http.expectOne('/api/jobapplications');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      req.flush(APPLICATION);
    });

    it('checkIfApplied requests /check/:id', () => {
      let result: { hasApplied: boolean } | undefined;
      service.checkIfApplied(5).subscribe(r => (result = r));
      const req = http.expectOne('/api/jobapplications/check/5');
      expect(req.request.method).toBe('GET');
      req.flush({ hasApplied: true });
      expect(result).toEqual({ hasApplied: true });
    });

    it('deleteApplication sends DELETE', () => {
      service.deleteApplication(8).subscribe();
      const req = http.expectOne('/api/jobapplications/8');
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
    });

    it('getApplication requests /:id', () => {
      service.getApplication(6).subscribe();
      const req = http.expectOne('/api/jobapplications/6');
      expect(req.request.method).toBe('GET');
      req.flush(APPLICATION);
    });

    it('updateStatus puts status and comment to /:id/status', () => {
      const body = { status: ApplicationStatus.Interview, comment: 'Zapraszamy' };
      service.updateStatus(6, body).subscribe();
      const req = http.expectOne('/api/jobapplications/6/status');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(body);
      req.flush(APPLICATION);
    });

    it('withdraw posts to /:id/withdraw', () => {
      service.withdraw(6).subscribe();
      const req = http.expectOne('/api/jobapplications/6/withdraw');
      expect(req.request.method).toBe('POST');
      req.flush(APPLICATION);
    });
  });

  describe('CV', () => {
    it('uploadCv puts the file as multipart form data and emits progress and the updated application', () => {
      const file = new File(['%PDF-1.7'], 'moje cv.pdf', { type: 'application/pdf' });
      const events: CvUploadEvent[] = [];
      let completed = false;
      service.uploadCv(6, file).subscribe({ next: e => events.push(e), complete: () => (completed = true) });

      const req = http.expectOne('/api/jobapplications/6/cv');
      expect(req.request.method).toBe('PUT');
      expect(req.request.reportProgress).toBe(true);
      const body = req.request.body as FormData;
      expect(body).toBeInstanceOf(FormData);
      expect((body.get('file') as File).name).toBe('moje cv.pdf');

      req.event({ type: HttpEventType.Sent });
      req.event({ type: HttpEventType.UploadProgress, loaded: 25, total: 100 });
      req.event({ type: HttpEventType.UploadProgress, loaded: 100, total: 100 });
      const updated = { ...APPLICATION, id: 6, hasCv: true, cvFileName: 'moje cv.pdf', cvSize: 8, cvUploadedAt: '2026-01-02T10:00:00Z' };
      req.flush(updated);

      expect(events).toEqual([
        { type: 'progress', percent: 25 },
        { type: 'progress', percent: 100 },
        { type: 'done', application: updated }
      ]);
      expect(completed).toBe(true);
    });

    it('uploadCv reports 0 % when the total size is unknown', () => {
      const events: CvUploadEvent[] = [];
      service.uploadCv(6, new File(['x'], 'cv.pdf')).subscribe(e => events.push(e));

      const req = http.expectOne('/api/jobapplications/6/cv');
      req.event({ type: HttpEventType.UploadProgress, loaded: 10 });
      req.flush(APPLICATION);

      expect(events[0]).toEqual({ type: 'progress', percent: 0 });
      expect(events[events.length - 1].type).toBe('done');
    });

    it('uploadCv passes API errors through', () => {
      let error: HttpErrorResponse | undefined;
      service.uploadCv(6, new File(['x'], 'cv.pdf')).subscribe({ error: e => (error = e) });

      http.expectOne('/api/jobapplications/6/cv')
        .flush({ message: 'File is too large - maximum size is 5 MB' }, { status: 400, statusText: 'Bad Request' });

      expect(error?.status).toBe(400);
      expect(error?.error).toEqual({ message: 'File is too large - maximum size is 5 MB' });
    });

    it('uploadCv cancels the HTTP request when the subscriber unsubscribes (page or dialog closed)', () => {
      const subscription = service.uploadCv(6, new File(['x'], 'cv.pdf')).subscribe();
      const req = http.expectOne('/api/jobapplications/6/cv');
      expect(req.cancelled).toBe(false);

      subscription.unsubscribe();

      expect(req.cancelled).toBe(true);
    });

    it.each([
      [413, 'Payload Too Large', null],
      [409, 'Conflict', { message: 'CV was changed by another request' }],
      [400, 'Bad Request', { message: 'File is empty' }]
    ])('uploadCv passes the %i error through without emitting a done event', (status, statusText, body) => {
      const events: CvUploadEvent[] = [];
      let error: HttpErrorResponse | undefined;
      service.uploadCv(6, new File(['x'], 'cv.pdf')).subscribe({ next: e => events.push(e), error: e => (error = e) });

      http.expectOne('/api/jobapplications/6/cv').flush(body, { status, statusText });

      expect(error?.status).toBe(status);
      expect(error?.error).toEqual(body);
      expect(events).toEqual([]);
    });

    it('uploadCv sends a file name with markup only as a multipart value, never into the page', () => {
      service.uploadCv(6, new File(['x'], '<script>alert(1)</script>../../x.pdf')).subscribe();

      const req = http.expectOne('/api/jobapplications/6/cv');
      expect(req.request.url).toBe('/api/jobapplications/6/cv');
      expect(req.request.body).toBeInstanceOf(FormData);
      expect(document.querySelector('script')).toBeNull();
      req.flush(APPLICATION);
    });

    it('deleteCv sends DELETE to /:id/cv', () => {
      service.deleteCv(6).subscribe();
      const req = http.expectOne('/api/jobapplications/6/cv');
      expect(req.request.method).toBe('DELETE');
      req.flush(null, { status: 204, statusText: 'No Content' });
    });

    describe('downloadCv', () => {
      let createObjectURL: ReturnType<typeof vi.fn>;
      let downloads: { href: string; download: string }[];

      beforeEach(() => {
        vi.useFakeTimers();
        createObjectURL = vi.fn().mockReturnValue('blob:cv');
        Object.assign(URL, { createObjectURL, revokeObjectURL: vi.fn() });
        downloads = [];
        vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
          downloads.push({ href: this.href, download: this.download });
        });
      });

      afterEach(() => {
        vi.useRealTimers();
        vi.restoreAllMocks();
      });

      it('requests the file as a blob and saves it under the name from Content-Disposition', () => {
        let done = false;
        service.downloadCv(6, 'fallback.pdf').subscribe(() => (done = true));

        const req = http.expectOne('/api/jobapplications/6/cv');
        expect(req.request.method).toBe('GET');
        expect(req.request.responseType).toBe('blob');
        const blob = new Blob(['%PDF-'], { type: 'application/pdf' });
        req.flush(blob, { headers: { 'Content-Disposition': "attachment; filename*=UTF-8''%C5%BC%C3%B3%C5%82w.pdf" } });

        expect(done).toBe(true);
        expect(createObjectURL).toHaveBeenCalledWith(blob);
        expect(downloads).toEqual([{ href: 'blob:cv', download: 'żółw.pdf' }]);
      });

      it('falls back to the given file name when the header is not exposed', () => {
        service.downloadCv(6, 'moje-cv.docx').subscribe();

        http.expectOne('/api/jobapplications/6/cv').flush(new Blob(['PK']));

        expect(downloads[0].download).toBe('moje-cv.docx');
      });

      it('uses "cv" when neither the header nor the fallback name is available', () => {
        service.downloadCv(6).subscribe();

        http.expectOne('/api/jobapplications/6/cv').flush(new Blob(['PK']));

        expect(downloads[0].download).toBe('cv');
      });

      // FileReader (parsowanie treści błędu) działa asynchronicznie – te testy działają na prawdziwych timerach
      async function waitFor(condition: () => boolean): Promise<void> {
        for (let i = 0; i < 200 && !condition(); i++) {
          await new Promise(resolve => setTimeout(resolve, 5));
        }
      }

      it('does not save anything when the request fails', async () => {
        vi.useRealTimers();
        let error: HttpErrorResponse | undefined;
        service.downloadCv(6, 'cv.pdf').subscribe({ error: e => (error = e) });

        http.expectOne('/api/jobapplications/6/cv').flush(new Blob([]), { status: 404, statusText: 'Not Found' });

        await waitFor(() => error !== undefined);
        expect(error?.status).toBe(404);
        expect(createObjectURL).not.toHaveBeenCalled();
        expect(downloads).toEqual([]);
      });

      it('parses the JSON error body sent as a Blob so the API message can be shown', async () => {
        vi.useRealTimers();
        let error: HttpErrorResponse | undefined;
        service.downloadCv(6, 'cv.pdf').subscribe({ error: e => (error = e) });

        http.expectOne('/api/jobapplications/6/cv').flush(
          new Blob([JSON.stringify({ message: 'Application not found' })], { type: 'application/json' }),
          { status: 404, statusText: 'Not Found' }
        );

        await waitFor(() => error !== undefined);
        expect(error).toBeInstanceOf(HttpErrorResponse);
        expect(error?.status).toBe(404);
        expect(error?.error).toEqual({ message: 'Application not found' });
      });

      it('sanitizes a malicious file name from the header before saving', () => {
        service.downloadCv(6).subscribe();

        http.expectOne('/api/jobapplications/6/cv').flush(new Blob(['x']), {
          headers: { 'Content-Disposition': "attachment; filename*=UTF-8''..%2F..%2Fstartup%5Cevil%0A.pdf" }
        });

        expect(downloads[0].download).toBe('evil.pdf');
        expect(downloads[0].href).toBe('blob:cv');
      });

      it('cancels the HTTP request and saves nothing when the subscriber unsubscribes', () => {
        const subscription = service.downloadCv(6, 'cv.pdf').subscribe();
        const req = http.expectOne('/api/jobapplications/6/cv');

        subscription.unsubscribe();

        expect(req.cancelled).toBe(true);
        expect(createObjectURL).not.toHaveBeenCalled();
      });

      it('revokes every object URL it creates and leaves no pending timers', () => {
        const revokeObjectURL = vi.fn();
        let counter = 0;
        createObjectURL.mockImplementation(() => `blob:cv-${++counter}`);
        Object.assign(URL, { revokeObjectURL });

        service.downloadCv(6, 'a.pdf').subscribe();
        http.expectOne('/api/jobapplications/6/cv').flush(new Blob(['a']));
        service.downloadCv(7, 'b.pdf').subscribe();
        http.expectOne('/api/jobapplications/7/cv').flush(new Blob(['b']));

        vi.runAllTimers();

        expect(createObjectURL).toHaveBeenCalledTimes(2);
        expect(revokeObjectURL.mock.calls.map(call => call[0])).toEqual(['blob:cv-1', 'blob:cv-2']);
        expect(vi.getTimerCount()).toBe(0);
      });

      it('revokes the object URL and reports the error when the browser refuses to start the download', () => {
        const revokeObjectURL = vi.fn();
        Object.assign(URL, { revokeObjectURL });
        vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {
          throw new Error('download blocked');
        });
        let error: unknown;
        service.downloadCv(6, 'cv.pdf').subscribe({ error: e => (error = e) });

        http.expectOne('/api/jobapplications/6/cv').flush(new Blob(['x']));
        vi.runAllTimers();

        expect(error).toBeInstanceOf(Error);
        expect(revokeObjectURL).toHaveBeenCalledWith('blob:cv');
        expect(document.querySelector('a[download]')).toBeNull();
      });

      it.each([
        [413, 'Payload Too Large', null, 'Plik jest za duży – maksymalny rozmiar to 5 MB'],
        [409, 'Conflict', { message: 'CV was changed by another request' }, 'CV zostało w międzyczasie zmienione – odśwież dane'],
        [404, 'Not Found', { message: 'CV not found' }, 'Nie znaleziono CV tej aplikacji']
      ])('turns a %i error with a Blob body into a translatable API error', async (status, statusText, body, expected) => {
        vi.useRealTimers();
        let error: unknown;
        service.downloadCv(6, 'cv.pdf').subscribe({ error: e => (error = e) });

        http.expectOne('/api/jobapplications/6/cv').flush(
          new Blob(body ? [JSON.stringify(body)] : [], { type: 'application/json' }),
          { status, statusText }
        );

        await waitFor(() => error !== undefined);
        expect(getApiErrorMessage(error, 'Nie udało się pobrać CV')).toBe(expected);
        expect(createObjectURL).not.toHaveBeenCalled();
      });

      it('keeps the original error when the error Blob is not valid JSON', async () => {
        vi.useRealTimers();
        let error: HttpErrorResponse | undefined;
        service.downloadCv(6, 'cv.pdf').subscribe({ error: e => (error = e) });

        http.expectOne('/api/jobapplications/6/cv').flush(
          new Blob(['{"message": "unterminated'], { type: 'application/json' }),
          { status: 400, statusText: 'Bad Request' }
        );

        await waitFor(() => error !== undefined);
        expect(error?.status).toBe(400);
        expect(error?.error).toBeInstanceOf(Blob);
        expect(getApiErrorMessage(error, 'Nie udało się pobrać CV')).toBe('Nie udało się pobrać CV');
      });

      it('does not leak raw server details from a non-JSON error Blob', async () => {
        vi.useRealTimers();
        let error: HttpErrorResponse | undefined;
        service.downloadCv(6, 'cv.pdf').subscribe({ error: e => (error = e) });

        http.expectOne('/api/jobapplications/6/cv').flush(
          new Blob(['System.NullReferenceException at Backend.Controllers'], { type: 'text/plain' }),
          { status: 400, statusText: 'Bad Request' }
        );

        await waitFor(() => error !== undefined);
        expect(getApiErrorMessage(error, 'Nie udało się pobrać CV')).toBe('Nie udało się pobrać CV');
      });

      it('saves a markup-laden file name from the header as plain, harmless text', () => {
        service.downloadCv(6).subscribe();

        http.expectOne('/api/jobapplications/6/cv').flush(new Blob(['x']), {
          headers: { 'Content-Disposition': "attachment; filename*=UTF-8''%3Cscript%3Ealert(1)%3C%2Fscript%3E%0D%0Aevil%00.pdf" }
        });

        const name = downloads[0].download;
        expect(name).not.toMatch(/[<>/\\]/);
        expect(name).not.toMatch(/[\u0000-\u001f]/);
        expect(name.endsWith('.pdf')).toBe(true);
        expect(document.querySelector('script')).toBeNull();
      });
    });
  });

  describe('getAllApplications status filter', () => {
    it.each([
      [ApplicationStatus.Submitted, '0'],
      [ApplicationStatus.Rejected, '5']
    ])('sends status %s as a number', (status, expected) => {
      const params = new ApplicationParams();
      params.status = status;

      service.getAllApplications(params).subscribe();

      const req = http.expectOne(r => r.url === '/api/jobapplications/all');
      expect(req.request.params.get('status')).toBe(expected);
      req.flush([]);
    });

    it('omits a null status', () => {
      service.getAllApplications(new ApplicationParams()).subscribe();

      const req = http.expectOne(r => r.url === '/api/jobapplications/all');
      expect(req.request.params.has('status')).toBe(false);
      req.flush([]);
    });
  });
});
