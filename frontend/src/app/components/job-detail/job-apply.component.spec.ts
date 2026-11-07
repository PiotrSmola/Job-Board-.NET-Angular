import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Observable, Subject, of, throwError } from 'rxjs';
import { ATTACHMENTS_UPLOAD_FAILED_MESSAGE, JobApplyComponent } from './job-apply.component';
import { JobApplicationService } from '../../services/job-application.service';
import { AuthService } from '../../services/auth.service';
import { AccountService } from '../../services/account.service';
import { JobOffer, JobOfferStatus } from '../../models/job-offer.model';
import {
  ApplicationAttachment, ApplicationCheck, ApplicationStatus, AttachmentKind, AttachmentUploadEvent, AttachmentUploadItem
} from '../../models/job-application.model';
import { ProfileDocument } from '../../models/account.model';
import { makeJob } from '../../../testing/job-fixtures';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

const JOB = makeJob(5, { title: 'Angular Developer' });

const SAVED_CV: ApplicationAttachment = {
  id: 11, kind: AttachmentKind.Cv, fileName: 'cv.pdf', size: 4 * 1024 * 1024, uploadedAt: '2026-02-01T10:00:00Z'
};

const CV: AttachmentUploadItem = { file: new File(['%PDF-'], 'cv.pdf', { type: 'application/pdf' }), kind: AttachmentKind.Cv };
const LETTER: AttachmentUploadItem = { file: new File(['PK'], 'list.docx'), kind: AttachmentKind.CoverLetter };
const ITEMS = [CV, LETTER];

describe('JobApplyComponent', () => {
  let fixture: ComponentFixture<JobApplyComponent>;
  let component: JobApplyComponent;
  let checkIfApplied: ReturnType<typeof vi.fn>;
  let applyToJob: ReturnType<typeof vi.fn>;
  let uploadAttachments: ReturnType<typeof vi.fn>;
  let attachFromProfile: ReturnType<typeof vi.fn>;
  let getApplication: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;
  let profileDocuments: ProfileDocument[];
  let authenticated: boolean;
  let admin: boolean;
  let employer: boolean;
  let stale: number;
  let appliedEvents: boolean[];

  const root = () => fixture.nativeElement as HTMLElement;

  function create(job: JobOffer = JOB): void {
    fixture = TestBed.createComponent(JobApplyComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('job', job);
    component.jobStale.subscribe(() => stale++);
    component.appliedChange.subscribe(value => appliedEvents.push(value));
    fixture.detectChanges();
  }

  function text(): string {
    fixture.detectChanges();
    return root().textContent ?? '';
  }

  function liveStatus(): string {
    fixture.detectChanges();
    return root().querySelector('.upload-status[role="status"]')?.textContent ?? '';
  }

  function submitButton(): HTMLButtonElement | null {
    fixture.detectChanges();
    return root().querySelector<HTMLButtonElement>('button.submit');
  }

  function httpError(status: number, error: unknown = null): HttpErrorResponse {
    return new HttpErrorResponse({ status, statusText: 'Error', url: '/api/x', error });
  }

  function withdrawn(): Observable<ApplicationCheck> {
    return of({ hasApplied: false, applicationId: 3, status: ApplicationStatus.Withdrawn });
  }

  beforeEach(async () => {
    authenticated = true;
    admin = false;
    employer = false;
    stale = 0;
    appliedEvents = [];
    profileDocuments = [];
    checkIfApplied = vi.fn().mockReturnValue(of({ hasApplied: false }));
    applyToJob = vi.fn();
    uploadAttachments = vi.fn();
    attachFromProfile = vi.fn();
    getApplication = vi.fn().mockReturnValue(of({ id: 3, attachments: [] }));
    snackBarOpen = vi.fn();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [JobApplyComponent],
      providers: [
        provideRouter([]),
        { provide: JobApplicationService, useValue: { checkIfApplied, applyToJob, uploadAttachments, attachFromProfile, getApplication } },
        { provide: AuthService, useValue: { isAuthenticated: () => authenticated, isAdmin: () => admin, isEmployer: () => employer } },
        { provide: AccountService, useValue: { getDocuments: () => of(profileDocuments) } },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } }
      ]
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  describe('states', () => {
    it('shows the inline form for a candidate: profile documents, new files, message and send', () => {
      create();

      expect(checkIfApplied).toHaveBeenCalledWith(5);
      const section = root().querySelector('section#apply')!;
      expect(section.getAttribute('aria-labelledby')).toBe('apply-title');
      expect(root().querySelector('h2#apply-title')?.textContent).toBe('Twoja aplikacja');
      expect(root().querySelector('app-profile-documents-picker')).not.toBeNull();
      expect(root().querySelector('app-attachments-picker input[type="file"][multiple]')).not.toBeNull();
      expect(root().querySelector('textarea')?.maxLength).toBe(1000);
      expect(text()).toContain('Wiadomość do rekrutera (opcjonalnie)');
      expect(submitButton()?.textContent?.trim()).toBe('Wyślij aplikację');
      expect(root().querySelector('mat-dialog-content')).toBeNull();
    });

    it('asks a logged-out visitor to log in with a return URL and does not check the status', () => {
      authenticated = false;
      create();

      const login = root().querySelector<HTMLAnchorElement>('a[href^="/login"]')!;
      expect(login.textContent?.trim()).toBe('Zaloguj się, aby aplikować');
      expect(login.getAttribute('href')).toBe('/login?returnUrl=%2Fjobs%2F5');
      expect(root().querySelector('a[href="/register"]')).not.toBeNull();
      expect(checkIfApplied).not.toHaveBeenCalled();
    });

    it('renders nothing for a logged-out visitor when the offer is closed', () => {
      authenticated = false;
      create({ ...JOB, status: JobOfferStatus.Closed });

      expect(root().querySelector('section')).toBeNull();
    });

    it.each([['admin', () => admin = true], ['employer', () => employer = true]])(
      'tells an %s that staff accounts do not apply and does not check the status', (_role, setRole) => {
        setRole();
        create();

        expect(text()).toContain('Konta pracodawcy i administratora nie aplikują na oferty');
        expect(root().querySelector('form')).toBeNull();
        expect(checkIfApplied).not.toHaveBeenCalled();
      });

    it('reserves space while the application status is checked', () => {
      const check = new Subject<ApplicationCheck>();
      checkIfApplied.mockReturnValue(check);
      create();

      expect(root().querySelector('.placeholder')?.getAttribute('aria-busy')).toBe('true');
      expect(root().querySelector('form')).toBeNull();

      check.next({ hasApplied: false, applicationId: null, status: null });
      check.complete();

      expect(submitButton()).not.toBeNull();
    });

    it('shows the current status of an existing application with a link to "Moje aplikacje"', () => {
      checkIfApplied.mockReturnValue(of({ hasApplied: true, applicationId: 3, status: ApplicationStatus.Interview }));
      create();

      expect(text()).toContain('Rozmowa');
      expect(text()).toContain('Już aplikowałeś');
      expect(root().querySelector('a[href="/my-applications"]')?.textContent).toContain('Moich aplikacjach');
      expect(root().querySelector('form')).toBeNull();
      expect(appliedEvents).toEqual([true]);
    });

    it('stays silent when checking the application status fails with 401 and shows the form', () => {
      checkIfApplied.mockReturnValue(throwError(() => httpError(401)));
      create();

      expect(component.hasApplied()).toBe(false);
      expect(component.checking()).toBe(false);
      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(submitButton()).not.toBeNull();
    });

    it('renders nothing for a candidate who has not applied to a closed offer', () => {
      create({ ...JOB, status: JobOfferStatus.Active, isExpired: true });

      expect(root().querySelector('section')).toBeNull();
    });

    it('shows the "Wycofana" status (no form) for a closed offer after withdrawal', () => {
      checkIfApplied.mockReturnValue(withdrawn());
      create({ ...JOB, status: JobOfferStatus.Closed });

      expect(text()).toContain('Wycofana');
      expect(root().querySelector('form')).toBeNull();
      expect(text()).not.toContain('Aplikuj ponownie');
    });
  });

  describe('submitting the form', () => {
    it('sends the note, the new files and the chosen profile documents', () => {
      create();
      const apply = vi.spyOn(component, 'applyToJob').mockImplementation(() => undefined);
      const textarea = root().querySelector('textarea')!;
      textarea.value = 'Cześć';
      textarea.dispatchEvent(new Event('input'));
      component.attachments.set(ITEMS);
      component.profileDocuments.set([{ id: 7, kind: AttachmentKind.Cv, fileName: 'cv.pdf', size: 1, uploadedAt: '' }]);
      fixture.detectChanges();

      submitButton()!.click();

      expect(apply).toHaveBeenCalledWith('Cześć', ITEMS, [7]);
    });

    it('sends the application, shows a polite success snackbar, the status and moves focus to the section heading', async () => {
      create();
      document.body.appendChild(root());
      applyToJob.mockReturnValue(of({ id: 42, status: ApplicationStatus.Submitted }));

      component.applyToJob('Notatka');
      await Promise.resolve();

      expect(applyToJob).toHaveBeenCalledWith({ jobOfferId: 5, note: 'Notatka' });
      expect(component.hasApplied()).toBe(true);
      expect(component.submitting()).toBe(false);
      expect(snackBarOpen).toHaveBeenCalledWith('Aplikacja wysłana pomyślnie!', 'OK', { duration: 5000, politeness: 'polite' });
      expect(text()).toContain('Złożona');
      expect(document.activeElement).toBe(root().querySelector('#apply-title'));
      expect(appliedEvents).toContain(true);
      root().remove();
    });

    it('sends null instead of an empty or blank note', () => {
      create();
      applyToJob.mockReturnValue(of({}));

      component.applyToJob('   ');

      expect(applyToJob).toHaveBeenCalledWith({ jobOfferId: 5, note: null });
    });

    it('translates "already applied", marks the job as applied and shows the status', () => {
      create();
      applyToJob.mockReturnValue(throwError(() => httpError(400, { message: 'You have already applied to this job' })));

      component.applyToJob('');

      expect(component.submitting()).toBe(false);
      expect(component.hasApplied()).toBe(true);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Już aplikowałeś na tę ofertę');
      expect(text()).toContain('Już aplikowałeś');
    });

    it('shows other errors inline in an alert and keeps the form', () => {
      create();
      applyToJob.mockReturnValue(throwError(() => httpError(500)));

      component.applyToJob('');

      expect(component.hasApplied()).toBe(false);
      const alert = root().querySelector('.error[role="alert"]');
      fixture.detectChanges();
      expect(alert?.textContent).toBe('Błąd podczas aplikowania');
      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(submitButton()?.disabled).toBe(false);
    });

    it.each([401, 403])('does not duplicate the interceptor notice for %i', status => {
      create();
      applyToJob.mockReturnValue(throwError(() => httpError(status)));

      component.applyToJob('');

      expect(component.submitting()).toBe(false);
      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(component.error()).toBeNull();
    });

    it('asks the parent to reload the offer when the API reports that it no longer accepts applications', () => {
      create();
      applyToJob.mockReturnValue(throwError(() => httpError(400, { message: 'This job offer is not accepting applications' })));

      component.applyToJob('');

      expect(stale).toBe(1);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Ta oferta nie przyjmuje już aplikacji');
    });

    it('locks the form while sending', () => {
      create();
      const submitted = new Subject<unknown>();
      applyToJob.mockReturnValue(submitted);

      component.applyToJob('');

      expect(submitButton()?.disabled).toBe(true);
      expect(submitButton()?.textContent).toContain('Wysyłanie aplikacji…');
      expect(root().querySelector('textarea')?.disabled).toBe(true);
      expect(root().querySelector('form')?.getAttribute('aria-busy')).toBe('true');
      submitted.complete();
    });
  });

  describe('attachments', () => {
    it('submits the application first and then uploads all attachments in one request to the returned application', () => {
      create();
      applyToJob.mockReturnValue(of({ id: 42, status: ApplicationStatus.Submitted }));
      uploadAttachments.mockReturnValue(of<AttachmentUploadEvent>({ type: 'done', application: { id: 42 } as never }));

      component.applyToJob('Notatka', ITEMS);

      expect(uploadAttachments).toHaveBeenCalledTimes(1);
      expect(uploadAttachments).toHaveBeenCalledWith(42, ITEMS);
      expect(applyToJob.mock.invocationCallOrder[0]).toBeLessThan(uploadAttachments.mock.invocationCallOrder[0]);
      expect(component.submitting()).toBe(false);
      expect(component.uploadProgress()).toBeNull();
      expect(snackBarOpen).toHaveBeenCalledTimes(1);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Aplikacja wysłana pomyślnie! Załączniki zostały dołączone.');
      expect(liveStatus()).toBe('Załączniki przesłane');
    });

    it('shows the upload progress on the button and announces the phase once', () => {
      create();
      const events = new Subject<AttachmentUploadEvent>();
      applyToJob.mockReturnValue(of({ id: 42, status: ApplicationStatus.Submitted }));
      uploadAttachments.mockReturnValue(events);

      component.applyToJob('', ITEMS);
      expect(component.submitting()).toBe(true);
      expect(component.hasApplied()).toBe(true);
      expect(submitButton()?.textContent).toContain('Wysyłanie załączników… 0%');
      expect(liveStatus()).toBe('Wysyłanie załączników…');

      events.next({ type: 'progress', percent: 60 });
      expect(submitButton()?.textContent).toContain('Wysyłanie załączników… 60%');
      expect(liveStatus()).toBe('Wysyłanie załączników…');

      events.next({ type: 'progress', percent: 100 });
      expect(submitButton()?.textContent).toContain('Przetwarzanie załączników…');
      expect(snackBarOpen).not.toHaveBeenCalled();

      events.next({ type: 'done', application: { id: 42 } as never });
      events.complete();
      expect(text()).toContain('Już aplikowałeś');
      expect(text()).not.toContain('Wysyłanie załączników');
    });

    it('has a single, visually hidden status region', () => {
      create();

      expect(root().querySelectorAll('.visually-hidden[role="status"]').length).toBe(1);
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
      expect(snackBarOpen.mock.calls[0][2]).toEqual({ duration: 8000, politeness: 'polite' });
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

      expect(component.hasApplied()).toBe(true);
      expect(component.applicationStatus()).toBe(ApplicationStatus.Submitted);
      expect(component.submitting()).toBe(false);
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
      expect(component.hasApplied()).toBe(true);
      expect(snackBarOpen.mock.calls[0][0]).toBe(ATTACHMENTS_UPLOAD_FAILED_MESSAGE);
    });

    it('does not upload the attachments when the application itself is rejected', () => {
      create();
      applyToJob.mockReturnValue(throwError(() => httpError(400, { message: 'You have already applied to this job' })));

      component.applyToJob('', ITEMS);

      expect(uploadAttachments).not.toHaveBeenCalled();
    });

    describe('documents from the profile', () => {
      it('copies the profile documents first and then uploads the new files', () => {
        create();
        applyToJob.mockReturnValue(of({ id: 42, status: ApplicationStatus.Submitted }));
        attachFromProfile.mockReturnValue(of({ id: 42 }));
        uploadAttachments.mockReturnValue(of<AttachmentUploadEvent>({ type: 'done', application: { id: 42 } as never }));

        component.applyToJob('', ITEMS, [7, 8]);

        expect(attachFromProfile).toHaveBeenCalledWith(42, [7, 8]);
        expect(uploadAttachments).toHaveBeenCalledWith(42, ITEMS);
        expect(attachFromProfile.mock.invocationCallOrder[0]).toBeLessThan(uploadAttachments.mock.invocationCallOrder[0]);
        expect(snackBarOpen.mock.calls[0][0]).toBe('Aplikacja wysłana pomyślnie! Załączniki zostały dołączone.');
        expect(liveStatus()).toBe('Załączniki przesłane');
      });

      it('only copies the profile documents when no new files were chosen', () => {
        create();
        applyToJob.mockReturnValue(of({ id: 42, status: ApplicationStatus.Submitted }));
        attachFromProfile.mockReturnValue(of({ id: 42 }));

        component.applyToJob('', [], [7]);

        expect(uploadAttachments).not.toHaveBeenCalled();
        expect(component.submitting()).toBe(false);
        expect(snackBarOpen.mock.calls[0][0]).toBe('Aplikacja wysłana pomyślnie! Załączniki zostały dołączone.');
      });

      it('shows the processing state and announces the phase while the documents are copied', () => {
        create();
        applyToJob.mockReturnValue(of({ id: 42, status: ApplicationStatus.Submitted }));
        attachFromProfile.mockReturnValue(new Subject());

        component.applyToJob('', [], [7]);

        expect(submitButton()?.textContent).toContain('Przetwarzanie załączników…');
        expect(liveStatus()).toBe('Dołączanie dokumentów z profilu…');
      });

      it('keeps the application, skips the upload and explains what to do when copying fails', () => {
        create();
        applyToJob.mockReturnValue(of({ id: 42, status: ApplicationStatus.Submitted }));
        attachFromProfile.mockReturnValue(throwError(() => httpError(400, { message: 'An application can have at most 10 attachments' })));

        component.applyToJob('', ITEMS, [7]);

        expect(uploadAttachments).not.toHaveBeenCalled();
        expect(component.hasApplied()).toBe(true);
        expect(snackBarOpen.mock.calls[0][0]).toBe(ATTACHMENTS_UPLOAD_FAILED_MESSAGE);
        expect(liveStatus()).toBe('Przerwano wysyłanie');
      });

      it('cancels copying when the page is left', () => {
        create();
        const copied = new Subject<unknown>();
        applyToJob.mockReturnValue(of({ id: 42, status: ApplicationStatus.Submitted }));
        attachFromProfile.mockReturnValue(copied);

        component.applyToJob('', [], [7]);
        fixture.destroy();

        expect(copied.observed).toBe(false);
        expect(snackBarOpen.mock.calls[0][0]).toBe(ATTACHMENTS_UPLOAD_FAILED_MESSAGE);
      });

      it('offers the profile documents and counts them into the limits of the file picker', () => {
        profileDocuments = [
          { id: 7, kind: AttachmentKind.Cv, fileName: 'cv-profil.pdf', size: 2 * 1024 * 1024, uploadedAt: '2026-01-01T10:00:00Z' }
        ];
        create();

        root().querySelector<HTMLInputElement>('app-profile-documents-picker input[type="checkbox"]')!.click();
        fixture.detectChanges();

        expect(component.profileDocuments().map(d => d.id)).toEqual([7]);
        const usage = root().querySelector('app-attachments-picker .usage')?.textContent;
        expect(usage).toContain('plików: 1 z 10');
        expect(usage).toContain('Wykorzystano 2 MB z 25 MB');
      });
    });
  });

  describe('applying again after withdrawal', () => {
    it('offers "Aplikuj ponownie" with the "Wycofana" status and reports the reactivation', () => {
      checkIfApplied.mockReturnValue(withdrawn());
      create();

      expect(component.hasWithdrawn()).toBe(true);
      expect(submitButton()?.textContent?.trim()).toBe('Aplikuj ponownie');
      expect(root().querySelector('.withdrawn app-status-chip')?.textContent?.trim()).toBe('Wycofana');

      applyToJob.mockReturnValue(of({ id: 3, status: ApplicationStatus.Submitted }));
      component.applyToJob('Znowu');

      expect(component.hasApplied()).toBe(true);
      expect(component.applicationStatus()).toBe(ApplicationStatus.Submitted);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Aplikacja została złożona ponownie!');
    });

    it('mentions the reactivation and the attachments after applying again', () => {
      checkIfApplied.mockReturnValue(withdrawn());
      create();
      applyToJob.mockReturnValue(of({ id: 3, status: ApplicationStatus.Submitted }));
      uploadAttachments.mockReturnValue(of<AttachmentUploadEvent>({ type: 'done', application: { id: 3 } as never }));

      component.applyToJob('', [CV]);

      expect(uploadAttachments).toHaveBeenCalledWith(3, [CV]);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Aplikacja została złożona ponownie! Załączniki zostały dołączone.');
    });

    it('waits for the kept attachments and counts them into the limits', () => {
      const application = new Subject<unknown>();
      checkIfApplied.mockReturnValue(withdrawn());
      getApplication.mockReturnValue(application);
      create();

      expect(component.checking()).toBe(true);
      expect(submitButton()).toBeNull();

      application.next({ id: 3, attachments: [SAVED_CV] });
      application.complete();

      expect(getApplication).toHaveBeenCalledWith(3);
      expect(component.checking()).toBe(false);
      expect(component.existingAttachments()).toEqual([SAVED_CV]);
      fixture.detectChanges();
      expect(root().querySelector('app-attachments-picker .usage')?.textContent).toContain('plików: 1 z 10');
    });

    it('shows the form without kept attachments when they cannot be loaded (the server still checks the limits)', () => {
      checkIfApplied.mockReturnValue(withdrawn());
      getApplication.mockReturnValue(throwError(() => httpError(500)));
      create();

      expect(component.checking()).toBe(false);
      expect(component.existingAttachments()).toEqual([]);
      expect(submitButton()).not.toBeNull();
    });

    it('does not load an application for a first-time applicant', () => {
      create();

      expect(getApplication).not.toHaveBeenCalled();
    });

    it('cancels loading the kept attachments when the page is left', () => {
      const application = new Subject<unknown>();
      checkIfApplied.mockReturnValue(withdrawn());
      getApplication.mockReturnValue(application);
      create();

      fixture.destroy();

      expect(application.observed).toBe(false);
    });
  });

  describe('focusSection', () => {
    it('moves focus to the section heading', () => {
      create();
      document.body.appendChild(root());

      component.focusSection();

      expect(document.activeElement).toBe(root().querySelector('#apply-title'));
      root().remove();
    });
  });
});
