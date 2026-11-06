import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { Observable, Subject, of, throwError } from 'rxjs';
import { ApplicationHistoryDialogComponent } from './application-history-dialog.component';
import { JobApplicationService } from '../../../services/job-application.service';
import { ApplicationStatus, ApplicationStatusHistoryEntry, JobApplication } from '../../../models/job-application.model';

const APPLICATION: JobApplication = {
  id: 7, jobOfferId: 3, userId: 'u1', userEmail: 'jan@example.com', userFullName: 'Jan Kowalski',
  jobTitle: 'Programista Angular', company: 'Acme', companyId: 1, note: null, appliedAt: new Date('2026-01-01T10:00:00Z'),
  status: ApplicationStatus.Interview, statusChangedAt: '2026-01-03T10:00:00Z',
  hasCv: false, cvFileName: null, cvSize: null, cvUploadedAt: null
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
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [ApplicationHistoryDialogComponent],
      providers: [
        { provide: JobApplicationService, useValue: { getApplication } },
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
});
