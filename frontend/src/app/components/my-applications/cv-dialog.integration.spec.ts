import { TestBed } from '@angular/core/testing';
import { HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { OverlayContainer } from '@angular/cdk/overlay';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { CvDialogComponent } from './cv-dialog.component';
import { ApplicationStatus, JobApplication } from '../../models/job-application.model';

const APPLICATION: JobApplication = {
  id: 7, jobOfferId: 3, userId: 'u1', userEmail: 'jan@example.com', userFullName: 'Jan Kowalski',
  jobTitle: 'Programista Angular', company: 'Acme', companyId: 1, note: null, appliedAt: new Date('2026-01-01T10:00:00Z'),
  status: ApplicationStatus.Submitted, statusChangedAt: '2026-01-01T10:00:00Z',
  hasCv: false, cvFileName: null, cvSize: null, cvUploadedAt: null
};

const WITH_CV: JobApplication = {
  ...APPLICATION, hasCv: true, cvFileName: 'stare-cv.pdf', cvSize: 2048, cvUploadedAt: '2026-01-02T10:00:00Z'
};

const CV_URL = '/api/jobapplications/7/cv';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

/**
 * Dialog CV otwarty przez prawdziwy MatDialog i prawdziwy serwis (z HttpTestingController):
 * anulowanie żądań, zarządzanie fokusem, klawiatura i komunikaty błędów.
 */
describe('CvDialogComponent in a real MatDialog', () => {
  let dialog: MatDialog;
  let http: HttpTestingController;
  let overlay: OverlayContainer;
  let ref: MatDialogRef<CvDialogComponent, JobApplication>;
  let closed: (JobApplication | undefined)[];

  const PDF = new File(['%PDF-1.7'], 'nowe-cv.pdf', { type: 'application/pdf' });

  function open(data: JobApplication = APPLICATION): void {
    ref = dialog.open(CvDialogComponent, { data, width: '520px', enterAnimationDuration: '0ms', exitAnimationDuration: '0ms' });
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

  /** Zamknięcie bez wyniku (Esc daje undefined, przycisk mat-dialog-close – pusty tekst). */
  function expectClosedWithoutResult(): void {
    expect(closed.length).toBe(1);
    expect(closed[0]).toBeFalsy();
  }

  function query<T extends HTMLElement>(selector: string): T | null {
    return overlay.getContainerElement().querySelector<T>(selector);
  }

  function chooseFile(file: File): void {
    const input = query<HTMLInputElement>('input[type="file"]')!;
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
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
      imports: [CvDialogComponent],
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

  it('exposes the dialog title as the accessible name of the dialog', async () => {
    open();

    const container = query('mat-dialog-container')!;
    const title = query('h2')!;
    expect(container.getAttribute('role')).toBe('dialog');
    await vi.waitFor(() => {
      TestBed.tick();
      expect(container.getAttribute('aria-labelledby')).toBe(title.id);
    });
    expect(title.textContent).toContain('Dodaj CV');
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

    const focusable = Array.from(overlay.getContainerElement().querySelectorAll<HTMLElement>('input, button:not([disabled])'));
    expect(focusable[0]).toBe(query('input[type="file"]'));
    expect(focusable[0].tabIndex).toBe(0);
  });

  it('closes on Escape when nothing is being uploaded', async () => {
    open();

    pressEscape();
    await settled();

    expectClosedWithoutResult();
  });

  it('closes with the cancel button without sending anything', async () => {
    open();

    query<HTMLButtonElement>('.cancel-button')!.click();
    await settled();

    expectClosedWithoutResult();
    http.expectNone(CV_URL);
  });

  it('uploads through the real service: multipart PUT, progress on the progressbar, close with the application', async () => {
    open();
    chooseFile(PDF);
    clickUpload();

    const req = http.expectOne(CV_URL);
    expect(req.request.method).toBe('PUT');
    expect((req.request.body as FormData).get('file')).toBeInstanceOf(File);

    req.event({ type: HttpEventType.UploadProgress, loaded: 40, total: 100 });
    TestBed.tick();
    expect(query('mat-progress-bar')?.getAttribute('aria-valuenow')).toBe('40');
    expect(query('mat-progress-bar')?.getAttribute('aria-valuetext')).toBe('Wysyłanie… 40%');

    req.event({ type: HttpEventType.UploadProgress, loaded: 70, total: 100 });
    TestBed.tick();
    expect(query('mat-progress-bar')?.getAttribute('aria-valuenow')).toBe('70');

    const updated = { ...WITH_CV, cvFileName: 'nowe-cv.pdf' };
    req.flush(updated);
    await settled();

    expect(closed).toEqual([updated]);
  });

  it('keeps the dialog open on Escape while uploading (only "Przerwij wysyłanie" aborts it)', () => {
    open();
    chooseFile(PDF);
    clickUpload();
    const req = http.expectOne(CV_URL);

    pressEscape();

    expect(closed).toEqual([]);
    expect(req.cancelled).toBe(false);
    req.flush(WITH_CV);
  });

  it('cancels the HTTP request when "Przerwij wysyłanie" closes the dialog', async () => {
    open();
    chooseFile(PDF);
    clickUpload();
    const req = http.expectOne(CV_URL);
    expect(query('.cancel-button')?.textContent).toContain('Przerwij wysyłanie');

    query<HTMLButtonElement>('.cancel-button')!.click();
    await settled();

    expectClosedWithoutResult();
    expect(req.cancelled).toBe(true);
  });

  it('cancels the HTTP request when the dialog is closed programmatically during the upload', async () => {
    open();
    chooseFile(PDF);
    clickUpload();
    const req = http.expectOne(CV_URL);

    ref.close();
    await settled();

    expect(req.cancelled).toBe(true);
  });

  it('cancels the HTTP request when all dialogs are closed (e.g. on navigation)', async () => {
    open();
    chooseFile(PDF);
    clickUpload();
    const req = http.expectOne(CV_URL);

    dialog.closeAll();
    await settled();

    expect(req.cancelled).toBe(true);
  });

  it('allows closing with Escape again after a failed upload', async () => {
    open();
    chooseFile(PDF);
    clickUpload();
    http.expectOne(CV_URL).flush({ message: 'File is empty' }, { status: 400, statusText: 'Bad Request' });
    TestBed.tick();

    pressEscape();
    await settled();

    expectClosedWithoutResult();
  });

  it.each([
    [413, 'Payload Too Large', null, 'Plik jest za duży – maksymalny rozmiar to 5 MB'],
    [409, 'Conflict', { message: 'CV was changed by another request' }, 'CV zostało w międzyczasie zmienione – odśwież dane'],
    [400, 'Bad Request', { message: 'File is too large - maximum size is 5 MB' }, 'Plik jest za duży – maksymalny rozmiar to 5 MB'],
    [400, 'Bad Request', { message: 'CV cannot be changed in the current application status' },
      'Na tym etapie rekrutacji nie można już zmienić CV']
  ])('shows an announced, translated message for a %i response and lets the user retry', async (status, statusText, body, expected) => {
    open();
    chooseFile(PDF);
    clickUpload();

    http.expectOne(CV_URL).flush(body, { status, statusText });
    TestBed.tick();

    expect(query('[role="alert"]')!.textContent).toContain(expected);
    expect(closed).toEqual([]);
    expect(query<HTMLButtonElement>('.upload-button')!.disabled).toBe(false);
    expect(ref.componentInstance.uploading()).toBe(false);

    clickUpload();
    http.expectOne(CV_URL).flush(WITH_CV);
    await settled();
    expect(closed.length).toBe(1);
  });

  it('shows the generic message and no raw server details for a 500 response', () => {
    open();
    chooseFile(PDF);
    clickUpload();

    http.expectOne(CV_URL).flush(
      { message: 'System.IO.IOException: disk full' }, { status: 500, statusText: 'Server Error' }
    );
    TestBed.tick();

    const text = query('[role="alert"]')!.textContent ?? '';
    expect(text).toContain('Nie udało się przesłać CV');
    expect(text).not.toContain('IOException');
  });

  it('does not react to the cancelled request after the dialog was closed', async () => {
    open();
    chooseFile(PDF);
    clickUpload();
    const req = http.expectOne(CV_URL);
    const component = ref.componentInstance;

    ref.close();
    await settled();

    expect(req.cancelled).toBe(true);
    expect(component.progress()).toBe(0);
    expectClosedWithoutResult();
  });

  it('shows markup in the current CV name as text', () => {
    open({ ...WITH_CV, cvFileName: '<img src=x onerror="window.__pwned=true">.pdf' });

    expect(query('.current strong')?.textContent).toBe('<img src=x onerror="window.__pwned=true">.pdf');
    expect(overlay.getContainerElement().querySelector('img')).toBeNull();
  });

  it('shows markup in the chosen file name as text', () => {
    open();
    chooseFile(new File(['%PDF-'], '<script>window.__pwned=true</script>.pdf'));

    expect(query('.file-name')?.textContent).toBe('<script>window.__pwned=true</script>.pdf');
    expect(overlay.getContainerElement().querySelector('script')).toBeNull();
  });

  it('leaves no pending timers after the dialog is closed during an upload', () => {
    vi.useFakeTimers();
    try {
      open();
      chooseFile(PDF);
      clickUpload();
      const req = http.expectOne(CV_URL);

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
