import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { JobApplicationService } from './job-application.service';
import { ApplicationStatus, AttachmentKind, AttachmentUploadEvent, AttachmentUploadItem, JobApplication } from '../models/job-application.model';
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
  appliedAt: '2026-01-01T10:00:00Z',
  attachments: [],
  attachmentsTotalSize: 0
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

  describe('attachments', () => {
    const URL_6 = '/api/jobapplications/6/attachments';
    const ATTACHMENT = { id: 3, kind: AttachmentKind.Cv, fileName: 'moje cv.pdf', size: 8, uploadedAt: '2026-01-02T10:00:00Z' };

    function item(name: string, kind = AttachmentKind.Other): AttachmentUploadItem {
      return { file: new File(['x'], name), kind };
    }

    it('uploadAttachments posts all files and their kinds in one multipart request and emits progress and the application', () => {
      const cv = new File(['%PDF-1.7'], 'moje cv.pdf', { type: 'application/pdf' });
      const letter = new File(['PK'], 'list.docx');
      const events: AttachmentUploadEvent[] = [];
      let completed = false;
      service.uploadAttachments(6, [{ file: cv, kind: AttachmentKind.Cv }, { file: letter, kind: AttachmentKind.CoverLetter }])
        .subscribe({ next: e => events.push(e), complete: () => (completed = true) });

      const req = http.expectOne(URL_6);
      expect(req.request.method).toBe('POST');
      expect(req.request.reportProgress).toBe(true);
      const body = req.request.body as FormData;
      expect(body).toBeInstanceOf(FormData);
      expect((body.getAll('files') as File[]).map(f => f.name)).toEqual(['moje cv.pdf', 'list.docx']);
      expect(body.getAll('kinds')).toEqual(['0', '1']);
      expect(body.has('file')).toBe(false);

      req.event({ type: HttpEventType.Sent });
      req.event({ type: HttpEventType.UploadProgress, loaded: 25, total: 100 });
      req.event({ type: HttpEventType.UploadProgress, loaded: 100, total: 100 });
      const updated = { ...APPLICATION, id: 6, attachments: [ATTACHMENT], attachmentsTotalSize: 8 };
      req.flush(updated, { status: 201, statusText: 'Created' });

      expect(events).toEqual([
        { type: 'progress', percent: 25 },
        { type: 'progress', percent: 100 },
        { type: 'done', application: updated }
      ]);
      expect(completed).toBe(true);
    });

    it('uploadAttachments keeps the kinds aligned with the files for every kind', () => {
      service.uploadAttachments(6, [
        item('a.pdf', AttachmentKind.Other), item('b.png', AttachmentKind.Portfolio), item('c.pdf', AttachmentKind.Cv)
      ]).subscribe();

      const req = http.expectOne(URL_6);
      const body = req.request.body as FormData;
      expect((body.getAll('files') as File[]).map(f => f.name)).toEqual(['a.pdf', 'b.png', 'c.pdf']);
      expect(body.getAll('kinds')).toEqual(['3', '2', '0']);
      req.flush(APPLICATION);
    });

    it('uploadAttachments reports 0 % when the total size is unknown', () => {
      const events: AttachmentUploadEvent[] = [];
      service.uploadAttachments(6, [item('cv.pdf')]).subscribe(e => events.push(e));

      const req = http.expectOne(URL_6);
      req.event({ type: HttpEventType.UploadProgress, loaded: 10 });
      req.flush(APPLICATION);

      expect(events[0]).toEqual({ type: 'progress', percent: 0 });
      expect(events[events.length - 1].type).toBe('done');
    });

    it('uploadAttachments never reports more than 100 %', () => {
      const events: AttachmentUploadEvent[] = [];
      service.uploadAttachments(6, [item('cv.pdf')]).subscribe(e => events.push(e));

      const req = http.expectOne(URL_6);
      req.event({ type: HttpEventType.UploadProgress, loaded: 120, total: 100 });
      req.flush(APPLICATION);

      expect(events[0]).toEqual({ type: 'progress', percent: 100 });
    });

    it('uploadAttachments cancels the HTTP request when the subscriber unsubscribes (page or dialog closed)', () => {
      const subscription = service.uploadAttachments(6, [item('cv.pdf')]).subscribe();
      const req = http.expectOne(URL_6);
      expect(req.cancelled).toBe(false);

      subscription.unsubscribe();

      expect(req.cancelled).toBe(true);
    });

    it.each([
      [413, 'Payload Too Large', null],
      [429, 'Too Many Requests', { message: 'Too many requests - try again later' }],
      [400, 'Bad Request', { message: 'Total size of attachments cannot exceed 25 MB' }],
      [400, 'Bad Request', { message: 'An application can have at most 10 attachments' }],
      [400, 'Bad Request', { message: 'File is empty' }]
    ])('uploadAttachments passes the %i error through without emitting a done event', (status, statusText, body) => {
      const events: AttachmentUploadEvent[] = [];
      let error: HttpErrorResponse | undefined;
      service.uploadAttachments(6, [item('cv.pdf')]).subscribe({ next: e => events.push(e), error: e => (error = e) });

      http.expectOne(URL_6).flush(body, { status, statusText });

      expect(error?.status).toBe(status);
      expect(error?.error).toEqual(body);
      expect(events).toEqual([]);
    });

    it('uploadAttachments sends a file name with markup only as a multipart value, never into the page', () => {
      service.uploadAttachments(6, [item('<script>alert(1)</script>../../x.pdf')]).subscribe();

      const req = http.expectOne(URL_6);
      expect(req.request.url).toBe(URL_6);
      expect(req.request.body).toBeInstanceOf(FormData);
      expect(document.querySelector('script')).toBeNull();
      req.flush(APPLICATION);
    });

    it('deleteAttachment sends DELETE to /:id/attachments/:attachmentId', () => {
      let done = false;
      service.deleteAttachment(6, 3).subscribe({ complete: () => (done = true) });
      const req = http.expectOne('/api/jobapplications/6/attachments/3');
      expect(req.request.method).toBe('DELETE');
      req.flush(null, { status: 204, statusText: 'No Content' });
      expect(done).toBe(true);
    });

    it('no longer offers the removed single-CV API', () => {
      const methods = service as unknown as Record<string, unknown>;
      expect(methods['uploadCv']).toBeUndefined();
      expect(methods['deleteCv']).toBeUndefined();
      expect(methods['downloadCv']).toBeUndefined();
    });

    describe('downloadAttachment', () => {
      const DOWNLOAD_URL = '/api/jobapplications/6/attachments/3';
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
        service.downloadAttachment(6, { id: 3, fileName: 'fallback.pdf' }).subscribe(() => (done = true));

        const req = http.expectOne(DOWNLOAD_URL);
        expect(req.request.method).toBe('GET');
        expect(req.request.responseType).toBe('blob');
        const blob = new Blob(['%PDF-'], { type: 'application/pdf' });
        req.flush(blob, { headers: { 'Content-Disposition': "attachment; filename*=UTF-8''%C5%BC%C3%B3%C5%82w.pdf" } });

        expect(done).toBe(true);
        expect(createObjectURL).toHaveBeenCalledWith(blob);
        expect(downloads).toEqual([{ href: 'blob:cv', download: 'żółw.pdf' }]);
      });

      it('falls back to the file name from the DTO when the header is not exposed', () => {
        service.downloadAttachment(6, ATTACHMENT).subscribe();

        http.expectOne(DOWNLOAD_URL).flush(new Blob(['PK']));

        expect(downloads[0].download).toBe('moje cv.pdf');
      });

      it('uses "zalacznik" when neither the header nor a usable file name is available', () => {
        service.downloadAttachment(6, { id: 3, fileName: '' }).subscribe();

        http.expectOne(DOWNLOAD_URL).flush(new Blob(['PK']));

        expect(downloads[0].download).toBe('zalacznik');
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
        service.downloadAttachment(6, ATTACHMENT).subscribe({ error: e => (error = e) });

        http.expectOne(DOWNLOAD_URL).flush(new Blob([]), { status: 404, statusText: 'Not Found' });

        await waitFor(() => error !== undefined);
        expect(error?.status).toBe(404);
        expect(createObjectURL).not.toHaveBeenCalled();
        expect(downloads).toEqual([]);
      });

      it('parses the JSON error body sent as a Blob so the API message can be shown', async () => {
        vi.useRealTimers();
        let error: HttpErrorResponse | undefined;
        service.downloadAttachment(6, ATTACHMENT).subscribe({ error: e => (error = e) });

        http.expectOne(DOWNLOAD_URL).flush(
          new Blob([JSON.stringify({ message: 'Attachment not found' })], { type: 'application/json' }),
          { status: 404, statusText: 'Not Found' }
        );

        await waitFor(() => error !== undefined);
        expect(error).toBeInstanceOf(HttpErrorResponse);
        expect(error?.status).toBe(404);
        expect(error?.error).toEqual({ message: 'Attachment not found' });
      });

      it('sanitizes a malicious file name from the header before saving', () => {
        service.downloadAttachment(6, { id: 3, fileName: 'x.pdf' }).subscribe();

        http.expectOne(DOWNLOAD_URL).flush(new Blob(['x']), {
          headers: { 'Content-Disposition': "attachment; filename*=UTF-8''..%2F..%2Fstartup%5Cevil%0A.pdf" }
        });

        expect(downloads[0].download).toBe('evil.pdf');
        expect(downloads[0].href).toBe('blob:cv');
      });

      it('cancels the HTTP request and saves nothing when the subscriber unsubscribes', () => {
        const subscription = service.downloadAttachment(6, ATTACHMENT).subscribe();
        const req = http.expectOne(DOWNLOAD_URL);

        subscription.unsubscribe();

        expect(req.cancelled).toBe(true);
        expect(createObjectURL).not.toHaveBeenCalled();
      });

      it('revokes every object URL it creates after about 30 s and leaves no pending timers', () => {
        const revokeObjectURL = vi.fn();
        let counter = 0;
        createObjectURL.mockImplementation(() => `blob:cv-${++counter}`);
        Object.assign(URL, { revokeObjectURL });

        service.downloadAttachment(6, { id: 3, fileName: 'a.pdf' }).subscribe();
        http.expectOne(DOWNLOAD_URL).flush(new Blob(['a']));
        service.downloadAttachment(7, { id: 4, fileName: 'b.pdf' }).subscribe();
        http.expectOne('/api/jobapplications/7/attachments/4').flush(new Blob(['b']));

        vi.advanceTimersByTime(1000);
        expect(revokeObjectURL).not.toHaveBeenCalled();
        vi.advanceTimersByTime(29_000);

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
        service.downloadAttachment(6, ATTACHMENT).subscribe({ error: e => (error = e) });

        http.expectOne(DOWNLOAD_URL).flush(new Blob(['x']));
        vi.runAllTimers();

        expect(error).toBeInstanceOf(Error);
        expect(revokeObjectURL).toHaveBeenCalledWith('blob:cv');
        expect(document.querySelector('a[download]')).toBeNull();
      });

      it.each([
        [404, 'Not Found', { message: 'Attachment not found' }, 'Nie znaleziono załącznika'],
        [404, 'Not Found', { message: 'Application not found' }, 'Aplikacja nie została znaleziona'],
        [429, 'Too Many Requests', { message: 'Too many requests - try again later' }, 'Zbyt wiele żądań – spróbuj ponownie za kilka minut']
      ])('turns a %i error with a Blob body into a translatable API error', async (status, statusText, body, expected) => {
        vi.useRealTimers();
        let error: unknown;
        service.downloadAttachment(6, ATTACHMENT).subscribe({ error: e => (error = e) });

        http.expectOne(DOWNLOAD_URL).flush(
          new Blob([JSON.stringify(body)], { type: 'application/json' }),
          { status, statusText }
        );

        await waitFor(() => error !== undefined);
        expect(getApiErrorMessage(error, 'Nie udało się pobrać załącznika')).toBe(expected);
        expect(createObjectURL).not.toHaveBeenCalled();
      });

      it('keeps the original error when the error Blob is not valid JSON', async () => {
        vi.useRealTimers();
        let error: HttpErrorResponse | undefined;
        service.downloadAttachment(6, ATTACHMENT).subscribe({ error: e => (error = e) });

        http.expectOne(DOWNLOAD_URL).flush(
          new Blob(['{"message": "unterminated'], { type: 'application/json' }),
          { status: 400, statusText: 'Bad Request' }
        );

        await waitFor(() => error !== undefined);
        expect(error?.status).toBe(400);
        expect(error?.error).toBeInstanceOf(Blob);
        expect(getApiErrorMessage(error, 'Nie udało się pobrać załącznika')).toBe('Nie udało się pobrać załącznika');
      });

      it('does not leak raw server details from a non-JSON error Blob', async () => {
        vi.useRealTimers();
        let error: HttpErrorResponse | undefined;
        service.downloadAttachment(6, ATTACHMENT).subscribe({ error: e => (error = e) });

        http.expectOne(DOWNLOAD_URL).flush(
          new Blob(['System.NullReferenceException at Backend.Controllers'], { type: 'text/plain' }),
          { status: 400, statusText: 'Bad Request' }
        );

        await waitFor(() => error !== undefined);
        expect(getApiErrorMessage(error, 'Nie udało się pobrać załącznika')).toBe('Nie udało się pobrać załącznika');
      });

      it('saves a markup-laden file name from the header as plain, harmless text', () => {
        service.downloadAttachment(6, { id: 3, fileName: 'x.pdf' }).subscribe();

        http.expectOne(DOWNLOAD_URL).flush(new Blob(['x']), {
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
