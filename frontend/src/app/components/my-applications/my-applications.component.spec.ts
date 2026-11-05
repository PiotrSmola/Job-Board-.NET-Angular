import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Router, provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Observable, Subject, of, throwError } from 'rxjs';
import { MyApplicationsComponent } from './my-applications.component';
import { JobApplicationService } from '../../services/job-application.service';
import { ApplicationStatus, JobApplication } from '../../models/job-application.model';

function makeApplication(id: number, status = ApplicationStatus.Submitted): JobApplication {
  return {
    id, jobOfferId: id * 10, userId: 'u1', userEmail: 'jan@example.com', userFullName: 'Jan Kowalski',
    jobTitle: `Oferta ${id}`, company: 'Acme', companyId: 1, note: null, appliedAt: new Date('2026-01-01T10:00:00Z'),
    status, statusChangedAt: '2026-01-01T10:00:00Z'
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
    snackBarOpen = vi.fn();
    alertSpy = vi.spyOn(window, 'alert').mockImplementation(() => undefined);
    confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [MyApplicationsComponent],
      providers: [
        provideRouter([]),
        { provide: JobApplicationService, useValue: { getMyApplications, getApplication, withdraw } },
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
});
