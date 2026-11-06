import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { Observable, Subject, of, throwError } from 'rxjs';
import { APPLY_DIALOG_CONFIG, ApplyDialogComponent, ApplyDialogData, ATTACHMENTS_UPLOAD_FAILED_MESSAGE, JobDetailComponent } from './job-detail.component';
import { JobService } from '../../services/job.service';
import { JobApplicationService } from '../../services/job-application.service';
import { AuthService } from '../../services/auth.service';
import { JobOffer, JobOfferStatus, WorkType } from '../../models/job-offer.model';
import { ApplicationAttachment, ApplicationStatus, AttachmentKind, AttachmentUploadEvent, AttachmentUploadItem } from '../../models/job-application.model';

const JOB: JobOffer = {
  id: 5, title: 'Angular Developer', company: 'Acme', companyId: 1, description: 'Opis', salaryMin: 1000, salaryMax: 2000,
  location: 'Rzeszów', workType: WorkType.Remote, createdAt: new Date('2026-01-01'),
  status: JobOfferStatus.Active, expiresAt: null, isExpired: false
};

const SAVED_CV: ApplicationAttachment = {
  id: 11, kind: AttachmentKind.Cv, fileName: 'cv.pdf', size: 4 * 1024 * 1024, uploadedAt: '2026-02-01T10:00:00Z'
};

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('JobDetailComponent', () => {
  let fixture: ComponentFixture<JobDetailComponent>;
  let component: JobDetailComponent;
  let getJob: ReturnType<typeof vi.fn>;
  let checkIfApplied: ReturnType<typeof vi.fn>;
  let applyToJob: ReturnType<typeof vi.fn>;
  let uploadAttachments: ReturnType<typeof vi.fn>;
  let getApplication: ReturnType<typeof vi.fn>;
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
    uploadAttachments = vi.fn();
    getApplication = vi.fn().mockReturnValue(of({ id: 3, attachments: [] }));
    snackBarOpen = vi.fn();
    alertSpy = vi.spyOn(window, 'alert').mockImplementation(() => undefined);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [JobDetailComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id: '5' })) } },
        { provide: JobService, useValue: { getJob } },
        { provide: JobApplicationService, useValue: { checkIfApplied, applyToJob, uploadAttachments, getApplication } },
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

  describe('applying with attachments', () => {
    const CV: AttachmentUploadItem = { file: new File(['%PDF-'], 'cv.pdf', { type: 'application/pdf' }), kind: AttachmentKind.Cv };
    const LETTER: AttachmentUploadItem = { file: new File(['PK'], 'list.docx'), kind: AttachmentKind.CoverLetter };
    const ITEMS = [CV, LETTER];

    function text(): string {
      fixture.detectChanges();
      return (fixture.nativeElement as HTMLElement).textContent ?? '';
    }

    function liveStatus(): string {
      fixture.detectChanges();
      return (fixture.nativeElement as HTMLElement).querySelector('[role="status"].sr-only')?.textContent ?? '';
    }

    it('submits the application first and then uploads all attachments in one request to the returned application', () => {
      create();
      applyToJob.mockReturnValue(of({ id: 42, status: ApplicationStatus.Submitted }));
      uploadAttachments.mockReturnValue(of<AttachmentUploadEvent>({ type: 'done', application: { id: 42 } as never }));

      component.applyToJob('Notatka', ITEMS);

      expect(applyToJob).toHaveBeenCalledWith({ jobOfferId: 5, note: 'Notatka' });
      expect(uploadAttachments).toHaveBeenCalledTimes(1);
      expect(uploadAttachments).toHaveBeenCalledWith(42, ITEMS);
      expect(applyToJob.mock.invocationCallOrder[0]).toBeLessThan(uploadAttachments.mock.invocationCallOrder[0]);
      expect(component.applying).toBe(false);
      expect(component.attachmentsUploadProgress).toBeNull();
      expect(snackBarOpen).toHaveBeenCalledTimes(1);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Aplikacja wysłana pomyślnie! Załączniki zostały dołączone.');
      expect(liveStatus()).toBe('Załączniki przesłane');
    });

    it('does not upload anything without attachments', () => {
      create();
      applyToJob.mockReturnValue(of({ id: 42 }));

      component.applyToJob('', []);

      expect(uploadAttachments).not.toHaveBeenCalled();
      expect(snackBarOpen.mock.calls[0][0]).toBe('Aplikacja wysłana pomyślnie!');
    });

    it('shows the upload progress on the button and announces the phase once', () => {
      create();
      const events = new Subject<AttachmentUploadEvent>();
      applyToJob.mockReturnValue(of({ id: 42, status: ApplicationStatus.Submitted }));
      uploadAttachments.mockReturnValue(events);

      component.applyToJob('', ITEMS);
      expect(component.applying).toBe(true);
      expect(component.hasApplied).toBe(true);
      expect(text()).toContain('Wysyłanie załączników… 0%');
      expect(liveStatus()).toBe('Wysyłanie załączników…');

      events.next({ type: 'progress', percent: 60 });
      expect(component.attachmentsUploadProgress).toBe(60);
      expect(text()).toContain('Wysyłanie załączników… 60%');
      expect(liveStatus()).toBe('Wysyłanie załączników…');

      events.next({ type: 'progress', percent: 100 });
      expect(text()).toContain('Przetwarzanie załączników…');
      expect(snackBarOpen).not.toHaveBeenCalled();

      events.next({ type: 'done', application: { id: 42 } as never });
      events.complete();
      expect(text()).toContain('Już aplikowałeś');
      expect(text()).not.toContain('Wysyłanie załączników');
    });

    it('has a single, visually hidden status region on the page', () => {
      create();

      const regions = (fixture.nativeElement as HTMLElement).querySelectorAll('.sr-only[role="status"]');
      expect(regions.length).toBe(1);
    });

    it('cancels a pending upload and explains how to add the attachments later when the page is left', () => {
      create();
      const events = new Subject<AttachmentUploadEvent>();
      applyToJob.mockReturnValue(of({ id: 42, status: ApplicationStatus.Submitted }));
      uploadAttachments.mockReturnValue(events);

      component.applyToJob('', ITEMS);
      expect(events.observed).toBe(true);

      fixture.destroy();

      expect(events.observed).toBe(false);
      expect(snackBarOpen).toHaveBeenCalledTimes(1);
      expect(snackBarOpen.mock.calls[0][0]).toBe(ATTACHMENTS_UPLOAD_FAILED_MESSAGE);
      expect(snackBarOpen.mock.calls[0][2]).toEqual({ duration: 8000 });
    });

    it('does not show the "left the page" message when the page is left after the upload finished', () => {
      create();
      applyToJob.mockReturnValue(of({ id: 42, status: ApplicationStatus.Submitted }));
      uploadAttachments.mockReturnValue(of<AttachmentUploadEvent>({ type: 'done', application: { id: 42 } as never }));
      component.applyToJob('', ITEMS);
      snackBarOpen.mockClear();

      fixture.destroy();

      expect(snackBarOpen).not.toHaveBeenCalled();
    });

    it('does not start the upload but informs the user when the page was left before the application was submitted', () => {
      create();
      const submitted = new Subject<{ id: number; status: ApplicationStatus }>();
      applyToJob.mockReturnValue(submitted);

      component.applyToJob('', ITEMS);
      fixture.destroy();
      submitted.next({ id: 42, status: ApplicationStatus.Submitted });
      submitted.complete();

      expect(uploadAttachments).not.toHaveBeenCalled();
      expect(snackBarOpen).toHaveBeenCalledTimes(1);
      expect(snackBarOpen.mock.calls[0][0]).toBe(ATTACHMENTS_UPLOAD_FAILED_MESSAGE);
    });

    it('keeps the submitted application and explains how to add the attachments later when the upload fails', () => {
      create();
      applyToJob.mockReturnValue(of({ id: 42, status: ApplicationStatus.Submitted }));
      uploadAttachments.mockReturnValue(throwError(() => httpError(400, { message: 'Total size of attachments cannot exceed 25 MB' })));

      component.applyToJob('', ITEMS);

      expect(component.hasApplied).toBe(true);
      expect(component.applicationStatus).toBe(ApplicationStatus.Submitted);
      expect(component.applying).toBe(false);
      expect(component.attachmentsUploadProgress).toBeNull();
      expect(snackBarOpen).toHaveBeenCalledTimes(1);
      expect(snackBarOpen.mock.calls[0][0]).toBe(ATTACHMENTS_UPLOAD_FAILED_MESSAGE);
      expect(ATTACHMENTS_UPLOAD_FAILED_MESSAGE)
        .toBe('Aplikacja złożona, ale nie udało się dołączyć załączników – możesz dodać je w Moich aplikacjach');
      expect(liveStatus()).toBe('Przerwano wysyłanie');
      expect(text()).toContain('Już aplikowałeś');
    });

    it.each([401, 403, 429, 500])('reports the partial success also for a %i upload error', status => {
      create();
      applyToJob.mockReturnValue(of({ id: 42, status: ApplicationStatus.Submitted }));
      uploadAttachments.mockReturnValue(throwError(() => httpError(status)));

      component.applyToJob('', ITEMS);

      expect(snackBarOpen.mock.calls[0][0]).toBe(ATTACHMENTS_UPLOAD_FAILED_MESSAGE);
    });

    it('reports the partial success also when the API does not return the application id', () => {
      create();
      applyToJob.mockReturnValue(of(null));

      component.applyToJob('', ITEMS);

      expect(uploadAttachments).not.toHaveBeenCalled();
      expect(component.hasApplied).toBe(true);
      expect(snackBarOpen.mock.calls[0][0]).toBe(ATTACHMENTS_UPLOAD_FAILED_MESSAGE);
    });

    it('does not upload the attachments when the application itself is rejected', () => {
      create();
      applyToJob.mockReturnValue(throwError(() => httpError(400, { message: 'You have already applied to this job' })));

      component.applyToJob('', ITEMS);

      expect(uploadAttachments).not.toHaveBeenCalled();
      expect(snackBarOpen.mock.calls[0][0]).toBe('Już aplikowałeś na tę ofertę');
    });

    it('mentions the reactivation and the attachments after applying again', () => {
      checkIfApplied.mockReturnValue(of({ hasApplied: false, applicationId: 3, status: ApplicationStatus.Withdrawn }));
      create();
      applyToJob.mockReturnValue(of({ id: 3, status: ApplicationStatus.Submitted }));
      uploadAttachments.mockReturnValue(of<AttachmentUploadEvent>({ type: 'done', application: { id: 3 } as never }));

      component.applyToJob('', [CV]);

      expect(uploadAttachments).toHaveBeenCalledWith(3, [CV]);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Aplikacja została złożona ponownie! Załączniki zostały dołączone.');
    });

    it('opens the apply dialog as an ARIA modal and passes the note and the attachments to applyToJob', () => {
      create();
      const dialog = (component as unknown as { dialog: MatDialog }).dialog;
      const open = vi.spyOn(dialog, 'open').mockReturnValue({
        afterClosed: () => of({ confirmed: true, note: 'Cześć', attachments: ITEMS })
      } as never);
      const apply = vi.spyOn(component, 'applyToJob').mockImplementation(() => undefined);

      component.openApplyDialog();

      expect(open).toHaveBeenCalledWith(ApplyDialogComponent, { ...APPLY_DIALOG_CONFIG, data: { existing: [] } });
      expect(APPLY_DIALOG_CONFIG.ariaModal).toBe(true);
      expect(apply).toHaveBeenCalledWith('Cześć', ITEMS);
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

  describe('applying again with the attachments of the withdrawn application', () => {
    function openDialogSpy(): ReturnType<typeof vi.fn> {
      const dialog = (component as unknown as { dialog: MatDialog }).dialog;
      return vi.spyOn(dialog, 'open').mockReturnValue({ afterClosed: () => of(undefined) } as never) as never;
    }

    function applyButton(): HTMLButtonElement {
      return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'))
        .find(b => b.textContent?.includes('Aplikuj'))!;
    }

    it('loads the kept attachments and passes them to the dialog as existing', () => {
      checkIfApplied.mockReturnValue(of({ hasApplied: false, applicationId: 3, status: ApplicationStatus.Withdrawn }));
      getApplication.mockReturnValue(of({ id: 3, status: ApplicationStatus.Withdrawn, attachments: [SAVED_CV] }));
      create();
      const open = openDialogSpy();

      component.openApplyDialog();

      expect(getApplication).toHaveBeenCalledWith(3);
      const data = open.mock.calls[0][1].data as ApplyDialogData;
      expect(data.existing).toEqual([SAVED_CV]);
      expect(open.mock.calls[0][1]).toMatchObject(APPLY_DIALOG_CONFIG);
    });

    it('keeps "Aplikuj ponownie" disabled until the kept attachments are loaded', () => {
      const application = new Subject<unknown>();
      checkIfApplied.mockReturnValue(of({ hasApplied: false, applicationId: 3, status: ApplicationStatus.Withdrawn }));
      getApplication.mockReturnValue(application);
      create();

      expect(component.checkingApplication).toBe(true);
      expect(applyButton().disabled).toBe(true);

      application.next({ id: 3, attachments: [SAVED_CV] });
      application.complete();
      fixture.detectChanges();

      expect(component.checkingApplication).toBe(false);
      expect(applyButton().disabled).toBe(false);
      expect(applyButton().textContent).toContain('Aplikuj ponownie');
    });

    it('opens the dialog without existing attachments when they cannot be loaded (the server still checks the limits)', () => {
      checkIfApplied.mockReturnValue(of({ hasApplied: false, applicationId: 3, status: ApplicationStatus.Withdrawn }));
      getApplication.mockReturnValue(throwError(() => httpError(500)));
      create();
      const open = openDialogSpy();

      expect(component.checkingApplication).toBe(false);
      component.openApplyDialog();

      expect((open.mock.calls[0][1].data as ApplyDialogData).existing).toEqual([]);
    });

    it('does not load an application for a first-time applicant', () => {
      create();
      const open = openDialogSpy();

      component.openApplyDialog();

      expect(getApplication).not.toHaveBeenCalled();
      expect((open.mock.calls[0][1].data as ApplyDialogData).existing).toEqual([]);
    });

    it('cancels loading the kept attachments when the page is left', () => {
      const application = new Subject<unknown>();
      checkIfApplied.mockReturnValue(of({ hasApplied: false, applicationId: 3, status: ApplicationStatus.Withdrawn }));
      getApplication.mockReturnValue(application);
      create();

      fixture.destroy();

      expect(application.observed).toBe(false);
    });
  });
});

describe('ApplyDialogComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ApplyDialogComponent],
      providers: [{ provide: MatDialogRef, useValue: { close: vi.fn() } }, { provide: MAT_DIALOG_DATA, useValue: null }]
    }).compileComponents();
  });

  it('offers an optional attachments picker next to the note', () => {
    const fixture = TestBed.createComponent(ApplyDialogComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('textarea')).not.toBeNull();
    expect(root.querySelector('app-attachments-picker input[type="file"][multiple]')).not.toBeNull();
    expect(root.textContent).toContain('Dołącz CV, list motywacyjny lub inne pliki (opcjonalnie)');
  });

  it('returns the note and the chosen attachments (or none) as the dialog result', () => {
    const fixture = TestBed.createComponent(ApplyDialogComponent);
    const dialog = fixture.componentInstance;

    expect(dialog.result()).toEqual({ confirmed: true, note: '', attachments: [] });

    const items = [{ file: new File(['x'], 'cv.docx'), kind: AttachmentKind.Cv }];
    dialog.note = 'Hej';
    dialog.attachments = items;
    expect(dialog.result()).toEqual({ confirmed: true, note: 'Hej', attachments: items });
  });

  it('collects the files chosen in the picker with their kinds', () => {
    const fixture = TestBed.createComponent(ApplyDialogComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const input = root.querySelector<HTMLInputElement>('input[type="file"]')!;
    const cv = new File(['%PDF-'], 'cv.pdf');
    const portfolio = new File(['x'], 'portfolio.png');

    Object.defineProperty(input, 'files', { value: [cv, portfolio], configurable: true });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(fixture.componentInstance.result().attachments).toEqual([
      { file: cv, kind: AttachmentKind.Cv }, { file: portfolio, kind: AttachmentKind.Other }
    ]);
    expect(root.querySelectorAll('.file-name').length).toBe(2);
  });

  describe('with the attachments kept from the withdrawn application', () => {
    function createWith(existing: ApplicationAttachment[]) {
      TestBed.overrideProvider(MAT_DIALOG_DATA, { useValue: { existing } satisfies ApplyDialogData });
      const fixture = TestBed.createComponent(ApplyDialogComponent);
      fixture.detectChanges();
      return fixture;
    }

    function choose(fixture: ReturnType<typeof createWith>, files: File[]): void {
      const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('input[type="file"]')!;
      Object.defineProperty(input, 'files', { value: files, configurable: true });
      input.dispatchEvent(new Event('change'));
      fixture.detectChanges();
    }

    it('does not default new files to CV when the application already has one', () => {
      const fixture = createWith([SAVED_CV]);
      const letter = new File(['%PDF-'], 'list.pdf');
      const photo = new File(['x'], 'zdjecie.png');

      choose(fixture, [letter, photo]);

      expect(fixture.componentInstance.result().attachments).toEqual([
        { file: letter, kind: AttachmentKind.Other }, { file: photo, kind: AttachmentKind.Other }
      ]);
    });

    it('counts the kept attachments towards the 25 MB and 10 files limits', () => {
      const kept = Array.from({ length: 9 }, (_, i): ApplicationAttachment => ({ ...SAVED_CV, id: 20 + i, fileName: `plik${i}.pdf`, size: 1024 * 1024 }));
      const fixture = createWith(kept);
      const root = fixture.nativeElement as HTMLElement;

      expect(root.querySelector('.usage')?.textContent).toContain('plików: 9 z 10');

      const accepted = new File(['%PDF-'], 'a.pdf');
      choose(fixture, [accepted, new File(['%PDF-'], 'b.pdf')]);

      expect(fixture.componentInstance.result().attachments.map(item => item.file)).toEqual([accepted]);
      expect(root.querySelector('.error-line')?.textContent).toContain('b.pdf');
    });

    it('rejects a file that would exceed 25 MB together with the kept attachments', () => {
      const kept = Array.from({ length: 6 }, (_, i): ApplicationAttachment => ({ ...SAVED_CV, id: 30 + i, fileName: `plik${i}.pdf` }));
      const fixture = createWith(kept);
      const big = new File([new Uint8Array(2 * 1024 * 1024)], 'duzy.pdf');

      choose(fixture, [big]);

      expect(fixture.componentInstance.result().attachments).toEqual([]);
      expect((fixture.nativeElement as HTMLElement).querySelector('.error-line')?.textContent).toContain('duzy.pdf');
    });
  });
});
