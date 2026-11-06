import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Router, provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatDialog } from '@angular/material/dialog';
import { Observable, Subject, of, throwError } from 'rxjs';
import { MyApplicationsComponent } from './my-applications.component';
import { CvDialogComponent } from './cv-dialog.component';
import { JobApplicationService } from '../../services/job-application.service';
import { ApplicationStatus, JobApplication } from '../../models/job-application.model';

function makeApplication(id: number, status = ApplicationStatus.Submitted): JobApplication {
  return {
    id, jobOfferId: id * 10, userId: 'u1', userEmail: 'jan@example.com', userFullName: 'Jan Kowalski',
    jobTitle: `Oferta ${id}`, company: 'Acme', companyId: 1, note: null, appliedAt: new Date('2026-01-01T10:00:00Z'),
    status, statusChangedAt: '2026-01-01T10:00:00Z',
    hasCv: false, cvFileName: null, cvSize: null, cvUploadedAt: null
  };
}

const HISTORY = [
  { status: ApplicationStatus.Submitted, changedAt: '2026-01-01T10:00:00Z', comment: null, changedBy: null },
  { status: ApplicationStatus.Interview, changedAt: '2026-01-02T10:00:00Z', comment: 'Zapraszamy', changedBy: null }
];

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('MyApplicationsComponent', () => {
  let fixture: ComponentFixture<MyApplicationsComponent>;
  let component: MyApplicationsComponent;
  let getMyApplications: ReturnType<typeof vi.fn>;
  let getApplication: ReturnType<typeof vi.fn>;
  let withdraw: ReturnType<typeof vi.fn>;
  let downloadCv: ReturnType<typeof vi.fn>;
  let deleteCv: ReturnType<typeof vi.fn>;
  let dialogOpen: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;
  let alertSpy: ReturnType<typeof vi.spyOn>;
  let confirmSpy: ReturnType<typeof vi.spyOn>;

  function create(initial: Observable<JobApplication[]> = of([makeApplication(1), makeApplication(2)])): void {
    getMyApplications.mockReturnValue(initial);
    fixture = TestBed.createComponent(MyApplicationsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(async () => {
    getMyApplications = vi.fn();
    getApplication = vi.fn().mockReturnValue(of({ ...makeApplication(1, ApplicationStatus.Interview), history: HISTORY }));
    withdraw = vi.fn().mockImplementation((id: number) => of({
      ...makeApplication(id, ApplicationStatus.Withdrawn),
      history: [...HISTORY, { status: ApplicationStatus.Withdrawn, changedAt: '2026-01-03T10:00:00Z', comment: null, changedBy: null }]
    }));
    downloadCv = vi.fn().mockReturnValue(of(undefined));
    deleteCv = vi.fn().mockReturnValue(of(undefined));
    dialogOpen = vi.fn().mockReturnValue({ afterClosed: () => of(undefined) });
    snackBarOpen = vi.fn();
    alertSpy = vi.spyOn(window, 'alert').mockImplementation(() => undefined);
    confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [MyApplicationsComponent],
      providers: [
        provideRouter([]),
        { provide: JobApplicationService, useValue: { getMyApplications, getApplication, withdraw, downloadCv, deleteCv } },
        { provide: MatDialog, useValue: { open: dialogOpen } },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } }
      ]
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  it('loads and shows the current user applications', () => {
    create();

    expect(component.applications.map(a => a.id)).toEqual([1, 2]);
    expect(component.loading).toBe(false);
  });

  it('shows a snackbar when loading fails', () => {
    create(throwError(() => new HttpErrorResponse({ status: 500 })));

    expect(snackBarOpen).toHaveBeenCalledTimes(1);
    expect(snackBarOpen.mock.calls[0][0]).toBe('Błąd podczas ładowania aplikacji');
    expect(alertSpy).not.toHaveBeenCalled();
    expect(component.loading).toBe(false);
  });

  it.each([401, 403])('does not duplicate the interceptor notice when loading fails with %i', status => {
    create(throwError(() => new HttpErrorResponse({ status, statusText: 'Error', url: '/api/jobapplications/my-applications' })));

    expect(snackBarOpen).not.toHaveBeenCalled();
    expect(alertSpy).not.toHaveBeenCalled();
    expect(component.loading).toBe(false);
  });

  it('shows the Polish status label of each application', () => {
    create(of([makeApplication(1, ApplicationStatus.Interview), makeApplication(2, ApplicationStatus.Rejected)]));

    const labels = Array.from(root().querySelectorAll('app-status-chip')).map(e => e.textContent?.trim());
    expect(labels).toEqual(['Rozmowa', 'Odrzucona']);
  });

  describe('withdrawing', () => {
    it('withdraws after confirmation and updates the status in place', () => {
      create();

      component.withdrawApplication(component.applications[0]);

      expect(withdraw).toHaveBeenCalledWith(1);
      expect(component.applications.map(a => a.status)).toEqual([ApplicationStatus.Withdrawn, ApplicationStatus.Submitted]);
      expect(component.withdrawingId).toBeNull();
      expect(snackBarOpen.mock.calls[0][0]).toBe('Aplikacja została wycofana');
    });

    it('does not withdraw when the confirmation is declined', () => {
      create();
      confirmSpy.mockReturnValue(false);

      component.withdrawApplication(component.applications[0]);

      expect(withdraw).not.toHaveBeenCalled();
      expect(component.applications[0].status).toBe(ApplicationStatus.Submitted);
    });

    it('offers withdrawal only for statuses the API allows', () => {
      const statuses = [
        ApplicationStatus.Submitted, ApplicationStatus.InReview, ApplicationStatus.Interview, ApplicationStatus.Offered,
        ApplicationStatus.Hired, ApplicationStatus.Rejected, ApplicationStatus.Withdrawn
      ];
      create(of(statuses.map((status, i) => makeApplication(i + 1, status))));

      expect(component.applications.map(a => component.canWithdraw(a))).toEqual([true, true, true, true, false, false, false]);
      expect(root().querySelectorAll('button[mattooltip="Wycofaj aplikację"]').length).toBe(4);
    });

    it('keeps the status and shows the translated API message when withdrawal is rejected', () => {
      create();
      withdraw.mockReturnValue(throwError(() => new HttpErrorResponse({
        status: 400, error: { message: 'Application cannot be withdrawn in its current status' }
      })));

      component.withdrawApplication(component.applications[0]);

      expect(component.applications[0].status).toBe(ApplicationStatus.Submitted);
      expect(component.withdrawingId).toBeNull();
      expect(snackBarOpen.mock.calls[0][0]).toBe('Tej aplikacji nie można już wycofać');
    });

    it('shows a generic message when withdrawal fails on the server', () => {
      create();
      withdraw.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

      component.withdrawApplication(component.applications[0]);

      expect(snackBarOpen.mock.calls[0][0]).toBe('Błąd podczas wycofywania aplikacji');
    });

    it.each([401, 403])('does not duplicate the interceptor notice when withdrawal fails with %i', status => {
      create();
      withdraw.mockReturnValue(throwError(() => new HttpErrorResponse({ status, statusText: 'Error' })));

      component.withdrawApplication(component.applications[0]);

      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(alertSpy).not.toHaveBeenCalled();
      expect(component.withdrawingId).toBeNull();
    });
  });

  describe('history', () => {
    it('loads the history once and toggles the timeline', () => {
      create();

      component.toggleHistory(component.applications[0]);

      expect(getApplication).toHaveBeenCalledWith(1);
      expect(component.expandedId).toBe(1);
      expect(root().querySelectorAll('app-status-timeline li').length).toBe(2);
      expect(root().querySelector('app-status-timeline')?.textContent).toContain('Zapraszamy');

      component.toggleHistory(component.applications[0]);
      expect(component.expandedId).toBeNull();

      component.toggleHistory(component.applications[0]);
      expect(getApplication).toHaveBeenCalledTimes(1);
    });

    it('collapses and shows a snackbar when the history cannot be loaded', () => {
      create();
      getApplication.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

      component.toggleHistory(component.applications[0]);

      expect(component.expandedId).toBeNull();
      expect(snackBarOpen.mock.calls[0][0]).toBe('Nie udało się wczytać historii aplikacji');
    });

    it('keeps the spinner of a row expanded later when the history of an earlier row arrives', () => {
      create();
      const first = new Subject<JobApplication>();
      const second = new Subject<JobApplication>();
      getApplication.mockImplementation((id: number) => id === 1 ? first : second);

      component.toggleHistory(component.applications[0]);
      component.toggleHistory(component.applications[1]);
      first.next({ ...makeApplication(1), history: HISTORY });

      expect(component.expandedId).toBe(2);
      expect(component.historyLoadingId).toBe(2);
      expect(component.histories[1].length).toBe(2);

      second.next({ ...makeApplication(2), history: HISTORY.slice(0, 1) });
      expect(component.historyLoadingId).toBeNull();
      expect(root().querySelectorAll('app-status-timeline li').length).toBe(1);
    });

    it('does not collapse a row expanded later when the history of an earlier row fails', () => {
      create();
      const first = new Subject<JobApplication>();
      const second = new Subject<JobApplication>();
      getApplication.mockImplementation((id: number) => id === 1 ? first : second);

      component.toggleHistory(component.applications[0]);
      component.toggleHistory(component.applications[1]);
      first.error(new HttpErrorResponse({ status: 500 }));

      expect(component.expandedId).toBe(2);
      expect(component.historyLoadingId).toBe(2);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Nie udało się wczytać historii aplikacji');
    });

    it('reloads the cached history of an expanded row after withdrawal without history in the response', () => {
      create();
      component.toggleHistory(component.applications[0]);
      expect(component.histories[1].length).toBe(2);
      withdraw.mockReturnValue(of(makeApplication(1, ApplicationStatus.Withdrawn)));
      const withWithdrawal = [...HISTORY, { status: ApplicationStatus.Withdrawn, changedAt: '2026-01-03T10:00:00Z', comment: null, changedBy: null }];
      getApplication.mockReturnValue(of({ ...makeApplication(1, ApplicationStatus.Withdrawn), history: withWithdrawal }));

      component.withdrawApplication(component.applications[0]);

      expect(getApplication).toHaveBeenCalledTimes(2);
      expect(component.histories[1].length).toBe(3);
    });

    it('drops the cached history of a collapsed row after withdrawal without history in the response', () => {
      create();
      component.toggleHistory(component.applications[0]);
      component.toggleHistory(component.applications[0]);
      withdraw.mockReturnValue(of(makeApplication(1, ApplicationStatus.Withdrawn)));

      component.withdrawApplication(component.applications[0]);

      expect(component.histories[1]).toBeUndefined();
      expect(getApplication).toHaveBeenCalledTimes(1);
      component.toggleHistory(component.applications[0]);
      expect(getApplication).toHaveBeenCalledTimes(2);
    });

    it('uses the history returned by withdraw without another request', () => {
      create();
      component.withdrawApplication(component.applications[0]);

      component.toggleHistory(component.applications[0]);

      expect(getApplication).not.toHaveBeenCalled();
      expect(component.histories[1].length).toBe(3);
    });
  });

  it('viewJobDetails navigates to the job page', () => {
    create();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    component.viewJobDetails(20);

    expect(navigate).toHaveBeenCalledWith(['/jobs', 20]);
  });

  describe('CV', () => {
    const CV_FIELDS = { hasCv: true, cvFileName: 'moje-cv.pdf', cvSize: 350 * 1024, cvUploadedAt: '2026-01-02T10:00:00Z' };

    function withCv(id: number, status = ApplicationStatus.Submitted): JobApplication {
      return { ...makeApplication(id, status), ...CV_FIELDS };
    }

    function cvCell(index: number): HTMLElement {
      return root().querySelectorAll<HTMLElement>('td.mat-column-cv')[index];
    }

    it('shows the CV name, size and upload date', () => {
      create(of([withCv(1), makeApplication(2)]));

      expect(cvCell(0).textContent).toContain('moje-cv.pdf');
      expect(cvCell(0).textContent).toContain('350 KB');
      expect(cvCell(0).textContent).toContain('02.01.2026');
      expect(cvCell(1).textContent).toContain('Brak');
    });

    it('labels the icon buttons with the action and the file name', () => {
      create(of([withCv(1), makeApplication(2)]));

      const labels = (index: number) => Array.from(cvCell(index).querySelectorAll('button')).map(b => b.getAttribute('aria-label'));
      expect(labels(0)).toEqual(['Pobierz CV: moje-cv.pdf', 'Zmień CV: moje-cv.pdf', 'Usuń CV: moje-cv.pdf']);
      expect(labels(1)).toEqual(['Dodaj CV do aplikacji: Oferta 2']);
    });

    it('offers changing the CV only in the statuses the API allows, but always the download', () => {
      const statuses = [
        ApplicationStatus.Submitted, ApplicationStatus.InReview, ApplicationStatus.Interview, ApplicationStatus.Offered,
        ApplicationStatus.Hired, ApplicationStatus.Rejected, ApplicationStatus.Withdrawn
      ];
      create(of(statuses.map((status, i) => withCv(i + 1, status))));

      const counts = statuses.map((_, i) => cvCell(i).querySelectorAll('button').length);
      expect(counts).toEqual([3, 3, 3, 3, 1, 1, 1]);
    });

    it('downloads the CV with the stored file name as a fallback', () => {
      create(of([withCv(1)]));

      component.downloadCv(component.applications[0]);

      expect(downloadCv).toHaveBeenCalledWith(1, 'moje-cv.pdf');
      expect(component.downloadingCvId).toBeNull();
    });

    it('marks the row as downloading until the file arrives', () => {
      create(of([withCv(1)]));
      const pending = new Subject<void>();
      downloadCv.mockReturnValue(pending);

      component.downloadCv(component.applications[0]);
      expect(component.downloadingCvId).toBe(1);
      expect(cvCell(0).querySelector<HTMLButtonElement>('button.cv-file')!.disabled).toBe(true);

      pending.next();
      pending.complete();
      expect(component.downloadingCvId).toBeNull();
    });

    it('shows the translated API message when the download fails', () => {
      create(of([withCv(1)]));
      downloadCv.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404, error: { message: 'Application not found' } })));

      component.downloadCv(component.applications[0]);

      expect(snackBarOpen.mock.calls[0][0]).toBe('Aplikacja nie została znaleziona');
      expect(component.downloadingCvId).toBeNull();
    });

    it('deletes the CV after confirmation and clears the CV fields', () => {
      create(of([withCv(1)]));

      component.deleteCv(component.applications[0]);

      expect(confirmSpy).toHaveBeenCalled();
      expect(deleteCv).toHaveBeenCalledWith(1);
      expect(component.applications[0]).toMatchObject({ hasCv: false, cvFileName: null, cvSize: null, cvUploadedAt: null });
      expect(snackBarOpen.mock.calls[0][0]).toBe('CV zostało usunięte');
      expect(cvCell(0).textContent).toContain('Brak');
    });

    it('does not delete the CV when the confirmation is declined', () => {
      create(of([withCv(1)]));
      confirmSpy.mockReturnValue(false);

      component.deleteCv(component.applications[0]);

      expect(deleteCv).not.toHaveBeenCalled();
      expect(component.applications[0].hasCv).toBe(true);
    });

    it('keeps the CV and shows the translated message when deleting is rejected', () => {
      create(of([withCv(1)]));
      deleteCv.mockReturnValue(throwError(() => new HttpErrorResponse({
        status: 400, error: { message: 'CV cannot be changed in the current application status' }
      })));

      component.deleteCv(component.applications[0]);

      expect(component.applications[0].hasCv).toBe(true);
      expect(component.deletingCvId).toBeNull();
      expect(snackBarOpen.mock.calls[0][0]).toBe('Na tym etapie rekrutacji nie można już zmienić CV');
    });

    it('opens the CV dialog and puts the updated application into the list', () => {
      create();
      const updated = { ...withCv(1), statusChangedAt: '2026-01-05T10:00:00Z' };
      dialogOpen.mockReturnValue({ afterClosed: () => of(updated) });

      const original = component.applications[0];
      component.openCvDialog(original);

      expect(dialogOpen).toHaveBeenCalledWith(CvDialogComponent, expect.objectContaining({ data: original }));
      expect(component.applications[0].cvFileName).toBe('moje-cv.pdf');
      expect(snackBarOpen.mock.calls[0][0]).toBe('CV zostało dołączone do aplikacji');
    });

    it('reports a replaced CV', () => {
      create(of([withCv(1)]));
      dialogOpen.mockReturnValue({ afterClosed: () => of({ ...withCv(1), cvFileName: 'nowe.docx' }) });

      component.openCvDialog(component.applications[0]);

      expect(component.applications[0].cvFileName).toBe('nowe.docx');
      expect(snackBarOpen.mock.calls[0][0]).toBe('CV zostało zastąpione');
    });

    it('leaves the list unchanged when the CV dialog is cancelled', () => {
      create();
      dialogOpen.mockReturnValue({ afterClosed: () => of(undefined) });

      component.openCvDialog(component.applications[0]);

      expect(component.applications[0].hasCv).toBe(false);
      expect(snackBarOpen).not.toHaveBeenCalled();
    });

    it('stops listening to a pending download when the page is destroyed', () => {
      create(of([withCv(1)]));
      const pending = new Subject<void>();
      downloadCv.mockReturnValue(pending);

      component.downloadCv(component.applications[0]);
      fixture.destroy();

      expect(pending.observed).toBe(false);
    });
  });
});
