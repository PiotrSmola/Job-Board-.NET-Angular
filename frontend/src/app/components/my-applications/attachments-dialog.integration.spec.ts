import { TestBed } from '@angular/core/testing';
import { HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { OverlayContainer } from '@angular/cdk/overlay';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import {
  ATTACHMENTS_DIALOG_CONFIG,
  ATTACHMENTS_UPLOAD_ABORTED,
  AttachmentsDialogComponent,
  AttachmentsDialogResult
} from './attachments-dialog.component';
import { ApplicationStatus, AttachmentKind, JobApplication } from '../../models/job-application.model';

const APPLICATION: JobApplication = {
  id: 7, jobOfferId: 3, userId: 'u1', userEmail: 'jan@example.com', userFullName: 'Jan Kowalski',
  jobTitle: 'Programista Angular', company: 'Acme', companyId: 1, note: null, appliedAt: new Date('2026-01-01T10:00:00Z'),
  status: ApplicationStatus.Submitted, statusChangedAt: '2026-01-01T10:00:00Z',
  attachments: [], attachmentsTotalSize: 0
};

const UPDATED: JobApplication = {
  ...APPLICATION,
  attachments: [{ id: 1, kind: AttachmentKind.Cv, fileName: 'nowe-cv.pdf', size: 8, uploadedAt: '2026-01-02T10:00:00Z' }],
  attachmentsTotalSize: 8
};

const URL = '/api/jobapplications/7/attachments';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

/**
 * Dialog załączników otwarty przez prawdziwy MatDialog i prawdziwy serwis (z HttpTestingController):
 * anulowanie żądań, Escape, zarządzanie fokusem, klawiatura i komunikaty błędów.
 */
describe('AttachmentsDialogComponent in a real MatDialog', () => {
  let dialog: MatDialog;
  let http: HttpTestingController;
  let overlay: OverlayContainer;
  let ref: MatDialogRef<AttachmentsDialogComponent, AttachmentsDialogResult>;
  let closed: AttachmentsDialogResult[];

  const PDF = new File(['%PDF-1.7'], 'nowe-cv.pdf', { type: 'application/pdf' });
  const DOCX = new File(['PK'], 'list.docx');

  function open(data: JobApplication = APPLICATION): void {
    ref = dialog.open<AttachmentsDialogComponent, JobApplication, AttachmentsDialogResult>(AttachmentsDialogComponent, {
      ...ATTACHMENTS_DIALOG_CONFIG, data, enterAnimationDuration: '0ms', exitAnimationDuration: '0ms'
    });
    closed = [];
    ref.afterClosed().subscribe(value => closed.push(value));
    TestBed.tick();
  }

  /** Zamknięcie dialogu jest asynchroniczne (jsdom nie wysyła zdarzeń animacji) – czekamy na usunięcie kontenera. */
  async function settled(): Promise<void> {
    await vi.waitFor(() => {
      TestBed.tick();
      expect(query('mat-dialog-container')).toBeNull();
    });
  }

  function query<T extends HTMLElement>(selector: string): T | null {
    return overlay.getContainerElement().querySelector<T>(selector);
  }

  function queryAll<T extends HTMLElement>(selector: string): T[] {
    return Array.from(overlay.getContainerElement().querySelectorAll<T>(selector));
  }

  function chooseFiles(...files: File[]): void {
    const input = query<HTMLInputElement>('input[type="file"]')!;
    Object.defineProperty(input, 'files', { value: files, configurable: true });
    input.dispatchEvent(new Event('change'));
    TestBed.tick();
  }

  function clickUpload(): void {
    query<HTMLButtonElement>('.upload-button')!.click();
    TestBed.tick();
  }

  function pressEscape(): void {
    const container = query('mat-dialog-container')!;
    container.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', keyCode: 27, bubbles: true, cancelable: true }));
    TestBed.tick();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AttachmentsDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
    dialog = TestBed.inject(MatDialog);
    http = TestBed.inject(HttpTestingController);
    overlay = TestBed.inject(OverlayContainer);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  afterEach(() => {
    dialog.closeAll();
    TestBed.tick();
    http.verify();
    vi.restoreAllMocks();
  });

  it('is an ARIA modal dialog named by its title', async () => {
    open();

    const container = query('mat-dialog-container')!;
    const title = query('h2')!;
    expect(container.getAttribute('role')).toBe('dialog');
    expect(container.getAttribute('aria-modal')).toBe('true');
    await vi.waitFor(() => {
      TestBed.tick();
      expect(container.getAttribute('aria-labelledby')).toBe(title.id);
    });
    expect(title.textContent).toContain('Dodaj załączniki');
  });

  it('puts the keyboard focus on the first interactive element (the file input) after opening', async () => {
    // jsdom nie ma układu strony, a CDK pomija elementy bez wymiarów – symulujemy wymiary
    vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(100);
    vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(20);
    open();

    await vi.waitFor(() => expect(document.activeElement).toBe(query('input[type="file"]')));
  });

  it('starts the tab order with the file input, before the footer buttons', () => {
    open();

    const focusable = queryAll<HTMLElement>('input, select, button:not([disabled])');
    expect(focusable[0]).toBe(query('input[type="file"]'));
    expect(focusable[0].tabIndex).toBe(0);
  });

  it('closes on Escape when nothing is being uploaded', async () => {
    open();

    pressEscape();
    await settled();

    expect(closed).toEqual([undefined]);
  });

  it('closes with the cancel button without sending anything', async () => {
    open();
    chooseFiles(PDF);

    query<HTMLButtonElement>('.cancel-button')!.click();
    await settled();

    expect(closed).toEqual([undefined]);
    http.expectNone(URL);
  });

  it('uploads through the real service: one multipart POST with files and kinds, progress, close with the application', async () => {
    open();
    chooseFiles(PDF, DOCX);
    const kind = queryAll<HTMLSelectElement>('select.kind-select')[1];
    kind.value = String(AttachmentKind.CoverLetter);
    kind.dispatchEvent(new Event('change'));
    TestBed.tick();
    clickUpload();

    const req = http.expectOne(URL);
    expect(req.request.method).toBe('POST');
    const body = req.request.body as FormData;
    expect((body.getAll('files') as File[]).map(f => f.name)).toEqual(['nowe-cv.pdf', 'list.docx']);
    expect(body.getAll('kinds')).toEqual(['0', '1']);
    expect(query('[role="status"]')?.textContent).toBe('Wysyłanie załączników…');

    req.event({ type: HttpEventType.UploadProgress, loaded: 40, total: 100 });
    TestBed.tick();
    expect(query('mat-progress-bar')?.getAttribute('aria-valuenow')).toBe('40');
    expect(query('mat-progress-bar')?.getAttribute('aria-valuetext')).toBe('Wysyłanie… 40%');

    req.event({ type: HttpEventType.UploadProgress, loaded: 70, total: 100 });
    TestBed.tick();
    expect(query('mat-progress-bar')?.getAttribute('aria-valuenow')).toBe('70');

    req.flush(UPDATED, { status: 201, statusText: 'Created' });
    await settled();

    expect(closed).toEqual([UPDATED]);
  });

  it('Escape during the upload cancels the HTTP request and closes the dialog as aborted', async () => {
    open();
    chooseFiles(PDF);
    clickUpload();
    const req = http.expectOne(URL);

    pressEscape();
    await settled();

    expect(req.cancelled).toBe(true);
    expect(closed).toEqual([ATTACHMENTS_UPLOAD_ABORTED]);
  });

  it('Escape in the processing phase (100 % sent) also cancels and reports the abort', async () => {
    open();
    chooseFiles(PDF);
    clickUpload();
    const req = http.expectOne(URL);
    req.event({ type: HttpEventType.UploadProgress, loaded: 100, total: 100 });
    TestBed.tick();
    expect(query('[role="status"]')?.textContent).toBe('Przetwarzanie załączników…');

    pressEscape();
    await settled();

    expect(req.cancelled).toBe(true);
    expect(closed).toEqual([ATTACHMENTS_UPLOAD_ABORTED]);
  });

  it('keeps the dialog open after a click on the backdrop during the upload', () => {
    open();
    chooseFiles(PDF);
    clickUpload();
    const req = http.expectOne(URL);

    (overlay.getContainerElement().querySelector('.cdk-overlay-backdrop') as HTMLElement | null)?.click();
    TestBed.tick();

    expect(closed).toEqual([]);
    expect(req.cancelled).toBe(false);
    req.flush(UPDATED);
  });

  it('cancels the HTTP request when "Przerwij wysyłanie" closes the dialog', async () => {
    open();
    chooseFiles(PDF);
    clickUpload();
    const req = http.expectOne(URL);
    expect(query('.cancel-button')?.textContent).toContain('Przerwij wysyłanie');

    query<HTMLButtonElement>('.cancel-button')!.click();
    await settled();

    expect(closed).toEqual([ATTACHMENTS_UPLOAD_ABORTED]);
    expect(req.cancelled).toBe(true);
  });

  it('cancels the HTTP request when the dialog is closed programmatically during the upload', async () => {
    open();
    chooseFiles(PDF);
    clickUpload();
    const req = http.expectOne(URL);

    ref.close();
    await settled();

    expect(req.cancelled).toBe(true);
  });

  it('cancels the HTTP request when all dialogs are closed (e.g. on navigation)', async () => {
    open();
    chooseFiles(PDF);
    clickUpload();
    const req = http.expectOne(URL);

    dialog.closeAll();
    await settled();

    expect(req.cancelled).toBe(true);
  });

  it('allows closing with Escape again after a failed upload (closed without a result)', async () => {
    open();
    chooseFiles(PDF);
    clickUpload();
    http.expectOne(URL).flush({ message: 'File is empty' }, { status: 400, statusText: 'Bad Request' });
    TestBed.tick();

    pressEscape();
    await settled();

    expect(closed).toEqual([undefined]);
  });

  it.each([
    [413, 'Payload Too Large', null, 'Przesyłane pliki są za duże – maksymalnie 5 MB na plik i 25 MB łącznie'],
    [429, 'Too Many Requests', { message: 'Too many requests - try again later' }, 'Zbyt wiele żądań – spróbuj ponownie za kilka minut'],
    [400, 'Bad Request', { message: 'Total size of attachments cannot exceed 25 MB' }, 'Łączny rozmiar załączników nie może przekroczyć 25 MB'],
    [400, 'Bad Request', { message: 'An application can have at most 10 attachments' }, 'Aplikacja może mieć maksymalnie 10 załączników'],
    [400, 'Bad Request', { message: 'Unsupported file type - upload a PDF, DOCX, PNG or JPEG file' },
      'Nieobsługiwany typ pliku – prześlij plik PDF, DOCX, PNG lub JPEG'],
    [400, 'Bad Request', { message: 'Attachments cannot be added in the current application status' },
      'Na tym etapie rekrutacji nie można już dodawać załączników'],
    [404, 'Not Found', { message: 'Application not found' }, 'Aplikacja nie została znaleziona']
  ])('shows an announced, translated message for a %i response and lets the user retry', async (status, statusText, body, expected) => {
    open();
    chooseFiles(PDF);
    clickUpload();

    http.expectOne(URL).flush(body, { status, statusText });
    TestBed.tick();

    expect(query('[role="alert"]')!.textContent).toContain(expected);
    expect(query('[role="status"]')!.textContent).toBe('Przerwano wysyłanie załączników');
    expect(closed).toEqual([]);
    expect(query<HTMLButtonElement>('.upload-button')!.disabled).toBe(false);
    expect(ref.componentInstance.uploading()).toBe(false);

    clickUpload();
    http.expectOne(URL).flush(UPDATED);
    await settled();
    expect(closed).toEqual([UPDATED]);
  });

  it('shows the generic message and no raw server details for a 500 response', () => {
    open();
    chooseFiles(PDF);
    clickUpload();

    http.expectOne(URL).flush(
      { message: 'System.IO.IOException: disk full' }, { status: 500, statusText: 'Server Error' }
    );
    TestBed.tick();

    const text = query('[role="alert"]')!.textContent ?? '';
    expect(text).toContain('Nie udało się przesłać załączników');
    expect(text).not.toContain('IOException');
  });

  it('does not react to the cancelled request after the dialog was closed', async () => {
    open();
    chooseFiles(PDF);
    clickUpload();
    const req = http.expectOne(URL);
    const component = ref.componentInstance;

    ref.close();
    await settled();

    expect(req.cancelled).toBe(true);
    expect(component.progress()).toBe(0);
    expect(closed).toEqual([undefined]);
  });

  it('labels the kind select of every chosen file with the file name', () => {
    open();
    chooseFiles(PDF, DOCX);

    const labels = queryAll<HTMLSelectElement>('select.kind-select').map(select =>
      (select.getAttribute('aria-labelledby') ?? '').split(' ').map(id => query('#' + id)?.textContent?.trim()).join(' '));
    expect(labels).toEqual(['Rodzaj nowe-cv.pdf', 'Rodzaj list.docx']);
  });

  it('keeps the focus inside the dialog after removing a chosen file', () => {
    open();
    chooseFiles(PDF, DOCX);

    query<HTMLButtonElement>('.remove-button')!.click();
    TestBed.tick();

    expect(document.activeElement?.getAttribute('aria-label')).toBe('Usuń z listy: list.docx');
    expect(overlay.getContainerElement().contains(document.activeElement)).toBe(true);
  });

  it('shows markup in the chosen file name as text', () => {
    open();
    chooseFiles(new File(['%PDF-'], '<script>window.__pwned=true</script>.pdf'));

    expect(query('.file-name')?.textContent).toBe('<script>window.__pwned=true</script>.pdf');
    expect(overlay.getContainerElement().querySelector('script')).toBeNull();
  });

  it('leaves no pending timers after the dialog is closed during an upload', () => {
    vi.useFakeTimers();
    try {
      open();
      chooseFiles(PDF);
      clickUpload();
      const req = http.expectOne(URL);

      ref.close();
      TestBed.tick();
      vi.runAllTimers();

      expect(req.cancelled).toBe(true);
      expect(vi.getTimerCount()).toBe(0);
    } finally {
      vi.useRealTimers();
    }
  });
});
