import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { Observable, Subject, of, throwError } from 'rxjs';
import { ApplyDialogComponent, CV_UPLOAD_FAILED_MESSAGE, JobDetailComponent } from './job-detail.component';
import { JobService } from '../../services/job.service';
import { JobApplicationService } from '../../services/job-application.service';
import { AuthService } from '../../services/auth.service';
import { JobOffer, JobOfferStatus, WorkType } from '../../models/job-offer.model';
import { ApplicationStatus, CvUploadEvent } from '../../models/job-application.model';

const JOB: JobOffer = {
  id: 5, title: 'Angular Developer', company: 'Acme', companyId: 1, description: 'Opis', salaryMin: 1000, salaryMax: 2000,
  location: 'Rzeszów', workType: WorkType.Remote, createdAt: new Date('2026-01-01'),
  status: JobOfferStatus.Active, expiresAt: null, isExpired: false
};

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('JobDetailComponent', () => {
  let fixture: ComponentFixture<JobDetailComponent>;
  let component: JobDetailComponent;
  let getJob: ReturnType<typeof vi.fn>;
  let checkIfApplied: ReturnType<typeof vi.fn>;
  let applyToJob: ReturnType<typeof vi.fn>;
  let uploadCv: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;
  let alertSpy: ReturnType<typeof vi.spyOn>;
  let authenticated: boolean;
  let admin: boolean;
  let employer: boolean;

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
    authenticated = true;
    admin = false;
    employer = false;
    checkIfApplied = vi.fn().mockReturnValue(of({ hasApplied: false }));
    applyToJob = vi.fn();
    uploadCv = vi.fn();
    snackBarOpen = vi.fn();
    alertSpy = vi.spyOn(window, 'alert').mockImplementation(() => undefined);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [JobDetailComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id: '5' })) } },
        { provide: JobService, useValue: { getJob } },
        { provide: JobApplicationService, useValue: { checkIfApplied, applyToJob, uploadCv } },
        { provide: AuthService, useValue: { isAuthenticated: () => authenticated, isAdmin: () => admin, isEmployer: () => employer } },
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

  describe('offer lifecycle and application status', () => {
    function text(): string {
      fixture.detectChanges();
      return (fixture.nativeElement as HTMLElement).textContent ?? '';
    }

    it.each([
      [{ status: JobOfferStatus.Closed, isExpired: false }, 'Rekrutacja na to stanowisko została zakończona'],
      [{ status: JobOfferStatus.Active, isExpired: true }, 'Ta oferta wygasła']
    ])('shows a banner and blocks applying for %o', (state, bannerText) => {
      create(of({ ...JOB, ...state }));

      expect(component.acceptsApplications).toBe(false);
      expect(text()).toContain(bannerText);
      expect(text()).toContain('Aplikowanie zamknięte');
      expect(text()).not.toContain('Aplikuj na to stanowisko');
    });

    it('shows no banner and the expiration date for an active offer', () => {
      create(of({ ...JOB, expiresAt: '2026-12-31T22:59:59Z' }));

      expect(component.banner).toBeNull();
      expect(text()).toContain('Aktualna do');
      expect(text()).toContain('Aplikuj na to stanowisko');
    });

    it('shows the current status of an existing application', () => {
      checkIfApplied.mockReturnValue(of({ hasApplied: true, applicationId: 3, status: ApplicationStatus.Interview }));
      create();

      expect(component.applicationStatus).toBe(ApplicationStatus.Interview);
      expect(text()).toContain('Rozmowa');
      expect(text()).toContain('Już aplikowałeś');
    });

    it('offers applying again after withdrawal and reports the reactivation', () => {
      checkIfApplied.mockReturnValue(of({ hasApplied: false, applicationId: 3, status: ApplicationStatus.Withdrawn }));
      create();

      expect(component.hasWithdrawn).toBe(true);
      expect(text()).toContain('Aplikuj ponownie');

      applyToJob.mockReturnValue(of({ id: 3, status: ApplicationStatus.Submitted }));
      component.applyToJob('Znowu');

      expect(component.hasApplied).toBe(true);
      expect(component.applicationStatus).toBe(ApplicationStatus.Submitted);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Aplikacja została złożona ponownie!');
    });

    it('shows the draft banner and blocks applying for a draft offer', () => {
      create(of({ ...JOB, status: JobOfferStatus.Draft }));

      expect(component.acceptsApplications).toBe(false);
      expect(text()).toContain('Szkic – oferta nie jest widoczna publicznie');
      expect(text()).toContain('Aplikowanie zamknięte');
    });

    it('does not offer applying for an offer that expires in the past even when the status is Active', () => {
      create(of({ ...JOB, expiresAt: '2020-01-01T00:00:00Z', isExpired: true }));

      expect(component.acceptsApplications).toBe(false);
      expect(text()).toContain('Aktualna do');
      expect(text()).toContain('Ta oferta wygasła');
    });

    it('does not show "Aktualna do" for an open-ended offer', () => {
      create();

      expect(text()).not.toContain('Aktualna do');
    });

    it('shows the "Withdrawn" chip next to "Aplikuj ponownie" after withdrawal', () => {
      checkIfApplied.mockReturnValue(of({ hasApplied: false, applicationId: 3, status: ApplicationStatus.Withdrawn }));
      create();

      expect((fixture.nativeElement as HTMLElement).querySelector('.application-status app-status-chip')?.textContent?.trim())
        .toBe('Wycofana');
      expect(text()).toContain('Aplikuj ponownie');
      expect(text()).not.toContain('Już aplikowałeś');
    });

    it('does not offer applying again for a closed offer even after withdrawal', () => {
      checkIfApplied.mockReturnValue(of({ hasApplied: false, applicationId: 3, status: ApplicationStatus.Withdrawn }));
      create(of({ ...JOB, status: JobOfferStatus.Closed }));

      expect(text()).toContain('Aplikowanie zamknięte');
      expect(text()).not.toContain('Aplikuj ponownie');
    });

    it('shows no application status chip when the user has not applied', () => {
      create();

      expect((fixture.nativeElement as HTMLElement).querySelector('.application-status')).toBeNull();
      expect(component.applicationStatusPresentation).toBeNull();
    });

    it('lets a logged-out visitor log in to apply for an active offer', () => {
      authenticated = false;
      create();

      expect(text()).toContain('Zaloguj się, aby aplikować');
      expect(checkIfApplied).not.toHaveBeenCalled();
    });

    it('shows a logged-out visitor no login button for a closed offer', () => {
      authenticated = false;
      create(of({ ...JOB, status: JobOfferStatus.Closed }));

      expect(text()).not.toContain('Zaloguj się, aby aplikować');
      expect(text()).toContain('Rekrutacja na to stanowisko została zakończona');
    });

    it('shows an admin neither the apply button nor the application status', () => {
      admin = true;
      create();

      expect(text()).not.toContain('Aplikuj na to stanowisko');
      expect(text()).not.toContain('Aplikowanie zamknięte');
    });

    it('shows an employer no apply button and does not check the application status', () => {
      employer = true;
      create();

      expect(text()).not.toContain('Aplikuj na to stanowisko');
      expect(text()).not.toContain('Zaloguj się, aby aplikować');
      expect(checkIfApplied).not.toHaveBeenCalled();
    });

    it('links the company name to the company profile', () => {
      create();

      const link = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>('a.company-link');
      expect(link?.textContent?.trim()).toBe('Acme');
      expect(link?.getAttribute('href')).toBe('/companies/1');
    });

    it('reloads the offer when the API reports that it no longer accepts applications', () => {
      create();
      getJob.mockReturnValue(of({ ...JOB, status: JobOfferStatus.Closed }));
      applyToJob.mockReturnValue(throwError(() => httpError(400, { message: 'This job offer is not accepting applications' })));

      component.applyToJob('');

      expect(getJob).toHaveBeenCalledTimes(2);
      expect(component.acceptsApplications).toBe(false);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Ta oferta nie przyjmuje już aplikacji');
    });
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

  describe('applying with a CV', () => {
    const CV = new File(['%PDF-'], 'cv.pdf', { type: 'application/pdf' });

    function text(): string {
      fixture.detectChanges();
      return (fixture.nativeElement as HTMLElement).textContent ?? '';
    }

    it('submits the application first and then uploads the CV to the returned application', () => {
      create();
      applyToJob.mockReturnValue(of({ id: 42, status: ApplicationStatus.Submitted }));
      uploadCv.mockReturnValue(of<CvUploadEvent>({ type: 'done', application: { id: 42 } as never }));

      component.applyToJob('Notatka', CV);

      expect(applyToJob).toHaveBeenCalledWith({ jobOfferId: 5, note: 'Notatka' });
      expect(uploadCv).toHaveBeenCalledWith(42, CV);
      expect(applyToJob.mock.invocationCallOrder[0]).toBeLessThan(uploadCv.mock.invocationCallOrder[0]);
      expect(component.applying).toBe(false);
      expect(component.cvUploadProgress).toBeNull();
      expect(snackBarOpen).toHaveBeenCalledTimes(1);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Aplikacja wysłana pomyślnie! CV zostało dołączone.');
    });

    it('does not upload anything without a CV', () => {
      create();
      applyToJob.mockReturnValue(of({ id: 42 }));

      component.applyToJob('', null);

      expect(uploadCv).not.toHaveBeenCalled();
      expect(snackBarOpen.mock.calls[0][0]).toBe('Aplikacja wysłana pomyślnie!');
    });

    it('shows the upload progress while the CV is being sent', () => {
      create();
      const events = new Subject<CvUploadEvent>();
      applyToJob.mockReturnValue(of({ id: 42, status: ApplicationStatus.Submitted }));
      uploadCv.mockReturnValue(events);

      component.applyToJob('', CV);
      expect(component.applying).toBe(true);
      expect(component.hasApplied).toBe(true);
      expect(text()).toContain('Wysyłanie CV… 0%');

      events.next({ type: 'progress', percent: 60 });
      expect(component.cvUploadProgress).toBe(60);
      expect(text()).toContain('Wysyłanie CV… 60%');

      events.next({ type: 'progress', percent: 100 });
      expect(text()).toContain('Przetwarzanie CV…');
      expect(snackBarOpen).not.toHaveBeenCalled();

      events.next({ type: 'done', application: { id: 42 } as never });
      events.complete();
      expect(text()).toContain('Już aplikowałeś');
      expect(text()).not.toContain('Wysyłanie CV');
    });

    it('cancels a pending CV upload when the page is destroyed', () => {
      create();
      const events = new Subject<CvUploadEvent>();
      applyToJob.mockReturnValue(of({ id: 42, status: ApplicationStatus.Submitted }));
      uploadCv.mockReturnValue(events);

      component.applyToJob('', CV);
      expect(events.observed).toBe(true);

      fixture.destroy();

      expect(events.observed).toBe(false);
    });

    it('keeps the submitted application and explains how to add the CV later when the upload fails', () => {
      create();
      applyToJob.mockReturnValue(of({ id: 42, status: ApplicationStatus.Submitted }));
      uploadCv.mockReturnValue(throwError(() => httpError(400, { message: 'Unsupported file type - upload a PDF or DOCX file' })));

      component.applyToJob('', CV);

      expect(component.hasApplied).toBe(true);
      expect(component.applicationStatus).toBe(ApplicationStatus.Submitted);
      expect(component.applying).toBe(false);
      expect(component.cvUploadProgress).toBeNull();
      expect(snackBarOpen).toHaveBeenCalledTimes(1);
      expect(snackBarOpen.mock.calls[0][0]).toBe(CV_UPLOAD_FAILED_MESSAGE);
      expect(CV_UPLOAD_FAILED_MESSAGE)
        .toBe('Aplikacja złożona, ale nie udało się dołączyć CV – możesz dodać je w Moich aplikacjach');
      expect(text()).toContain('Już aplikowałeś');
    });

    it('reports the partial success also when the API does not return the application id', () => {
      create();
      applyToJob.mockReturnValue(of(null));

      component.applyToJob('', CV);

      expect(uploadCv).not.toHaveBeenCalled();
      expect(component.hasApplied).toBe(true);
      expect(snackBarOpen.mock.calls[0][0]).toBe(CV_UPLOAD_FAILED_MESSAGE);
    });

    it('does not upload the CV when the application itself is rejected', () => {
      create();
      applyToJob.mockReturnValue(throwError(() => httpError(400, { message: 'You have already applied to this job' })));

      component.applyToJob('', CV);

      expect(uploadCv).not.toHaveBeenCalled();
      expect(snackBarOpen.mock.calls[0][0]).toBe('Już aplikowałeś na tę ofertę');
    });

    it('mentions the reactivation and the CV after applying again', () => {
      checkIfApplied.mockReturnValue(of({ hasApplied: false, applicationId: 3, status: ApplicationStatus.Withdrawn }));
      create();
      applyToJob.mockReturnValue(of({ id: 3, status: ApplicationStatus.Submitted }));
      uploadCv.mockReturnValue(of<CvUploadEvent>({ type: 'done', application: { id: 3 } as never }));

      component.applyToJob('', CV);

      expect(uploadCv).toHaveBeenCalledWith(3, CV);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Aplikacja została złożona ponownie! CV zostało dołączone.');
    });

    it('passes the note and the CV chosen in the dialog to applyToJob', () => {
      create();
      const dialog = (component as unknown as { dialog: MatDialog }).dialog;
      vi.spyOn(dialog, 'open').mockReturnValue({
        afterClosed: () => of({ confirmed: true, note: 'Cześć', cvFile: CV })
      } as never);
      const apply = vi.spyOn(component, 'applyToJob').mockImplementation(() => undefined);

      component.openApplyDialog();

      expect(apply).toHaveBeenCalledWith('Cześć', CV);
    });

    it('does not apply when the dialog is cancelled', () => {
      create();
      const dialog = (component as unknown as { dialog: MatDialog }).dialog;
      vi.spyOn(dialog, 'open').mockReturnValue({ afterClosed: () => of(undefined) } as never);
      const apply = vi.spyOn(component, 'applyToJob');

      component.openApplyDialog();

      expect(apply).not.toHaveBeenCalled();
    });
  });
});

describe('ApplyDialogComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ApplyDialogComponent],
      providers: [{ provide: MatDialogRef, useValue: { close: vi.fn() } }]
    }).compileComponents();
  });

  it('offers an optional CV picker next to the note', () => {
    const fixture = TestBed.createComponent(ApplyDialogComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('textarea')).not.toBeNull();
    expect(root.querySelector('app-cv-upload input[type="file"]')).not.toBeNull();
    expect(root.textContent).toContain('Dołącz CV (opcjonalnie)');
  });

  it('returns the note and the chosen CV (or null) as the dialog result', () => {
    const fixture = TestBed.createComponent(ApplyDialogComponent);
    const dialog = fixture.componentInstance;

    expect(dialog.result()).toEqual({ confirmed: true, note: '', cvFile: null });

    const file = new File(['x'], 'cv.docx');
    dialog.note = 'Hej';
    dialog.cvFile = file;
    expect(dialog.result()).toEqual({ confirmed: true, note: 'Hej', cvFile: file });
  });
});
