import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject, of, throwError } from 'rxjs';
import { EmployerCompanyTabComponent } from './employer-company-tab.component';
import { CompanyService } from '../../../services/company.service';
import { Company, CompanyStatus } from '../../../models/company.model';
import { makeCompany } from '../../../../testing/company-fixtures';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('EmployerCompanyTabComponent', () => {
  let fixture: ComponentFixture<EmployerCompanyTabComponent>;
  let component: EmployerCompanyTabComponent;
  let updateMyCompany: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;
  let emitted: Company[];

  async function create(company: Company): Promise<void> {
    fixture = TestBed.createComponent(EmployerCompanyTabComponent);
    fixture.componentRef.setInput('company', company);
    component = fixture.componentInstance;
    emitted = [];
    component.companyChange.subscribe(c => emitted.push(c));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function banner(): HTMLElement | null {
    return root().querySelector<HTMLElement>('.status-banner');
  }

  function submitButton(): HTMLButtonElement {
    return root().querySelector<HTMLButtonElement>('button[type="submit"]')!;
  }

  function edit(name: string, value: string): void {
    const control = component.form.get(name)!;
    control.setValue(value);
    control.markAsDirty();
    control.markAsTouched();
  }

  function httpError(status: number, error: unknown = null): HttpErrorResponse {
    return new HttpErrorResponse({ status, statusText: 'Error', error });
  }

  beforeEach(async () => {
    updateMyCompany = vi.fn().mockImplementation((body: { name: string }) =>
      of(makeCompany(3, { name: body.name, status: CompanyStatus.Pending })));
    snackBarOpen = vi.fn();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [EmployerCompanyTabComponent],
      providers: [
        provideRouter([]),
        { provide: CompanyService, useValue: { updateMyCompany } },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } }
      ]
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  describe('status banner', () => {
    it('pending: asks to wait for verification and mentions drafts', async () => {
      await create(makeCompany(3, { status: CompanyStatus.Pending }));

      const el = banner()!;
      expect(el.classList).toContain('tone-pending');
      expect(el.textContent).toContain('Oczekuje na weryfikację');
      expect(el.textContent).toContain('szkice');
      expect(el.querySelector('a')).toBeNull();
    });

    it('rejected: shows the rejection reason and how to resubmit', async () => {
      await create(makeCompany(3, { status: CompanyStatus.Rejected, rejectionReason: 'Brak NIP w opisie' }));

      const el = banner()!;
      expect(el.classList).toContain('tone-rejected');
      expect(el.textContent).toContain('Zgłoszenie odrzucone');
      expect(el.textContent).toContain('Powód: Brak NIP w opisie');
      expect(el.textContent).toContain('Wyślij ponownie');
    });

    it('rejected without a reason: omits the reason line', async () => {
      await create(makeCompany(3, { status: CompanyStatus.Rejected, rejectionReason: null }));

      expect(banner()!.textContent).not.toContain('Powód:');
    });

    it('does not show a stale rejection reason for a pending company', async () => {
      await create(makeCompany(3, { status: CompanyStatus.Pending, rejectionReason: 'Stary powód' }));

      expect(banner()!.textContent).not.toContain('Stary powód');
    });

    it('approved: confirms approval and links to the public profile', async () => {
      await create(makeCompany(3, { status: CompanyStatus.Approved }));

      const el = banner()!;
      expect(el.classList).toContain('tone-approved');
      expect(el.textContent).toContain('Firma zatwierdzona');
      const link = el.querySelector<HTMLAnchorElement>('a')!;
      expect(link.textContent).toContain('Profil publiczny');
      expect(link.getAttribute('href')).toBe('/companies/3');
    });

    it('shows the review date when present', async () => {
      await create(makeCompany(3, { status: CompanyStatus.Approved, reviewedAt: '2026-03-05T14:30:00Z' }));

      expect(banner()!.textContent).toContain('Weryfikacja:');
      expect(banner()!.textContent).toContain('2026');
    });

    it('shows no banner when the API did not return a status', async () => {
      await create(makeCompany(3, { status: null }));

      expect(banner()).toBeNull();
    });
  });

  describe('form', () => {
    it('is prefilled with the company data', async () => {
      await create(makeCompany(3, { name: 'Acme', description: 'Opis', website: 'https://acme.pl', location: 'Łódź' }));

      expect(component.form.value).toEqual({ name: 'Acme', description: 'Opis', website: 'https://acme.pl', location: 'Łódź' });
    });

    it('uses empty strings for null optional fields', async () => {
      await create(makeCompany(3, { description: null, website: null, location: null }));

      expect(component.form.value).toEqual(expect.objectContaining({ description: '', website: '', location: '' }));
    });

    it('requires a non-blank name', async () => {
      await create(makeCompany(3));

      edit('name', '   ');
      expect(component.form.get('name')!.valid).toBe(false);
      expect(root().textContent).toContain('Nazwa firmy jest wymagana');

      edit('name', '');
      expect(component.form.get('name')!.hasError('required')).toBe(true);
    });

    it.each([['javascript:alert(1)'], ['ftp://x.pl'], ['acme.pl']])('rejects the website "%s"', async website => {
      await create(makeCompany(3));

      edit('website', website);

      expect(component.form.get('website')!.valid).toBe(false);
      expect(root().textContent).toContain('Podaj adres zaczynający się od http:// lub https://');
      expect(component.canSubmit()).toBe(false);
    });

    it('accepts http and https websites', async () => {
      await create(makeCompany(3));

      edit('website', 'http://acme.pl');
      expect(component.form.get('website')!.valid).toBe(true);
      edit('website', 'https://acme.pl');
      expect(component.form.get('website')!.valid).toBe(true);
    });

    it('limits name to 200 and description to 5000 characters', async () => {
      await create(makeCompany(3));

      edit('name', 'n'.repeat(201));
      edit('description', 'd'.repeat(5001));

      expect(component.form.get('name')!.hasError('maxlength')).toBe(true);
      expect(component.form.get('description')!.hasError('maxlength')).toBe(true);
    });
  });

  describe('submit button', () => {
    it('is disabled for a pristine pending/approved company', async () => {
      await create(makeCompany(3, { status: CompanyStatus.Approved }));
      expect(submitButton().disabled).toBe(true);
      expect(submitButton().textContent).toContain('Zapisz zmiany');
    });

    it('is enabled after a change', async () => {
      await create(makeCompany(3, { status: CompanyStatus.Pending }));

      edit('location', 'Gdańsk');

      expect(submitButton().disabled).toBe(false);
    });

    it('stays disabled when the changed form is invalid', async () => {
      await create(makeCompany(3, { status: CompanyStatus.Pending }));

      edit('name', '');

      expect(submitButton().disabled).toBe(true);
    });

    it('rejected company: shows "Wyślij ponownie" and is enabled without changes', async () => {
      await create(makeCompany(3, { status: CompanyStatus.Rejected, rejectionReason: 'x' }));

      expect(submitButton().textContent).toContain('Wyślij ponownie');
      expect(submitButton().disabled).toBe(false);
    });

    it('rejected company with an invalid form cannot be resubmitted', async () => {
      await create(makeCompany(3, { status: CompanyStatus.Rejected, rejectionReason: 'x' }));

      edit('website', 'javascript:alert(1)');

      expect(submitButton().disabled).toBe(true);
    });
  });

  describe('saving', () => {
    it('sends trimmed values (blank optional fields as null), emits the updated company and notifies', async () => {
      await create(makeCompany(3, { name: 'Acme', status: CompanyStatus.Approved }));
      edit('name', '  Acme 2  ');
      edit('location', '   ');
      edit('description', '');

      component.onSubmit();

      expect(updateMyCompany).toHaveBeenCalledWith({
        name: 'Acme 2',
        description: null,
        website: 'https://firma.example.com',
        location: null
      });
      expect(emitted.length).toBe(1);
      expect(emitted[0].name).toBe('Acme 2');
      expect(snackBarOpen).toHaveBeenCalledWith('Dane firmy zostały zapisane', 'OK', expect.anything());
      expect(component.saving()).toBe(false);
    });

    it('marks the form pristine after saving so it cannot be re-saved without changes', async () => {
      await create(makeCompany(3, { status: CompanyStatus.Approved }));
      edit('location', 'Gdańsk');

      component.onSubmit();

      expect(component.form.dirty).toBe(false);
      expect(component.canSubmit()).toBe(false);
    });

    it('resubmitting a rejected company without changes sends the current data and says it was resubmitted', async () => {
      await create(makeCompany(3, { name: 'Acme', status: CompanyStatus.Rejected, rejectionReason: 'x' }));

      submitButton().click();

      expect(updateMyCompany).toHaveBeenCalledTimes(1);
      expect(snackBarOpen).toHaveBeenCalledWith(
        'Zgłoszenie firmy zostało wysłane ponownie do weryfikacji', 'OK', expect.anything());
      expect(emitted[0].status).toBe(CompanyStatus.Pending);
    });

    it('does nothing when the form cannot be submitted', async () => {
      await create(makeCompany(3, { status: CompanyStatus.Approved }));

      component.onSubmit();

      expect(updateMyCompany).not.toHaveBeenCalled();
    });

    it('ignores a second submit while saving', async () => {
      await create(makeCompany(3, { status: CompanyStatus.Pending }));
      updateMyCompany.mockReturnValue(new Subject<Company>());
      edit('location', 'Gdańsk');

      component.onSubmit();
      component.onSubmit();

      expect(updateMyCompany).toHaveBeenCalledTimes(1);
      expect(component.saving()).toBe(true);
      expect(submitButton().disabled).toBe(true);
    });

    it('translates "Company name is already taken" and keeps the edits', async () => {
      await create(makeCompany(3, { status: CompanyStatus.Pending }));
      updateMyCompany.mockReturnValue(throwError(() => httpError(400, { message: 'Company name is already taken' })));
      edit('name', 'Zajęta');

      component.onSubmit();

      expect(snackBarOpen).toHaveBeenCalledWith('Ta nazwa firmy jest już zajęta', 'OK', expect.anything());
      expect(emitted).toEqual([]);
      expect(component.saving()).toBe(false);
      expect(component.form.get('name')!.value).toBe('Zajęta');
      expect(component.canSubmit()).toBe(true);
    });

    it('shows a generic message for a server error', async () => {
      await create(makeCompany(3, { status: CompanyStatus.Pending }));
      updateMyCompany.mockReturnValue(throwError(() => httpError(500)));
      edit('name', 'Nowa');

      component.onSubmit();

      expect(snackBarOpen).toHaveBeenCalledWith('Błąd podczas zapisywania danych firmy', 'OK', expect.anything());
    });

    it.each([401, 403])('does not show its own message when the interceptor handles %i', async status => {
      await create(makeCompany(3, { status: CompanyStatus.Pending }));
      updateMyCompany.mockReturnValue(throwError(() => httpError(status)));
      edit('name', 'Nowa');

      component.onSubmit();

      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(component.saving()).toBe(false);
    });
  });
});
