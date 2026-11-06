import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { Observable, Subject, of, throwError } from 'rxjs';
import { ApplicationHistoryDialogComponent } from './application-history-dialog.component';
import { JobApplicationService } from '../../../services/job-application.service';
import {
  ApplicationAttachment, ApplicationStatus, ApplicationStatusHistoryEntry, AttachmentKind, JobApplication
} from '../../../models/job-application.model';

const APPLICATION: JobApplication = {
  id: 7, jobOfferId: 3, userId: 'u1', userEmail: 'jan@example.com', userFullName: 'Jan Kowalski',
  jobTitle: 'Programista Angular', company: 'Acme', companyId: 1, note: null, appliedAt: new Date('2026-01-01T10:00:00Z'),
  status: ApplicationStatus.Interview, statusChangedAt: '2026-01-03T10:00:00Z',
  attachments: [], attachmentsTotalSize: 0
};

const HISTORY: ApplicationStatusHistoryEntry[] = [
  { status: ApplicationStatus.Submitted, changedAt: '2026-01-01T10:00:00Z', comment: null, changedBy: 'jan@example.com' },
  { status: ApplicationStatus.InReview, changedAt: '2026-01-02T10:00:00Z', comment: 'Sprawdzamy CV', changedBy: 'admin@jobboard.com' },
  { status: ApplicationStatus.Interview, changedAt: '2026-01-03T10:00:00Z', comment: 'Rozmowa w czwartek', changedBy: 'admin@jobboard.com' }
];

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('ApplicationHistoryDialogComponent', () => {
  let fixture: ComponentFixture<ApplicationHistoryDialogComponent>;
  let component: ApplicationHistoryDialogComponent;
  let getApplication: ReturnType<typeof vi.fn>;
  let downloadAttachment: ReturnType<typeof vi.fn>;

  function create(response: Observable<JobApplication>, data: JobApplication = APPLICATION): void {
    getApplication.mockReturnValue(response);
    TestBed.overrideProvider(MAT_DIALOG_DATA, { useValue: data });
    fixture = TestBed.createComponent(ApplicationHistoryDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function httpError(status: number, error: unknown = null): HttpErrorResponse {
    return new HttpErrorResponse({ status, statusText: 'Error', url: '/api/jobapplications/7', error });
  }

  beforeEach(async () => {
    getApplication = vi.fn();
    downloadAttachment = vi.fn().mockReturnValue(of(undefined));
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [ApplicationHistoryDialogComponent],
      providers: [
        { provide: JobApplicationService, useValue: { getApplication, downloadAttachment } },
        { provide: MAT_DIALOG_DATA, useValue: APPLICATION }
      ]
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  it('requests the application details by id (GET /api/jobapplications/{id})', () => {
    create(of({ ...APPLICATION, history: HISTORY }));

    expect(getApplication).toHaveBeenCalledTimes(1);
    expect(getApplication).toHaveBeenCalledWith(7);
  });

  it('shows the candidate and the offer in the header', () => {
    create(of({ ...APPLICATION, history: HISTORY }));

    expect(root().querySelector('.summary')?.textContent).toContain('Jan Kowalski');
    expect(root().querySelector('.summary')?.textContent).toContain('Programista Angular');
  });

  it('falls back to the e-mail when the full name is empty', () => {
    create(of({ ...APPLICATION, history: HISTORY }), { ...APPLICATION, userFullName: '' });

    expect(root().querySelector('.summary strong')?.textContent).toBe('jan@example.com');
  });

  it('shows a spinner until the history arrives', () => {
    const pending = new Subject<JobApplication>();
    create(pending);

    expect(component.loading()).toBe(true);
    expect(root().querySelector('mat-spinner')).not.toBeNull();
    expect(root().querySelector('app-status-timeline')).toBeNull();

    pending.next({ ...APPLICATION, history: HISTORY });

    expect(component.loading()).toBe(false);
    expect(root().querySelector('mat-spinner')).toBeNull();
    expect(root().querySelector('app-status-timeline')).not.toBeNull();
  });

  it('renders the timeline chronologically with comments and authors', () => {
    create(of({ ...APPLICATION, history: HISTORY }));

    const items = Array.from(root().querySelectorAll('app-status-timeline li'));
    expect(items.map(li => li.querySelector('app-status-chip')?.textContent?.trim())).toEqual(['Złożona', 'W analizie', 'Rozmowa']);
    expect(items[1].querySelector('.comment')?.textContent).toContain('Sprawdzamy CV');
    expect(items.map(li => li.querySelector('.author')?.textContent?.trim())).toEqual([
      'jan@example.com', 'admin@jobboard.com', 'admin@jobboard.com'
    ]);
    expect(component.history()).toEqual(HISTORY);
  });

  it.each([[null], [undefined]])('shows the empty state when the API returns history = %s', history => {
    create(of({ ...APPLICATION, history }));

    expect(root().querySelector('app-status-timeline .empty')?.textContent).toContain('Brak historii');
  });

  describe('errors', () => {
    it('shows a generic message and no timeline when loading fails', () => {
      create(throwError(() => httpError(500)));

      expect(component.loading()).toBe(false);
      expect(root().querySelector('.error')?.textContent).toContain('Nie udało się wczytać historii aplikacji');
      expect(root().querySelector('app-status-timeline')).toBeNull();
      expect(root().querySelector('mat-spinner')).toBeNull();
    });

    it('shows the translated API message when the application does not exist', () => {
      create(throwError(() => httpError(404, { message: 'Application not found' })));

      expect(root().querySelector('.error')?.textContent).toContain('Aplikacja nie została znaleziona');
    });

    it.each([401, 403])('shows an access message for %i', status => {
      create(throwError(() => httpError(status)));

      expect(root().querySelector('.error')?.textContent).toContain('Brak dostępu do historii aplikacji');
    });

    it('still offers the close button after an error', () => {
      create(throwError(() => httpError(500)));

      const buttons = Array.from(root().querySelectorAll('mat-dialog-actions button')).map(b => b.textContent?.trim());
      expect(buttons).toEqual(['Zamknij']);
    });
  });

  describe('attachments', () => {
    const CV: ApplicationAttachment = { id: 31, kind: AttachmentKind.Cv, fileName: 'jan-cv.docx', size: 2048, uploadedAt: '2026-01-02T10:00:00Z' };
    const LETTER: ApplicationAttachment = { id: 32, kind: AttachmentKind.CoverLetter, fileName: 'list.pdf', size: 1024, uploadedAt: '2026-01-03T10:00:00Z' };
    const WITH_ATTACHMENTS: JobApplication = { ...APPLICATION, attachments: [CV, LETTER], attachmentsTotalSize: 3072 };

    function downloadButtons(): HTMLButtonElement[] {
      return Array.from(root().querySelectorAll<HTMLButtonElement>('.attachments .download-button'));
    }

    it('says that the candidate did not attach anything and offers no download', () => {
      create(of({ ...APPLICATION, history: HISTORY }));

      expect(root().querySelector('.attachments .empty')?.textContent).toBe('Kandydat nie dołączył załączników');
      expect(downloadButtons().length).toBe(0);
    });

    it('lists the attachments under a heading that names the section', () => {
      create(of({ ...WITH_ATTACHMENTS, history: HISTORY }), WITH_ATTACHMENTS);

      const section = root().querySelector('section.attachments')!;
      const heading = root().querySelector('#' + section.getAttribute('aria-labelledby'));
      expect(heading?.textContent).toBe('Załączniki');
      expect(Array.from(section.querySelectorAll('.meta')).map(e => e.textContent)).toEqual([
        'CV · 2 KB · 02.01.2026', 'List motywacyjny · 1 KB · 03.01.2026'
      ]);
    });

    it('labels every download button with the kind and the file name', () => {
      create(of({ ...WITH_ATTACHMENTS, history: HISTORY }), WITH_ATTACHMENTS);

      expect(downloadButtons().map(b => b.getAttribute('aria-label'))).toEqual([
        'Pobierz CV: jan-cv.docx', 'Pobierz List motywacyjny: list.pdf'
      ]);
    });

    it('does not let the recruiter delete attachments', () => {
      create(of({ ...WITH_ATTACHMENTS, history: HISTORY }), WITH_ATTACHMENTS);

      expect(root().querySelector('.delete-button')).toBeNull();
    });

    it('downloads an attachment of the application', () => {
      create(of({ ...WITH_ATTACHMENTS, history: HISTORY }), WITH_ATTACHMENTS);

      downloadButtons()[1].click();

      expect(downloadAttachment).toHaveBeenCalledWith(7, LETTER);
    });

    it('shows the attachments returned with the details (fresher than the list row)', () => {
      create(of({ ...WITH_ATTACHMENTS, attachments: [LETTER], history: HISTORY }), WITH_ATTACHMENTS);

      expect(downloadButtons().map(b => b.getAttribute('aria-label'))).toEqual(['Pobierz List motywacyjny: list.pdf']);
    });

    it('keeps the attachments of the row when the details cannot be loaded', () => {
      create(throwError(() => httpError(500)), WITH_ATTACHMENTS);

      expect(downloadButtons().length).toBe(2);
    });

    it('shows the translated error in an alert when the download fails', () => {
      downloadAttachment.mockReturnValue(throwError(() => httpError(404, { message: 'Attachment not found' })));
      create(of({ ...WITH_ATTACHMENTS, history: HISTORY }), WITH_ATTACHMENTS);

      downloadButtons()[0].click();

      const error = root().querySelector('.attachments [role="alert"]');
      expect(error?.textContent).toContain('Nie znaleziono załącznika');
    });

    it('marks the button with aria-disabled while downloading and stops the download when the dialog is closed', () => {
      const pending = new Subject<void>();
      downloadAttachment.mockReturnValue(pending);
      create(of({ ...WITH_ATTACHMENTS, history: HISTORY }), WITH_ATTACHMENTS);

      downloadButtons()[0].click();
      expect(downloadButtons()[0].getAttribute('aria-disabled')).toBe('true');
      expect(downloadButtons()[0].disabled).toBe(false);
      fixture.destroy();

      expect(pending.observed).toBe(false);
    });

    it('stops loading the history when the dialog is closed', () => {
      const pending = new Subject<JobApplication>();
      create(pending);

      fixture.destroy();

      expect(pending.observed).toBe(false);
    });

    it('renders markup in attachment names as text', () => {
      const name = '<img src=x onerror="window.__pwned=true">.pdf';
      const data = { ...APPLICATION, attachments: [{ ...CV, fileName: name }], attachmentsTotalSize: 2048 };
      create(of({ ...data, history: HISTORY }), data);

      expect(root().querySelector('.file-name')?.textContent).toBe(name);
      expect(root().querySelector('img')).toBeNull();
    });
  });
});
