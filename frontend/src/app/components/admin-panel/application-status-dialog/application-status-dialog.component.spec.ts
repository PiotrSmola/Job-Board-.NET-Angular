import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Observable, Subject, of, throwError } from 'rxjs';
import { ApplicationStatusDialogComponent, COMMENT_MAX_LENGTH } from './application-status-dialog.component';
import { JobApplicationService } from '../../../services/job-application.service';
import { ApplicationStatus, JobApplication } from '../../../models/job-application.model';

function makeApplication(status = ApplicationStatus.Submitted): JobApplication {
  return {
    id: 7, jobOfferId: 3, userId: 'u1', userEmail: 'jan@example.com', userFullName: 'Jan Kowalski',
    jobTitle: 'Programista Angular', company: 'Acme', companyId: 1, note: null, appliedAt: new Date('2026-01-01T10:00:00Z'),
    status, statusChangedAt: '2026-01-01T10:00:00Z'
  };
}

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('ApplicationStatusDialogComponent', () => {
  let fixture: ComponentFixture<ApplicationStatusDialogComponent>;
  let component: ApplicationStatusDialogComponent;
  let updateStatus: ReturnType<typeof vi.fn>;
  let close: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;

  async function create(data: JobApplication = makeApplication()): Promise<void> {
    TestBed.overrideProvider(MAT_DIALOG_DATA, { useValue: data });
    fixture = TestBed.createComponent(ApplicationStatusDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function saveButton(): HTMLButtonElement {
    return root().querySelector<HTMLButtonElement>('button[mat-raised-button]')!;
  }

  function httpError(status: number, error: unknown = null): HttpErrorResponse {
    return new HttpErrorResponse({ status, statusText: 'Error', url: '/api/jobapplications/7/status', error });
  }

  beforeEach(async () => {
    updateStatus = vi.fn().mockReturnValue(of(makeApplication(ApplicationStatus.Interview)));
    close = vi.fn();
    snackBarOpen = vi.fn();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [ApplicationStatusDialogComponent],
      providers: [
        { provide: JobApplicationService, useValue: { updateStatus } },
        { provide: MatDialogRef, useValue: { close } },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } },
        { provide: MAT_DIALOG_DATA, useValue: makeApplication() }
      ]
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  describe('content', () => {
    it('shows the candidate, the offer and the current status', async () => {
      await create(makeApplication(ApplicationStatus.InReview));

      const text = root().textContent ?? '';
      expect(text).toContain('Jan Kowalski');
      expect(text).toContain('Programista Angular');
      expect(root().querySelector('.current app-status-chip')?.textContent?.trim()).toBe('W analizie');
    });

    it('falls back to the e-mail when the full name is empty', async () => {
      await create({ ...makeApplication(), userFullName: '' });

      expect(root().querySelector('.summary strong')?.textContent).toBe('jan@example.com');
    });

    it('offers all statuses except "Wycofana" to choose from', async () => {
      await create();

      expect(component.statusOptions.map(o => o.label)).toEqual([
        'Złożona', 'W analizie', 'Rozmowa', 'Oferta', 'Zatrudniony', 'Odrzucona'
      ]);
      expect(component.statusOptions.map(o => o.value)).not.toContain(ApplicationStatus.Withdrawn);
    });

    it('renders the selectable statuses in the dropdown without "Wycofana"', async () => {
      await create();

      root().querySelector<HTMLElement>('.mat-mdc-select-trigger')!.click();
      fixture.detectChanges();
      await fixture.whenStable();

      const options = Array.from(document.querySelectorAll('mat-option')).map(o => o.textContent?.trim());
      expect(options).toEqual(['Złożona', 'W analizie', 'Rozmowa', 'Oferta', 'Zatrudniony', 'Odrzucona']);
    });

    it('tells the recruiter that the comment is visible to the candidate', async () => {
      await create();

      expect(root().textContent).toContain('Komentarz będzie widoczny dla kandydata w historii aplikacji');
    });

    it('preselects the current status and starts with an empty comment', async () => {
      await create(makeApplication(ApplicationStatus.Offered));

      expect(component.status()).toBe(ApplicationStatus.Offered);
      expect(component.comment()).toBe('');
    });
  });

  describe('comment length', () => {
    it('limits the textarea to 1000 characters and shows the counter', async () => {
      await create();

      const textarea = root().querySelector<HTMLTextAreaElement>('textarea')!;
      expect(COMMENT_MAX_LENGTH).toBe(1000);
      expect(textarea.maxLength).toBe(1000);
      expect(root().textContent).toContain('0 / 1000');
    });

    it('updates the counter while typing', async () => {
      await create();
      const textarea = root().querySelector<HTMLTextAreaElement>('textarea')!;

      textarea.value = 'Zapraszamy';
      textarea.dispatchEvent(new Event('input'));

      expect(component.comment()).toBe('Zapraszamy');
      expect(root().textContent).toContain('10 / 1000');
    });

    it('blocks saving a comment longer than 1000 characters and explains why', async () => {
      await create();
      component.status.set(ApplicationStatus.Interview);
      component.comment.set('a'.repeat(1001));

      expect(component.commentTooLong()).toBe(true);
      expect(component.canSave()).toBe(false);
      expect(saveButton().disabled).toBe(true);
      expect(root().querySelector('.info.error')?.textContent).toContain('maksymalnie 1000 znaków');
      component.save();
      expect(updateStatus).not.toHaveBeenCalled();
    });

    it('measures the comment length after trimming, like the sent value', async () => {
      await create();
      component.status.set(ApplicationStatus.Interview);
      component.comment.set(`  ${'a'.repeat(1000)}  `);

      expect(component.commentTooLong()).toBe(false);
      expect(component.canSave()).toBe(true);
    });

    it('sends a comment of exactly 1000 characters unchanged', async () => {
      await create();
      component.status.set(ApplicationStatus.Interview);
      component.comment.set('a'.repeat(1000));

      component.save();

      expect(updateStatus.mock.calls[0][1].comment).toHaveLength(1000);
    });
  });

  describe('save button and notes', () => {
    it('disables saving when neither the status nor the comment changed and explains why', async () => {
      await create();

      expect(component.isNoteOnly()).toBe(true);
      expect(component.canSave()).toBe(false);
      expect(saveButton().disabled).toBe(true);
      expect(root().querySelector('.info')?.textContent).toContain('Wybierz inny status albo dodaj komentarz');
    });

    it('treats a whitespace-only comment as empty', async () => {
      await create();
      component.comment.set('   \n ');

      expect(component.hasComment()).toBe(false);
      expect(component.canSave()).toBe(false);
    });

    it('allows a note (same status with a comment) and changes the button label', async () => {
      await create();
      component.comment.set('Dzwoniłem, brak odpowiedzi');

      expect(component.canSave()).toBe(true);
      expect(saveButton().disabled).toBe(false);
      expect(saveButton().textContent?.trim()).toBe('Dodaj notatkę');
      expect(root().querySelector('.info')).toBeNull();
    });

    it('allows saving a different status without a comment', async () => {
      await create();
      component.status.set(ApplicationStatus.InReview);

      expect(component.isNoteOnly()).toBe(false);
      expect(component.canSave()).toBe(true);
      expect(saveButton().textContent?.trim()).toBe('Zapisz');
    });

    it('never allows saving the "Wycofana" status', async () => {
      await create();
      component.status.set(ApplicationStatus.Withdrawn);
      component.comment.set('komentarz');

      expect(component.canSave()).toBe(false);
      component.save();
      expect(updateStatus).not.toHaveBeenCalled();
    });

    it('does not call the API when saving is not allowed', async () => {
      await create();

      component.save();

      expect(updateStatus).not.toHaveBeenCalled();
    });
  });

  describe('saving', () => {
    it('sends PUT payload with the chosen status and the trimmed comment', async () => {
      await create();
      component.status.set(ApplicationStatus.Interview);
      component.comment.set('  Zapraszamy w czwartek  ');

      component.save();

      expect(updateStatus).toHaveBeenCalledWith(7, { status: ApplicationStatus.Interview, comment: 'Zapraszamy w czwartek' });
    });

    it('sends null instead of an empty comment', async () => {
      await create();
      component.status.set(ApplicationStatus.Rejected);
      component.comment.set('   ');

      component.save();

      expect(updateStatus).toHaveBeenCalledWith(7, { status: ApplicationStatus.Rejected, comment: null });
    });

    it('sends the status 0 (Złożona) as a number, not as a falsy value', async () => {
      await create(makeApplication(ApplicationStatus.InReview));
      component.status.set(ApplicationStatus.Submitted);

      component.save();

      expect(updateStatus.mock.calls[0][1].status).toBe(0);
    });

    it('closes the dialog with the updated application and notifies about the status change', async () => {
      const updated = makeApplication(ApplicationStatus.Interview);
      updateStatus.mockReturnValue(of(updated));
      await create();
      component.status.set(ApplicationStatus.Interview);

      component.save();

      expect(close).toHaveBeenCalledWith(updated);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Status aplikacji został zmieniony');
      expect(component.saving()).toBe(false);
    });

    it('notifies about a note when the status stayed the same', async () => {
      await create();
      component.comment.set('Notatka');

      component.save();

      expect(snackBarOpen.mock.calls[0][0]).toBe('Notatka została dodana');
      expect(close).toHaveBeenCalledTimes(1);
    });

    it('blocks the form and shows a spinner while the request is pending', async () => {
      const pending = new Subject<JobApplication>();
      updateStatus.mockReturnValue(pending as Observable<JobApplication>);
      await create();
      component.status.set(ApplicationStatus.Interview);

      component.save();

      expect(component.saving()).toBe(true);
      expect(saveButton().disabled).toBe(true);
      expect(root().querySelector('button[mat-raised-button] mat-spinner')).not.toBeNull();
      expect(root().querySelector<HTMLButtonElement>('button[mat-button]')!.disabled).toBe(true);

      component.save();
      expect(updateStatus).toHaveBeenCalledTimes(1);

      pending.next(makeApplication(ApplicationStatus.Interview));
      expect(close).toHaveBeenCalled();
    });
  });

  describe('errors', () => {
    async function failWith(error: HttpErrorResponse): Promise<void> {
      updateStatus.mockReturnValue(throwError(() => error));
      await create();
      component.status.set(ApplicationStatus.Interview);
      component.save();
    }

    it.each([
      ['Application has been withdrawn by the candidate', 'Kandydat wycofał tę aplikację'],
      ['Application already has this status - add a comment to record a note', 'Aplikacja ma już ten status – dodaj komentarz, aby zapisać notatkę'],
      ['Application not found', 'Aplikacja nie została znaleziona']
    ])('translates the 400/404 API message "%s"', async (message, expected) => {
      await failWith(httpError(400, { message }));

      expect(snackBarOpen.mock.calls[0][0]).toBe(expected);
    });

    it('translates the 409 conflict message and asks to refresh the data', async () => {
      await failWith(httpError(409, { message: 'Application status was changed by another request' }));

      expect(snackBarOpen.mock.calls[0][0]).toBe('Status aplikacji został w międzyczasie zmieniony – odśwież dane');
    });

    it('keeps the dialog open and re-enables the form after an error', async () => {
      await failWith(httpError(409, { message: 'Application status was changed by another request' }));

      expect(close).not.toHaveBeenCalled();
      expect(component.saving()).toBe(false);
      expect(component.canSave()).toBe(true);
    });

    it('shows a generic message for server errors', async () => {
      await failWith(httpError(500));

      expect(snackBarOpen.mock.calls[0][0]).toBe('Błąd podczas zmiany statusu aplikacji');
    });

    it('shows a generic message for ValidationProblemDetails', async () => {
      await failWith(httpError(400, { errors: { Comment: ['too long'] } }));

      expect(snackBarOpen.mock.calls[0][0]).toBe('Przesłane dane są nieprawidłowe');
    });

    it('shows a connection message when the server is unreachable', async () => {
      await failWith(httpError(0));

      expect(snackBarOpen.mock.calls[0][0]).toBe('Brak połączenia z serwerem');
    });

    it.each([401, 403])('does not duplicate the interceptor notice for %i', async status => {
      await failWith(httpError(status));

      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(close).not.toHaveBeenCalled();
      expect(component.saving()).toBe(false);
    });
  });
});
