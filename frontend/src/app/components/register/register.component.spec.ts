import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';
import { RegisterComponent } from './register.component';
import { AuthService } from '../../services/auth.service';
import { UserDto } from '../../models/user.model';

const USER: UserDto = { email: 'jan@example.com', firstName: 'Jan', lastName: 'Kowalski', token: 't' };

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('RegisterComponent', () => {
  let fixture: ComponentFixture<RegisterComponent>;
  let component: RegisterComponent;
  let register: ReturnType<typeof vi.fn>;
  let registerEmployer: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.spyOn>;

  function create(query: Record<string, string> = {}): void {
    TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { register, registerEmployer } },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } }
      ]
    });

    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(RegisterComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function fillPerson(): void {
    component.registerForm.patchValue({ email: 'jan@example.com', password: 'Secret1', firstName: 'Jan', lastName: 'Kowalski' });
  }

  function fillCompany(overrides: Record<string, string> = {}): void {
    component.registerForm.patchValue({
      companyName: 'Acme Sp. z o.o.', companyDescription: '', website: '', location: '', ...overrides
    });
  }

  function submit(): void {
    component.onSubmit();
    fixture.detectChanges();
  }

  function httpError(status: number, body: unknown = null): Observable<never> {
    return throwError(() => new HttpErrorResponse({ status, error: body }));
  }

  function errorText(): string | undefined {
    return root().querySelector('.error-message')?.textContent?.trim();
  }

  function submitButton(): HTMLButtonElement {
    return root().querySelector<HTMLButtonElement>('button[type="submit"]')!;
  }

  function toggle(label: string): HTMLButtonElement {
    const buttons = Array.from(root().querySelectorAll<HTMLButtonElement>('mat-button-toggle button'));
    return buttons.find(b => b.textContent?.includes(label))!;
  }

  function companySection(): HTMLElement | null {
    return root().querySelector<HTMLElement>('.company-section');
  }

  beforeEach(() => {
    register = vi.fn().mockReturnValue(of(USER));
    registerEmployer = vi.fn().mockReturnValue(of(USER));
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  afterEach(() => vi.restoreAllMocks());

  describe('account type', () => {
    it('defaults to the candidate form without company fields', () => {
      create();

      expect(component.accountType).toBe('candidate');
      expect(component.isEmployer).toBe(false);
      expect(companySection()).toBeNull();
      expect(submitButton().textContent).toContain('Zarejestruj się');
    });

    it('opens the employer form for ?type=employer', () => {
      create({ type: 'employer' });

      expect(component.accountType).toBe('employer');
      expect(companySection()).not.toBeNull();
      expect(submitButton().textContent).toContain('Zarejestruj firmę');
    });

    it.each(['candidate', 'admin', '', 'EMPLOYER'])('falls back to the candidate form for ?type=%j', type => {
      create({ type });

      expect(component.accountType).toBe('candidate');
    });

    it('shows both toggle options with the right one selected', () => {
      create({ type: 'employer' });

      const labels = Array.from(root().querySelectorAll('mat-button-toggle')).map(t => t.textContent?.replace(/\s+/g, ' ').trim());
      expect(labels).toEqual(['person_search Szukam pracy', 'business_center Rekrutuję']);
      expect(root().querySelector('mat-button-toggle.mat-button-toggle-checked')?.textContent).toContain('Rekrutuję');
    });

    it('the toggle switches to the employer form and back', () => {
      create();

      toggle('Rekrutuję').click();
      expect(component.accountType).toBe('employer');
      expect(companySection()).not.toBeNull();
      expect(companySection()!.textContent).toContain('Twoja firma');

      toggle('Szukam pracy').click();
      expect(component.accountType).toBe('candidate');
      expect(companySection()).toBeNull();
    });

    it('shows the company fields: name, location, website, description', () => {
      create({ type: 'employer' });

      const names = Array.from(companySection()!.querySelectorAll('[formControlName]')).map(e => e.getAttribute('formControlName'));
      expect(names).toEqual(['companyName', 'location', 'website', 'companyDescription']);
    });

    it('clears an error message when the account type changes', () => {
      registerEmployer.mockReturnValue(httpError(400, { message: 'Email is already taken' }));
      create({ type: 'employer' });
      fillPerson();
      fillCompany();
      submit();
      expect(errorText()).toBeTruthy();

      toggle('Szukam pracy').click();

      expect(errorText()).toBeUndefined();
    });
  });

  describe('validation', () => {
    it('candidate: company fields do not block the form', () => {
      create();

      fillPerson();

      expect(component.registerForm.valid).toBe(true);
      expect(submitButton().disabled).toBe(false);
    });

    it('employer: the company name is required', () => {
      create({ type: 'employer' });
      fillPerson();

      expect(component.registerForm.valid).toBe(false);
      expect(submitButton().disabled).toBe(true);

      fillCompany({ companyName: 'Acme' });
      expect(component.registerForm.valid).toBe(true);
    });

    it('employer: a whitespace-only company name is invalid', () => {
      create({ type: 'employer' });
      fillPerson();

      fillCompany({ companyName: '   ' });

      expect(component.registerForm.get('companyName')!.valid).toBe(false);
      expect(component.registerForm.valid).toBe(false);
    });

    it('employer: shows the company name error once touched', () => {
      create({ type: 'employer' });

      component.registerForm.get('companyName')!.markAsTouched();

      expect(root().textContent).toContain('Nazwa firmy jest wymagana');
    });

    it.each([['javascript:alert(1)'], ['ftp://acme.pl'], ['acme.pl']])('employer: rejects the website "%s"', website => {
      create({ type: 'employer' });
      fillPerson();

      fillCompany({ website });
      component.registerForm.get('website')!.markAsTouched();

      expect(component.registerForm.get('website')!.valid).toBe(false);
      expect(submitButton().disabled).toBe(true);
      expect(root().textContent).toContain('Podaj adres zaczynający się od http:// lub https://');
    });

    it.each([['https://acme.pl'], ['http://acme.pl/kariera'], ['']])('employer: accepts the website %j', website => {
      create({ type: 'employer' });
      fillPerson();

      fillCompany({ website });

      expect(component.registerForm.get('website')!.valid).toBe(true);
      expect(component.registerForm.valid).toBe(true);
    });

    it('employer: limits company name to 200, location to 200, description to 5000', () => {
      create({ type: 'employer' });

      fillCompany({ companyName: 'n'.repeat(201), location: 'l'.repeat(201), companyDescription: 'd'.repeat(5001) });

      expect(component.registerForm.get('companyName')!.hasError('maxlength')).toBe(true);
      expect(component.registerForm.get('location')!.hasError('maxlength')).toBe(true);
      expect(component.registerForm.get('companyDescription')!.hasError('maxlength')).toBe(true);
    });

    it('requires valid personal data in both modes (e-mail format, password >= 6)', () => {
      create({ type: 'employer' });
      fillCompany();

      component.registerForm.patchValue({ email: 'nie-email', password: '12345', firstName: 'J', lastName: 'K' });
      expect(component.registerForm.get('email')!.hasError('email')).toBe(true);
      expect(component.registerForm.get('password')!.hasError('minlength')).toBe(true);
      expect(component.registerForm.valid).toBe(false);
    });

    it('does not call the API when the form is invalid', () => {
      create({ type: 'employer' });
      fillPerson();

      submit();

      expect(registerEmployer).not.toHaveBeenCalled();
      expect(register).not.toHaveBeenCalled();
      expect(component.loading).toBe(false);
    });
  });

  describe('candidate registration', () => {
    it('calls register with the personal data only and goes to /jobs', () => {
      create();
      fillPerson();

      submit();

      expect(register).toHaveBeenCalledWith({ email: 'jan@example.com', password: 'Secret1', firstName: 'Jan', lastName: 'Kowalski' });
      expect(registerEmployer).not.toHaveBeenCalled();
      expect(navigate).toHaveBeenCalledWith(['/jobs']);
    });

    it('does not send company data even if it was typed in the employer form before switching back', () => {
      create({ type: 'employer' });
      fillCompany({ companyName: 'Acme' });
      toggle('Szukam pracy').click();
      fillPerson();

      submit();

      expect(register).toHaveBeenCalledWith({ email: 'jan@example.com', password: 'Secret1', firstName: 'Jan', lastName: 'Kowalski' });
      expect(registerEmployer).not.toHaveBeenCalled();
    });
  });

  describe('employer registration', () => {
    it('calls registerEmployer with person and trimmed company data and goes to /employer', () => {
      create({ type: 'employer' });
      fillPerson();
      fillCompany({
        companyName: '  Acme Sp. z o.o.  ', companyDescription: '  Robimy rzeczy  ',
        website: 'https://acme.pl', location: ' Rzeszów '
      });

      submit();

      expect(registerEmployer).toHaveBeenCalledWith({
        email: 'jan@example.com',
        password: 'Secret1',
        firstName: 'Jan',
        lastName: 'Kowalski',
        companyName: 'Acme Sp. z o.o.',
        companyDescription: 'Robimy rzeczy',
        website: 'https://acme.pl',
        location: 'Rzeszów'
      });
      expect(register).not.toHaveBeenCalled();
      expect(navigate).toHaveBeenCalledWith(['/employer']);
    });

    it('sends null for blank optional company fields', () => {
      create({ type: 'employer' });
      fillPerson();
      fillCompany({ companyName: 'Acme', companyDescription: '   ', website: '', location: '' });

      submit();

      expect(registerEmployer).toHaveBeenCalledWith(expect.objectContaining({
        companyName: 'Acme', companyDescription: null, website: null, location: null
      }));
    });

    it('works after switching from the candidate form via the toggle', () => {
      create();
      toggle('Rekrutuję').click();
      fillPerson();
      fillCompany({ companyName: 'Acme' });

      submit();

      expect(registerEmployer).toHaveBeenCalledTimes(1);
      expect(navigate).toHaveBeenCalledWith(['/employer']);
    });

    it('submits through the form (button click)', () => {
      create({ type: 'employer' });
      fillPerson();
      fillCompany();

      submitButton().click();

      expect(registerEmployer).toHaveBeenCalledTimes(1);
    });

    it('shows a spinner and disables the button while the request is in flight', () => {
      registerEmployer.mockReturnValue(new Subject<UserDto>());
      create({ type: 'employer' });
      fillPerson();
      fillCompany();

      submit();

      expect(component.loading).toBe(true);
      expect(submitButton().disabled).toBe(true);
      expect(root().querySelector('button[type="submit"] mat-spinner')).not.toBeNull();
      expect(navigate).not.toHaveBeenCalled();
    });
  });

  describe('errors', () => {
    it('translates "Company name is already taken" and stays on the page', () => {
      registerEmployer.mockReturnValue(httpError(400, { message: 'Company name is already taken' }));
      create({ type: 'employer' });
      fillPerson();
      fillCompany();

      submit();

      expect(errorText()).toBe('Ta nazwa firmy jest już zajęta');
      expect(component.loading).toBe(false);
      expect(navigate).not.toHaveBeenCalled();
      expect(submitButton().disabled).toBe(false);
    });

    it('translates "Email is already taken"', () => {
      registerEmployer.mockReturnValue(httpError(400, { message: 'Email is already taken' }));
      create({ type: 'employer' });
      fillPerson();
      fillCompany();

      submit();

      expect(errorText()).toBe('Ten adres email jest już zajęty');
    });

    it('shows a generic validation message for ValidationProblemDetails (400 with errors)', () => {
      registerEmployer.mockReturnValue(httpError(400, { errors: { CompanyName: ['required'] } }));
      create({ type: 'employer' });
      fillPerson();
      fillCompany();

      submit();

      expect(errorText()).toBe('Przesłane dane są nieprawidłowe');
    });

    it('shows the fallback for a server error', () => {
      registerEmployer.mockReturnValue(httpError(500));
      create({ type: 'employer' });
      fillPerson();
      fillCompany();

      submit();

      expect(errorText()).toBe('Błąd podczas rejestracji');
    });

    it('shows a connection message when the server is unreachable', () => {
      registerEmployer.mockReturnValue(httpError(0));
      create({ type: 'employer' });
      fillPerson();
      fillCompany();

      submit();

      expect(errorText()).toBe('Brak połączenia z serwerem');
    });

    it('shows the API message for a failed candidate registration too', () => {
      register.mockReturnValue(httpError(400, { message: 'Email is already taken' }));
      create();
      fillPerson();

      submit();

      expect(errorText()).toBe('Ten adres email jest już zajęty');
      expect(navigate).not.toHaveBeenCalled();
    });

    it('allows retrying after an error', () => {
      registerEmployer.mockReturnValueOnce(httpError(400, { message: 'Company name is already taken' }));
      create({ type: 'employer' });
      fillPerson();
      fillCompany();
      submit();

      fillCompany({ companyName: 'Inna firma' });
      submit();

      expect(registerEmployer).toHaveBeenCalledTimes(2);
      expect(registerEmployer).toHaveBeenLastCalledWith(expect.objectContaining({ companyName: 'Inna firma' }));
      expect(navigate).toHaveBeenCalledWith(['/employer']);
      expect(errorText()).toBeUndefined();
    });
  });
});
