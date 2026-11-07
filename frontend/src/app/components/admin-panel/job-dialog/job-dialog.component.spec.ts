import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of, throwError } from 'rxjs';
import { JobDialogComponent, JobDialogData, endOfLocalDay, toSalary } from './job-dialog.component';
import { JobService } from '../../../services/job.service';
import { CompanyService } from '../../../services/company.service';
import { AdminService } from '../../../services/admin.service';
import { CategoryService } from '../../../services/category.service';
import { SkillService } from '../../../services/skill.service';
import { ContractTypes, Currency, JobOffer, JobOfferStatus, SalaryPeriod, SeniorityLevel, WorkType } from '../../../models/job-offer.model';
import { Category } from '../../../models/taxonomy.model';
import { Company, CompanyStatus } from '../../../models/company.model';

const CATEGORIES: Category[] = [
  { id: 1, name: 'Programowanie', slug: 'programowanie', sortOrder: 1, activeJobsCount: 5 },
  { id: 2, name: 'Testowanie', slug: 'testowanie', sortOrder: 2, activeJobsCount: 2 }
];

const EMPTY_PAGE = { result: [], pagination: { CurrentPage: 1, ItemsPerPage: 10, TotalItems: 0, TotalPages: 1 } };

function makeCompany(status: CompanyStatus): Company {
  return {
    id: 7, name: 'Moja Firma', description: null, website: null, location: null,
    status, rejectionReason: null, createdAt: '2026-01-01T00:00:00Z', reviewedAt: null, activeJobsCount: 0
  };
}

const DESCRIPTION = 'Opis stanowiska, który ma co najmniej pięćdziesiąt znaków długości.';

const EXISTING_JOB: JobOffer = {
  id: 9, title: 'Backend Developer', company: 'Acme', companyId: 1, description: DESCRIPTION, salaryMin: 8000, salaryMax: 12000,
  location: 'Rzeszów', workType: WorkType.Hybrid, createdAt: new Date('2026-01-01'),
  status: JobOfferStatus.Active, expiresAt: null, isExpired: false,
  categoryId: null, categoryName: null, categorySlug: null, seniority: null, contractTypes: 0,
  salaryCurrency: Currency.PLN, salaryPeriod: SalaryPeriod.Month, skills: []
};

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('JobDialogComponent', () => {
  let fixture: ComponentFixture<JobDialogComponent>;
  let component: JobDialogComponent;
  let createJob: ReturnType<typeof vi.fn>;
  let updateJob: ReturnType<typeof vi.fn>;
  let close: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;
  let alertSpy: ReturnType<typeof vi.spyOn>;
  let getCategories: ReturnType<typeof vi.fn>;

  async function create(job: JobOffer | null = null, options: Partial<JobDialogData> = {}): Promise<void> {
    const data: JobDialogData = { job, mode: 'admin', ...options };
    await TestBed.configureTestingModule({
      imports: [JobDialogComponent],
      providers: [
        { provide: JobService, useValue: { createJob, updateJob } },
        { provide: CompanyService, useValue: { getCompanies: vi.fn().mockReturnValue(of(EMPTY_PAGE)) } },
        { provide: AdminService, useValue: { getCompanies: vi.fn().mockReturnValue(of(EMPTY_PAGE)) } },
        { provide: CategoryService, useValue: { getCategories } },
        { provide: SkillService, useValue: { suggest: vi.fn().mockReturnValue(of([])) } },
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
      companyId: 3,
      location: 'Rzeszów',
      workType: WorkType.Remote,
      salaryMin: 8000,
      salaryMax: 12000,
      description: DESCRIPTION,
      status: JobOfferStatus.Active,
      expiresAt: null,
      categoryId: null,
      seniority: null,
      contractTypes: 0,
      salaryCurrency: Currency.PLN,
      salaryPeriod: SalaryPeriod.Month,
      skills: [],
      ...overrides
    });
  }

  function httpError(status: number, error: unknown = null): HttpErrorResponse {
    return new HttpErrorResponse({ status, statusText: 'Error', url: '/api/jobs', error });
  }

  beforeEach(() => {
    getCategories = vi.fn().mockReturnValue(of(CATEGORIES));
    createJob = vi.fn().mockReturnValue(of({}));
    updateJob = vi.fn().mockReturnValue(of(undefined));
    close = vi.fn();
    snackBarOpen = vi.fn();
    alertSpy = vi.spyOn(window, 'alert').mockImplementation(() => undefined);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

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
      expect(root().querySelector('.form-error')?.textContent).toContain('Wynagrodzenie „od” nie może być wyższe niż „do”');
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

    it('defaults a new offer to Active without an expiration date', async () => {
      await create();

      expect(component.jobForm.value.status).toBe(JobOfferStatus.Active);
      expect(component.jobForm.value.expiresAt).toBeNull();
    });

    it('lets the admin choose only Szkic, Aktywna and Zamknięta (no "Wygasła")', async () => {
      await create();

      expect(component.statusOptions.map(o => o.label)).toEqual(['Szkic', 'Aktywna', 'Zamknięta']);

      const root = fixture.nativeElement as HTMLElement;
      root.querySelector<HTMLElement>('mat-select[formcontrolname="status"] .mat-mdc-select-trigger')!.click();
      fixture.detectChanges();
      await fixture.whenStable();
      expect(Array.from(document.querySelectorAll('mat-option')).map(o => o.textContent?.trim())).toEqual(['Szkic', 'Aktywna', 'Zamknięta']);
    });

    it('computes the end of the local day as 23:59:59.999', () => {
      const end = endOfLocalDay(new Date(2026, 5, 15, 8, 30, 0));

      expect([end.getFullYear(), end.getMonth(), end.getDate()]).toEqual([2026, 5, 15]);
      expect([end.getHours(), end.getMinutes(), end.getSeconds(), end.getMilliseconds()]).toEqual([23, 59, 59, 999]);
    });

    it('sends the end of the newly selected day when the day of an edited offer changes', async () => {
      const original = inDays(5).toISOString();
      await create({ ...EXISTING_JOB, expiresAt: original });
      const newDay = inDays(12);
      component.jobForm.patchValue({ expiresAt: newDay });

      component.onSubmit();

      const sent = updateJob.mock.calls[0][1].expiresAt as string;
      expect(sent).not.toBe(original);
      expect(sent).toBe(endOfLocalDay(newDay).toISOString());
    });

    it('sends null when the expiration date of an edited offer is cleared', async () => {
      await create({ ...EXISTING_JOB, expiresAt: inDays(5).toISOString() });

      component.clearExpiresAt();
      component.onSubmit();

      expect(updateJob).toHaveBeenCalledWith(9, expect.objectContaining({ expiresAt: null }));
    });

    it('lets the admin edit an expired active offer without changing its date and sends the original date', async () => {
      const original = yesterday().toISOString();
      await create({ ...EXISTING_JOB, expiresAt: original, isExpired: true });

      expect(component.jobForm.hasError('expiresAtInPast')).toBe(false);
      expect(component.jobForm.valid).toBe(true);
      component.jobForm.patchValue({ title: 'Poprawiony tytuł' });
      component.onSubmit();

      expect(updateJob).toHaveBeenCalledWith(9, expect.objectContaining({
        title: 'Poprawiony tytuł', status: JobOfferStatus.Active, expiresAt: original
      }));
    });

    it('rejects a different past date for an expired active offer', async () => {
      const twoDaysAgo = yesterday();
      twoDaysAgo.setDate(twoDaysAgo.getDate() - 1);
      await create({ ...EXISTING_JOB, expiresAt: yesterday().toISOString(), isExpired: true });

      component.jobForm.patchValue({ expiresAt: twoDaysAgo });

      expect(component.jobForm.hasError('expiresAtInPast')).toBe(true);
      component.jobForm.patchValue({ expiresAt: inDays(3) });
      expect(component.jobForm.valid).toBe(true);
    });

    it.each([JobOfferStatus.Draft, JobOfferStatus.Closed])(
      'rejects publishing an offer with status %s and an unchanged past date', async status => {
        await create({ ...EXISTING_JOB, status, expiresAt: yesterday().toISOString() });
        expect(component.jobForm.valid).toBe(true);

        component.jobForm.patchValue({ status: JobOfferStatus.Active });

        expect(component.jobForm.hasError('expiresAtInPast')).toBe(true);
      });

    it('drops the "date in the past" error after switching an offer to Closed', async () => {
      await create();
      fillValidForm({ expiresAt: yesterday() });
      expect(component.jobForm.hasError('expiresAtInPast')).toBe(true);

      component.jobForm.patchValue({ status: JobOfferStatus.Closed });

      expect(component.jobForm.hasError('expiresAtInPast')).toBe(false);
      expect(component.jobForm.valid).toBe(true);
    });

    it('does not submit an active offer with a past date and keeps the submit button disabled', async () => {
      await create();
      fillValidForm({ expiresAt: yesterday() });
      fixture.detectChanges();

      component.onSubmit();

      expect(createJob).not.toHaveBeenCalled();
      expect((fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('button.submit-button')?.disabled).toBe(true);
    });

    it('moves an already passed expiration hour of an active offer to the end of the same day', async () => {
      vi.useFakeTimers({ toFake: ['Date'] });
      vi.setSystemTime(new Date(2026, 5, 15, 15, 0, 0));
      const earlierToday = new Date(2026, 5, 15, 9, 0, 0).toISOString();
      await create({ ...EXISTING_JOB, expiresAt: earlierToday, isExpired: true });

      component.onSubmit();

      expect(updateJob).toHaveBeenCalledWith(9, expect.objectContaining({
        expiresAt: new Date(2026, 5, 15, 23, 59, 59, 999).toISOString()
      }));
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
      expect(snackBarOpen.mock.calls[0][0]).toBe('Oferta została dodana');
      expect(close).toHaveBeenCalledWith(true);
      expect(alertSpy).not.toHaveBeenCalled();
    });

    it('sends a zero salary as 0 instead of dropping it', async () => {
      await create();
      fillValidForm({ salaryMin: 0, salaryMax: 0 });

      component.onSubmit();

      expect(createJob).toHaveBeenCalledWith(expect.objectContaining({ salaryMin: 0, salaryMax: 0 }));
    });

    it('sends empty salary fields as null and numeric strings as numbers', async () => {
      await create();
      fillValidForm({ salaryMin: '', salaryMax: '9000' });

      component.onSubmit();

      expect(createJob).toHaveBeenCalledWith(expect.objectContaining({ salaryMin: null, salaryMax: 9000 }));
    });

    it('normalizes salary values', () => {
      expect([toSalary(0), toSalary(null), toSalary(undefined), toSalary(''), toSalary('15'), toSalary(8000)])
        .toEqual([0, null, null, null, 15, 8000]);
    });

    it('updates an existing job', async () => {
      await create(EXISTING_JOB);

      expect(component.jobForm.value.title).toBe('Backend Developer');
      component.onSubmit();

      expect(updateJob).toHaveBeenCalledWith(9, expect.objectContaining({ salaryMin: 8000, salaryMax: 12000 }));
      expect(snackBarOpen.mock.calls[0][0]).toBe('Oferta została zapisana');
      expect(close).toHaveBeenCalledWith(true);
    });

    it.each([401, 403])('does not duplicate the interceptor notice for %i and keeps the dialog open', async status => {
      await create();
      fillValidForm();
      createJob.mockReturnValue(throwError(() => httpError(status)));

      component.onSubmit();

      expect(component.submitting()).toBe(false);
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

  describe('company', () => {
    it('requires the admin to pick a company and sends its id', async () => {
      await create();
      fillValidForm({ companyId: null });
      expect(component.jobForm.valid).toBe(false);

      component.onCompanyChange({ id: 12, name: 'Acme' });
      component.onSubmit();

      expect(createJob).toHaveBeenCalledWith(expect.objectContaining({ companyId: 12 }));
      expect(createJob.mock.calls[0][0]).not.toHaveProperty('company');
    });

    it('prefills the company of an edited offer', async () => {
      await create(EXISTING_JOB);

      expect(component.jobForm.value.companyId).toBe(1);
      expect(component.initialCompany).toEqual({ id: 1, name: 'Acme' });
    });

    it('hides the company field from an employer and sends the employer company', async () => {
      await create(null, { mode: 'employer', company: makeCompany(CompanyStatus.Approved) });

      expect((fixture.nativeElement as HTMLElement).querySelector('app-company-autocomplete')).toBeNull();
      expect(component.jobForm.get('companyId')).toBeNull();
      expect(component.statusOptions.map(o => o.label)).toEqual(['Szkic', 'Aktywna', 'Zamknięta']);
      component.jobForm.setValue({
        title: 'Angular Developer', location: 'Rzeszów', workType: WorkType.Remote, salaryMin: null, salaryMax: null,
        description: DESCRIPTION, status: JobOfferStatus.Active, expiresAt: null,
        categoryId: null, seniority: null, contractTypes: 0, salaryCurrency: Currency.PLN, salaryPeriod: SalaryPeriod.Month, skills: []
      });
      component.onSubmit();

      expect(createJob).toHaveBeenCalledWith(expect.objectContaining({ companyId: 7, status: JobOfferStatus.Active }));
    });

    it('limits an employer of a not approved company to drafts and explains why', async () => {
      await create(null, { mode: 'employer', company: makeCompany(CompanyStatus.Pending) });

      expect(component.canPublish).toBe(false);
      expect(component.statusOptions.map(o => o.label)).toEqual(['Szkic']);
      expect(component.jobForm.value.status).toBe(JobOfferStatus.Draft);
      expect((fixture.nativeElement as HTMLElement).querySelector('.publish-info')?.textContent)
        .toContain('Firma nie została jeszcze zatwierdzona');
    });
  });

  describe('taxonomy fields', () => {
    const root = () => fixture.nativeElement as HTMLElement;

    it('loads the categories for the select', async () => {
      await create();

      expect(getCategories).toHaveBeenCalledTimes(1);
      expect(component.categories()).toEqual(CATEGORIES);
    });

    it('still works when the category list cannot be loaded', async () => {
      getCategories.mockReturnValue(throwError(() => httpError(500)));
      await create();
      fillValidForm();

      expect(component.categories()).toEqual([]);
      expect(component.jobForm.valid).toBe(true);
    });

    it('defaults a new offer to no category, no level, no contract types, PLN per month and no skills', async () => {
      await create();

      expect(component.jobForm.value).toMatchObject({
        categoryId: null, seniority: null, contractTypes: 0, salaryCurrency: Currency.PLN, salaryPeriod: SalaryPeriod.Month, skills: []
      });
    });

    it('sends the new fields on create', async () => {
      await create();
      fillValidForm({
        categoryId: 2, seniority: SeniorityLevel.Lead, salaryCurrency: Currency.EUR, salaryPeriod: SalaryPeriod.Hour,
        skills: ['Angular', 'C#']
      });
      component.toggleContract(ContractTypes.B2B, true);
      component.toggleContract(ContractTypes.Internship, true);

      component.onSubmit();

      expect(createJob).toHaveBeenCalledWith(expect.objectContaining({
        categoryId: 2, seniority: SeniorityLevel.Lead, contractTypes: 18, salaryCurrency: Currency.EUR,
        salaryPeriod: SalaryPeriod.Hour, skills: ['Angular', 'C#']
      }));
    });

    it('prefills the new fields when editing and sends them back', async () => {
      await create({
        ...EXISTING_JOB, categoryId: 1, categoryName: 'Programowanie', categorySlug: 'programowanie',
        seniority: SeniorityLevel.Mid, contractTypes: ContractTypes.EmploymentContract, salaryCurrency: Currency.GBP,
        salaryPeriod: SalaryPeriod.Year, skills: ['Docker']
      });

      expect(component.jobForm.value).toMatchObject({
        categoryId: 1, seniority: SeniorityLevel.Mid, contractTypes: 1, salaryCurrency: Currency.GBP, salaryPeriod: SalaryPeriod.Year, skills: ['Docker']
      });
      expect(component.hasContract(ContractTypes.EmploymentContract)).toBe(true);
      expect(component.hasContract(ContractTypes.B2B)).toBe(false);

      component.onSubmit();

      expect(updateJob).toHaveBeenCalledWith(9, expect.objectContaining({
        categoryId: 1, seniority: SeniorityLevel.Mid, contractTypes: 1, salaryCurrency: Currency.GBP, salaryPeriod: SalaryPeriod.Year, skills: ['Docker']
      }));
    });

    it('shows the selected currency in the salary fields', async () => {
      await create();
      fillValidForm({ salaryCurrency: Currency.USD });
      fixture.detectChanges();

      const suffixes = Array.from(root().querySelectorAll('.salary-field [matTextSuffix], .salary-field .mat-mdc-form-field-text-suffix'));
      expect(component.currencyLabel).toBe('USD');
      expect(suffixes.every(s => s.textContent?.includes('USD'))).toBe(true);
    });

    it('groups the contract type checkboxes in a fieldset with a legend', async () => {
      await create();

      const group = root().querySelector('fieldset.contract-types')!;
      expect(group.querySelector('legend')?.textContent).toBe('Typ umowy');
      expect(group.querySelectorAll('mat-checkbox').length).toBe(5);
    });

    it('ticking a contract type checkbox toggles its bit', async () => {
      await create();

      root().querySelector<HTMLInputElement>('fieldset.contract-types mat-checkbox input')!.click();
      expect(component.jobForm.value.contractTypes).toBe(ContractTypes.EmploymentContract);

      root().querySelector<HTMLInputElement>('fieldset.contract-types mat-checkbox input')!.click();
      expect(component.jobForm.value.contractTypes).toBe(0);
    });

    it('marks the form invalid for more than 15 skills or a name that is too long', async () => {
      await create();

      fillValidForm({ skills: Array.from({ length: 16 }, (_, i) => `S${i}`) });
      expect(component.jobForm.valid).toBe(false);

      fillValidForm({ skills: ['x'.repeat(51)] });
      expect(component.jobForm.valid).toBe(false);

      fillValidForm({ skills: Array.from({ length: 15 }, (_, i) => `S${i}`) });
      expect(component.jobForm.valid).toBe(true);
    });

    it('takes skills from the skills input', async () => {
      await create();

      component.onSkillsChange(['Rust']);

      expect(component.jobForm.value.skills).toEqual(['Rust']);
      expect(component.jobForm.get('skills')?.dirty).toBe(true);
    });
  });
});
