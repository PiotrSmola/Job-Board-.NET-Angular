import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { JobApplicationService } from './job-application.service';
import { AttachmentKind, AttachmentUploadEvent, JobApplication } from '../models/job-application.model';
import { getApiErrorMessage } from '../utils/http-errors';
import { getFileNameFromContentDisposition } from '../utils/content-disposition';
import { getAttachmentKindLabel } from '../utils/attachments';

/**
 * Kontrakt z backendem: żądania i odpowiedzi w dokładnie takim kształcie, jaki zwraca API (sprawdzone na żywym backendzie,
 * Development, magazyn Local) – JSON z polami camelCase, enum jako liczba, błędy `{ "message": "..." }`, nginx bez JSON-a.
 */
describe('Attachments API contract (backend response shapes)', () => {
  let service: JobApplicationService;
  let http: HttpTestingController;

  const UPLOAD_URL = '/api/jobapplications/12/attachments';
  const FALLBACK = 'Nie udało się przesłać załączników';

  // Odpowiedź 201 skopiowana z żywego backendu (4 pliki w jednym żądaniu; posortowana wg rodzaju)
  const BACKEND_CREATED = `{"id":12,"jobOfferId":11,"userId":"fdab93be-9330-48f3-b54c-ec930fe754f2","note":"kontrakt",
    "appliedAt":"2026-11-06T12:04:49.2Z","status":0,"statusChangedAt":"2026-11-06T12:04:49.2Z",
    "userEmail":"jan.kowalski@example.com","userFullName":"Jan Kowalski","jobTitle":"Cybersecurity Specialist","company":"SecureIT",
    "companyId":11,"jobOffer":null,
    "attachments":[
      {"id":66,"kind":0,"fileName":"Życiorys Łukasz %22CV%22.pdf","size":2000,"uploadedAt":"2026-11-06T12:07:00.860369Z"},
      {"id":67,"kind":1,"fileName":"list motywacyjny.docx","size":271,"uploadedAt":"2026-11-06T12:07:00.860369Z"},
      {"id":65,"kind":2,"fileName":"Portfolio zdjęcie ąę.png","size":2000,"uploadedAt":"2026-11-06T12:07:00.860369Z"},
      {"id":68,"kind":3,"fileName":"inne.jpg","size":2000,"uploadedAt":"2026-11-06T12:07:00.860369Z"}],
    "attachmentsTotalSize":6271,
    "history":[{"status":0,"changedAt":"2026-11-06T12:04:49.2Z","comment":null,"changedBy":"Jan Kowalski"}]}`;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(JobApplicationService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function file(name: string): File {
    return new File(['x'], name);
  }

  describe('multipart request', () => {
    it('appends a "kinds" value right after each "files" part, in the order the user picked the files', () => {
      service.uploadAttachments(12, [
        { file: file('zdjecie.png'), kind: AttachmentKind.Portfolio },
        { file: file('zyciorys.pdf'), kind: AttachmentKind.Cv },
        { file: file('list.docx'), kind: AttachmentKind.CoverLetter }
      ]).subscribe({ error: () => undefined });

      const req = http.expectOne(UPLOAD_URL);
      const parts = Array.from((req.request.body as FormData).entries());

      expect(parts.map(([key]) => key)).toEqual(['files', 'kinds', 'files', 'kinds', 'files', 'kinds']);
      // wartości kinds to liczby enuma zapisane jako tekst - tylko takie backend parsuje (" 1", "1.0", "Cv" daje 400)
      expect(parts.filter(([key]) => key === 'kinds').map(([, value]) => value)).toEqual(['2', '0', '1']);
      expect(parts.filter(([key]) => key === 'files').map(([, value]) => (value as File).name))
        .toEqual(['zdjecie.png', 'zyciorys.pdf', 'list.docx']);
      req.flush(null, { status: 400, statusText: 'Bad Request' });
    });

    it('sends 10 files as 20 parts (the UI limit of 10 files needs the backend form limit to allow 20 parts)', () => {
      service.uploadAttachments(12, Array.from({ length: 10 }, (_, i) => ({ file: file(`p${i}.pdf`), kind: (i % 4) as AttachmentKind })))
        .subscribe({ error: () => undefined });

      const req = http.expectOne(UPLOAD_URL);
      const parts = Array.from((req.request.body as FormData).entries());

      expect(parts).toHaveLength(20);
      expect(parts.filter(([key]) => key === 'files')).toHaveLength(10);
      expect(parts.filter(([key]) => key === 'kinds')).toHaveLength(10);
      req.flush(null, { status: 400, statusText: 'Bad Request' });
    });

    it('does not set Content-Type itself, so the browser adds the multipart boundary', () => {
      service.uploadAttachments(12, [{ file: file('a.pdf'), kind: AttachmentKind.Cv }]).subscribe({ error: () => undefined });

      const req = http.expectOne(UPLOAD_URL);

      expect(req.request.headers.has('Content-Type')).toBe(false);
      req.flush(null, { status: 400, statusText: 'Bad Request' });
    });
  });

  describe('201 response', () => {
    it('parses the backend JSON (enum as number, camelCase fields) into the application the lists render', () => {
      const events: AttachmentUploadEvent[] = [];
      service.uploadAttachments(12, [{ file: file('a.pdf'), kind: AttachmentKind.Cv }]).subscribe(e => events.push(e));

      http.expectOne(UPLOAD_URL).flush(JSON.parse(BACKEND_CREATED), { status: 201, statusText: 'Created' });

      const done = events.find(e => e.type === 'done') as { type: 'done'; application: JobApplication };
      const attachments = done.application.attachments;
      expect(attachments.map(a => a.kind)).toEqual([
        AttachmentKind.Cv, AttachmentKind.CoverLetter, AttachmentKind.Portfolio, AttachmentKind.Other
      ]);
      expect(attachments.map(a => getAttachmentKindLabel(a.kind))).toEqual(['CV', 'List motywacyjny', 'Portfolio', 'Inny']);
      expect(done.application.attachmentsTotalSize).toBe(attachments.reduce((sum, a) => sum + a.size, 0));
      expect(Object.keys(attachments[0]).sort()).toEqual(['fileName', 'id', 'kind', 'size', 'uploadedAt']);
      expect(attachments[0].fileName).toBe('Życiorys Łukasz %22CV%22.pdf');
      // data przychodzi jako tekst ISO z końcówką Z (UTC)
      expect(new Date(attachments[0].uploadedAt).toISOString()).toBe('2026-11-06T12:07:00.860Z');
    });

    it('shows an unknown kind from a newer API as "Inny" instead of an empty label', () => {
      expect(getAttachmentKindLabel(7 as AttachmentKind)).toBe('Inny');
    });
  });

  describe('error responses of POST /attachments', () => {
    function uploadError(status: number, statusText: string, body: string | object | null, headers?: Record<string, string>): HttpErrorResponse {
      let error: HttpErrorResponse | undefined;
      service.uploadAttachments(12, [{ file: file('a.pdf'), kind: AttachmentKind.Cv }]).subscribe({ error: e => (error = e) });
      http.expectOne(UPLOAD_URL).flush(body, { status, statusText, headers });
      return error as HttpErrorResponse;
    }

    it.each([
      ['Unsupported file type - upload a PDF, DOCX, PNG or JPEG file', 'Nieobsługiwany typ pliku – prześlij plik PDF, DOCX, PNG lub JPEG'],
      ['File is too large - maximum size is 5 MB', 'Plik jest za duży – maksymalny rozmiar to 5 MB'],
      ['File is empty', 'Plik jest pusty'],
      ['Total size of attachments cannot exceed 25 MB', 'Łączny rozmiar załączników nie może przekroczyć 25 MB'],
      ['An application can have at most 10 attachments', 'Aplikacja może mieć maksymalnie 10 załączników'],
      ["No file uploaded - send files in the 'files' form field", 'Nie wybrano plików do przesłania'],
      // 12 plików z polami kinds (ponad limit części formularza) i ucięty multipart kończą się tym komunikatem
      ['Invalid multipart form data', 'Nie udało się odczytać przesłanych plików'],
      ['Attachments cannot be added in the current application status', 'Na tym etapie rekrutacji nie można już dodawać załączników']
    ])('translates the 400 response {"message":"%s"}', (message, expected) => {
      const error = uploadError(400, 'Bad Request', { message });

      expect(error.status).toBe(400);
      expect(getApiErrorMessage(error, FALLBACK)).toBe(expected);
    });

    it('translates 404 {"message":"Application not found"} (somebody else\'s or missing application)', () => {
      const error = uploadError(404, 'Not Found', { message: 'Application not found' });

      expect(getApiErrorMessage(error, FALLBACK)).toBe('Aplikacja nie została znaleziona');
    });

    it('translates the 429 from the "uploads" policy with a Retry-After header', () => {
      const error = uploadError(429, 'Too Many Requests', { message: 'Too many requests - try again later' }, { 'Retry-After': '540' });

      expect(getApiErrorMessage(error, FALLBACK)).toBe('Zbyt wiele żądań – spróbuj ponownie za kilka minut');
    });

    it('translates the 413 from nginx (HTML page, no JSON) over the generic message', () => {
      const html = '<html>\r\n<head><title>413 Request Entity Too Large</title></head>\r\n<body><center><h1>413 Request Entity Too Large</h1></center>' +
        '<hr><center>nginx</center></body>\r\n</html>';
      const error = uploadError(413, 'Request Entity Too Large', html, { 'Content-Type': 'text/html' });

      expect(getApiErrorMessage(error, FALLBACK)).toBe('Przesyłane pliki są za duże – maksymalnie 5 MB na plik i 25 MB łącznie');
    });

    it.each([
      [500, 'Internal Server Error', { message: 'An unexpected error occurred' }],
      [502, 'Bad Gateway', '<html><head><title>502 Bad Gateway</title></head><body><center><h1>502 Bad Gateway</h1></center><hr><center>nginx</center></body></html>'],
      [504, 'Gateway Time-out', '<html><head><title>504 Gateway Time-out</title></head><body>nginx</body></html>']
    ])('shows the generic message and no server text for a %i response', (status, statusText, body) => {
      const error = uploadError(status, statusText, body);

      expect(getApiErrorMessage(error, FALLBACK)).toBe(FALLBACK);
    });

    it('shows the connection message when the server drops the connection while the body is still being sent', () => {
      let error: HttpErrorResponse | undefined;
      service.uploadAttachments(12, [{ file: file('a.pdf'), kind: AttachmentKind.Cv }]).subscribe({ error: e => (error = e) });

      http.expectOne(UPLOAD_URL).error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });

      expect(getApiErrorMessage(error, FALLBACK)).toBe('Brak połączenia z serwerem');
    });

    // backend: AttachmentFiles.InvalidKindMessage – klient wysyła tylko 0-3, więc to w praktyce niezgodność wersji
    it('translates 400 {"message":"Invalid attachment kind"} to Polish', () => {
      const error = uploadError(400, 'Bad Request', { message: 'Invalid attachment kind' });

      expect(getApiErrorMessage(error, FALLBACK)).toBe('Nieprawidłowy rodzaj załącznika');
    });

    // backend: GlobalExceptionHandler.RequestTooLargeMessage (BadHttpRequestException 413 z limitu treści żądania)
    it('translates the 413 JSON from the backend by its message, not as the nginx file limit', () => {
      const error = uploadError(413, 'Payload Too Large', { message: 'Request body too large' }, { 'Content-Type': 'application/json' });

      expect(getApiErrorMessage(error, FALLBACK)).toBe('Przesyłane dane są za duże');
    });
  });

  describe('Content-Disposition of GET /attachments/{id}', () => {
    // nagłówki skopiowane z żywego backendu (tylko filename*, UTF-8, kodowanie procentowe)
    it.each([
      ["attachment; filename*=UTF-8''%C5%BByciorys%20%C5%81ukasz%20%2522CV%2522.pdf", 'Życiorys Łukasz %22CV%22.pdf'],
      ["attachment; filename*=UTF-8''list%20motywacyjny.docx", 'list motywacyjny.docx'],
      ["attachment; filename*=UTF-8''Portfolio%20zdj%C4%99cie%20%C4%85%C4%99.png", 'Portfolio zdjęcie ąę.png'],
      ["attachment; filename*=UTF-8''inne.jpg", 'inne.jpg'],
      // nazwa po sanityzacji w backendzie: dozwolone nawiasy i średnik są kodowane (RFC 5987), a Windows-owe znaki zamienione na '_'
      ["attachment; filename*=UTF-8''%C5%BByciorys_%20Jan%20%282026%29.pdf", 'Życiorys_ Jan (2026).pdf'],
      ["attachment; filename*=UTF-8''Za%C5%BC%C3%B3%C5%82%C4%87%20g%C4%99%C5%9Bl%C4%85%20ja%C5%BA%C5%84%3B%20%281%29.pdf", 'Zażółć gęślą jaźń; (1).pdf']
    ])('reads the file name from "%s"', (header, expected) => {
      expect(getFileNameFromContentDisposition(header)).toBe(expected);
    });

    it('saves the file under the name from the exposed header, not the (possibly stale) DTO name', () => {
      vi.useFakeTimers();
      Object.assign(URL, { createObjectURL: vi.fn().mockReturnValue('blob:x'), revokeObjectURL: vi.fn() });
      let download = '';
      vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
        download = this.download;
      });
      service.downloadAttachment(12, { id: 66, fileName: 'stara-nazwa.pdf' }).subscribe();

      http.expectOne('/api/jobapplications/12/attachments/66').flush(new Blob(['%PDF-'], { type: 'application/pdf' }), {
        headers: {
          'Content-Disposition': "attachment; filename*=UTF-8''%C5%BByciorys%20%C5%81ukasz.pdf",
          'Content-Type': 'application/pdf',
          'X-Content-Type-Options': 'nosniff',
          'Cache-Control': 'no-store'
        }
      });

      expect(download).toBe('Życiorys Łukasz.pdf');
      vi.runAllTimers();
      vi.useRealTimers();
      vi.restoreAllMocks();
    });
  });
});
