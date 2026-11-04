import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Observable, of, throwError } from 'rxjs';
import { JobDetailComponent } from './job-detail.component';
import { JobService } from '../../services/job.service';
import { JobApplicationService } from '../../services/job-application.service';
import { AuthService } from '../../services/auth.service';
import { JobOffer, WorkType } from '../../models/job-offer.model';

const JOB: JobOffer = {
  id: 5, title: 'Angular Developer', company: 'Acme', description: 'Opis', salaryMin: 1000, salaryMax: 2000,
  location: 'Rzeszów', workType: WorkType.Remote, createdAt: new Date('2026-01-01')
};

describe('JobDetailComponent', () => {
  let fixture: ComponentFixture<JobDetailComponent>;
  let component: JobDetailComponent;
  let getJob: ReturnType<typeof vi.fn>;
  let checkIfApplied: ReturnType<typeof vi.fn>;
  let applyToJob: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;
  let alertSpy: ReturnType<typeof vi.spyOn>;

  function create(job: Observable<JobOffer> = of(JOB)): void {
    getJob.mockReturnValue(job);
    fixture = TestBed.createComponent(JobDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function httpError(status: number, error: unknown = null): HttpErrorResponse {
    return new HttpErrorResponse({ status, statusText: 'Error', url: '/api/x', error });
  }

  beforeEach(async () => {
    getJob = vi.fn();
    checkIfApplied = vi.fn().mockReturnValue(of({ hasApplied: false }));
    applyToJob = vi.fn();
    snackBarOpen = vi.fn();
    alertSpy = vi.spyOn(window, 'alert').mockImplementation(() => undefined);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [JobDetailComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id: '5' })) } },
        { provide: JobService, useValue: { getJob } },
        { provide: JobApplicationService, useValue: { checkIfApplied, applyToJob } },
        { provide: AuthService, useValue: { isAuthenticated: () => true, isAdmin: () => false } },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } }
      ]
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  it('loads the job and the application status', () => {
    create();

    expect(getJob).toHaveBeenCalledWith(5);
    expect(checkIfApplied).toHaveBeenCalledWith(5);
    expect(component.job).toEqual(JOB);
    expect(component.hasApplied).toBe(false);
  });

  describe('loadJob errors', () => {
    it('shows the translated message when the job does not exist', () => {
      create(throwError(() => httpError(404, { message: 'Job offer not found' })));

      expect(component.loading).toBe(false);
      expect(snackBarOpen).toHaveBeenCalledTimes(1);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Oferta nie została znaleziona');
    });

    it('shows a generic message for server errors', () => {
      create(throwError(() => httpError(500)));

      expect(snackBarOpen.mock.calls[0][0]).toBe('Nie udało się wczytać oferty');
    });
  });

  it('stays silent when checking the application status fails with 401', () => {
    checkIfApplied.mockReturnValue(throwError(() => httpError(401)));
    create();

    expect(component.hasApplied).toBe(false);
    expect(component.checkingApplication).toBe(false);
    expect(snackBarOpen).not.toHaveBeenCalled();
    expect(alertSpy).not.toHaveBeenCalled();
  });

  describe('applyToJob', () => {
    it('sends the application and shows a success snackbar', () => {
      create();
      applyToJob.mockReturnValue(of({}));

      component.applyToJob('Notatka');

      expect(applyToJob).toHaveBeenCalledWith({ jobOfferId: 5, note: 'Notatka' });
      expect(component.hasApplied).toBe(true);
      expect(component.applying).toBe(false);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Aplikacja wysłana pomyślnie!');
      expect(alertSpy).not.toHaveBeenCalled();
    });

    it('sends null instead of an empty note', () => {
      create();
      applyToJob.mockReturnValue(of({}));

      component.applyToJob('');

      expect(applyToJob).toHaveBeenCalledWith({ jobOfferId: 5, note: null });
    });

    it('translates "already applied" and marks the job as applied', () => {
      create();
      applyToJob.mockReturnValue(throwError(() => httpError(400, { message: 'You have already applied to this job' })));

      component.applyToJob('');

      expect(component.applying).toBe(false);
      expect(component.hasApplied).toBe(true);
      expect(snackBarOpen).toHaveBeenCalledTimes(1);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Już aplikowałeś na tę ofertę');
      expect(alertSpy).not.toHaveBeenCalled();
    });

    it('shows a generic message for server errors', () => {
      create();
      applyToJob.mockReturnValue(throwError(() => httpError(500)));

      component.applyToJob('');

      expect(component.hasApplied).toBe(false);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Błąd podczas aplikowania');
    });

    it.each([401, 403])('does not duplicate the interceptor notice for %i', status => {
      create();
      applyToJob.mockReturnValue(throwError(() => httpError(status)));

      component.applyToJob('');

      expect(component.applying).toBe(false);
      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(alertSpy).not.toHaveBeenCalled();
    });
  });
});
