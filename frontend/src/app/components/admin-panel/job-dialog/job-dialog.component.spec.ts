import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of, throwError } from 'rxjs';
import { JobDialogComponent, endOfLocalDay } from './job-dialog.component';
import { JobService } from '../../../services/job.service';
import { JobOffer, JobOfferStatus, WorkType } from '../../../models/job-offer.model';

const DESCRIPTION = 'Opis stanowiska, który ma co najmniej pięćdziesiąt znaków długości.';

const EXISTING_JOB: JobOffer = {
  id: 9, title: 'Backend Developer', company: 'Acme', description: DESCRIPTION, salaryMin: 8000, salaryMax: 12000,
  location: 'Rzeszów', workType: WorkType.Hybrid, createdAt: new Date('2026-01-01'),
  status: JobOfferStatus.Active, expiresAt: null, isExpired: false
};

describe('JobDialogComponent', () => {
  let fixture: ComponentFixture<JobDialogComponent>;
  let component: JobDialogComponent;
  let createJob: ReturnType<typeof vi.fn>;
  let updateJob: ReturnType<typeof vi.fn>;
  let close: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;
  let alertSpy: ReturnType<typeof vi.spyOn>;

  async function create(data: JobOffer | null = null): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [JobDialogComponent],
      providers: [
        { provide: JobService, useValue: { createJob, updateJob } },
        { provide: MatDialogRef, useValue: { close } },
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(JobDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function fillValidForm(overrides: Record<string, unknown> = {}): void {
    component.jobForm.setValue({
      title: 'Angular Developer',
      company: 'Acme',
      location: 'Rzeszów',
      workType: WorkType.Remote,
      salaryMin: 8000,
      salaryMax: 12000,
      description: DESCRIPTION,
      status: JobOfferStatus.Active,
      expiresAt: null,
      ...overrides
    });
  }

  function httpError(status: number, error: unknown = null): HttpErrorResponse {
    return new HttpErrorResponse({ status, statusText: 'Error', url: '/api/jobs', error });
  }

  beforeEach(() => {
    createJob = vi.fn().mockReturnValue(of({}));
    updateJob = vi.fn().mockReturnValue(of(undefined));
    close = vi.fn();
    snackBarOpen = vi.fn();
    alertSpy = vi.spyOn(window, 'alert').mockImplementation(() => undefined);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  afterEach(() => vi.restoreAllMocks());

  describe('salary validation', () => {
    function root(): HTMLElement {
      component.jobForm.markAllAsTouched();
      fixture.detectChanges();
      return fixture.nativeElement as HTMLElement;
    }

    it.each([
      [null, null],
      [8000, null],
      [null, 12000],
      [0, 0],
      [0, 5000],
      [10000, 10000]
    ])('accepts salaryMin=%s and salaryMax=%s', async (salaryMin, salaryMax) => {
      await create();
      fillValidForm({ salaryMin, salaryMax });

      expect(component.jobForm.valid).toBe(true);
    });

    it('rejects salaryMin greater than salaryMax and shows the message below the fields', async () => {
      await create();
      fillValidForm({ salaryMin: 15000, salaryMax: 8000 });

      expect(component.jobForm.hasError('salaryRangeInvalid')).toBe(true);
      expect(component.jobForm.valid).toBe(false);
      expect(root().querySelector('.salary-error')?.textContent).toContain('Wynagrodzenie "Od" nie może być wyższe niż "Do"');
    });

    it('compares salaries numerically', async () => {
      await create();
      fillValidForm({ salaryMin: '9000', salaryMax: '10000' });

      expect(component.jobForm.hasError('salaryRangeInvalid')).toBe(false);
    });

    it.each(['salaryMin', 'salaryMax'])('rejects a negative %s and shows the message under the field', async field => {
      await create();
      fillValidForm({ salaryMin: null, salaryMax: null, [field]: -1 });

      expect(component.jobForm.get(field)!.hasError('min')).toBe(true);
      expect(component.jobForm.valid).toBe(false);
      const errors = Array.from(root().querySelectorAll('.salary-field mat-error')).map(e => e.textContent?.trim());
      expect(errors).toEqual(['Wynagrodzenie nie może być ujemne']);
    });

    it('does not send the request and keeps the submit button disabled for an invalid salary range', async () => {
      await create();
      fillValidForm({ salaryMin: 15000, salaryMax: 8000 });
      fixture.detectChanges();

      const submit = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('mat-dialog-actions button')).pop()!;
      expect(submit.disabled).toBe(true);

      component.onSubmit();
      expect(createJob).not.toHaveBeenCalled();
    });
  });

  describe('status and expiration date', () => {
    const yesterday = () => { const d = new Date(); d.setDate(d.getDate() - 1); return d; };
    const inDays = (days: number) => { const d = new Date(); d.setDate(d.getDate() + days); return d; };

    it('rejects an active offer with an expiration date in the past', async () => {
      await create();
      fillValidForm({ expiresAt: yesterday() });
      fixture.detectChanges();

      expect(component.jobForm.hasError('expiresAtInPast')).toBe(true);
      expect((fixture.nativeElement as HTMLElement).querySelector('.expiry-error')?.textContent)
        .toContain('Aktywna oferta musi mieć datę wygaśnięcia w przyszłości');
    });

    it.each([JobOfferStatus.Draft, JobOfferStatus.Closed])('accepts a past expiration date for status %s', async status => {
      await create();
      fillValidForm({ status, expiresAt: yesterday() });

      expect(component.jobForm.valid).toBe(true);
    });

    it('accepts today as the expiration date of an active offer (valid until the end of the day)', async () => {
      await create();
      fillValidForm({ expiresAt: new Date() });

      expect(component.jobForm.valid).toBe(true);
    });

    it('sends the end of the selected local day as an ISO UTC string together with the status', async () => {
      await create();
      const selected = inDays(10);
      fillValidForm({ status: JobOfferStatus.Draft, expiresAt: selected });

      component.onSubmit();

      const expected = endOfLocalDay(selected).toISOString();
      expect(createJob).toHaveBeenCalledWith(expect.objectContaining({ status: JobOfferStatus.Draft, expiresAt: expected }));
    });

    it('sends null when no expiration date is selected', async () => {
      await create();
      fillValidForm();

      component.onSubmit();

      expect(createJob).toHaveBeenCalledWith(expect.objectContaining({ expiresAt: null }));
    });

    it('keeps the exact expiration date of an edited offer when the day is unchanged', async () => {
      const expiresAt = inDays(5).toISOString();
      await create({ ...EXISTING_JOB, status: JobOfferStatus.Closed, expiresAt });

      expect(component.jobForm.value.status).toBe(JobOfferStatus.Closed);
      component.onSubmit();

      expect(updateJob).toHaveBeenCalledWith(9, expect.objectContaining({ status: JobOfferStatus.Closed, expiresAt }));
    });
  });

  describe('saving', () => {
    it('creates a new job, shows a snackbar and closes the dialog', async () => {
      await create();
      fillValidForm({ salaryMin: null, salaryMax: null });

      component.onSubmit();

      expect(createJob).toHaveBeenCalledWith(expect.objectContaining({ title: 'Angular Developer', salaryMin: null, salaryMax: null }));
      expect(snackBarOpen.mock.calls[0][0]).toBe('Oferta dodana!');
      expect(close).toHaveBeenCalledWith(true);
      expect(alertSpy).not.toHaveBeenCalled();
    });

    it('updates an existing job', async () => {
      await create(EXISTING_JOB);

      expect(component.jobForm.value.title).toBe('Backend Developer');
      component.onSubmit();

      expect(updateJob).toHaveBeenCalledWith(9, expect.objectContaining({ salaryMin: 8000, salaryMax: 12000 }));
      expect(snackBarOpen.mock.calls[0][0]).toBe('Oferta zaktualizowana!');
      expect(close).toHaveBeenCalledWith(true);
    });

    it.each([401, 403])('does not duplicate the interceptor notice for %i and keeps the dialog open', async status => {
      await create();
      fillValidForm();
      createJob.mockReturnValue(throwError(() => httpError(status)));

      component.onSubmit();

      expect(component.submitting).toBe(false);
      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(alertSpy).not.toHaveBeenCalled();
      expect(close).not.toHaveBeenCalled();
    });

    it('shows a readable message for ValidationProblemDetails instead of the raw HTTP error', async () => {
      await create();
      fillValidForm();
      createJob.mockReturnValue(throwError(() => httpError(400, {
        title: 'One or more validation errors occurred.', status: 400, errors: { SalaryMin: ['invalid'] }
      })));

      component.onSubmit();

      expect(snackBarOpen.mock.calls[0][0]).toBe('Przesłane dane są nieprawidłowe');
      expect(close).not.toHaveBeenCalled();
    });

    it('shows the translated message when the edited job no longer exists', async () => {
      await create(EXISTING_JOB);
      updateJob.mockReturnValue(throwError(() => httpError(404, { message: 'Job offer not found' })));

      component.onSubmit();

      expect(snackBarOpen.mock.calls[0][0]).toBe('Oferta nie została znaleziona');
    });

    it('shows a generic message for server errors', async () => {
      await create();
      fillValidForm();
      createJob.mockReturnValue(throwError(() => httpError(500)));

      component.onSubmit();

      expect(snackBarOpen.mock.calls[0][0]).toBe('Błąd podczas zapisywania oferty');
    });

    it('does not send an invalid form', async () => {
      await create();

      component.onSubmit();

      expect(createJob).not.toHaveBeenCalled();
    });
  });
});
