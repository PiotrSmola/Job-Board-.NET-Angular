import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { INVALID_CREDENTIALS_MESSAGE, LOCKED_OUT_MESSAGE, LoginComponent, getDemoAccounts } from './login.component';
import { AuthService } from '../../services/auth.service';
import { UserDto } from '../../models/user.model';

const USER: UserDto = { email: 'jan@example.com', firstName: 'Jan', lastName: 'Kowalski', token: 't' };

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let component: LoginComponent;
  let login: ReturnType<typeof vi.fn>;
  let isEmployer: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.spyOn>;
  let navigateByUrl: ReturnType<typeof vi.spyOn>;

  function create(returnUrl: string | null = null): void {
    TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { login, isEmployer } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(returnUrl === null ? {} : { returnUrl }) } }
        }
      ]
    });

    const router = TestBed.inject(Router);
    navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    navigateByUrl = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function fillAndSubmit(email = 'jan@example.com', password = 'Secret123'): void {
    component.loginForm.setValue({ email, password });
    component.onSubmit();
    fixture.detectChanges();
  }

  function httpError(status: number, body: unknown = null): Observable<never> {
    return throwError(() => new HttpErrorResponse({ status, error: body }));
  }

  function errorText(): string | undefined {
    return root().querySelector('[role="alert"] .auth-alert')?.textContent?.trim();
  }

  beforeEach(() => {
    login = vi.fn();
    isEmployer = vi.fn().mockReturnValue(false);
  });

  it('does not call the API when the form is invalid and shows the field errors after the attempt', () => {
    create();
    const submit = root().querySelector<HTMLButtonElement>('button[type="submit"]')!;
    // przycisk aktywny także dla pustego formularza – błędy dopiero po próbie
    expect(submit.disabled).toBe(false);
    expect(root().querySelectorAll('mat-error').length).toBe(0);

    fillAndSubmit('not-an-email', '');

    expect(login).not.toHaveBeenCalled();
    expect(component.loading()).toBe(false);
    const errors = Array.from(root().querySelectorAll('mat-error')).map(e => e.textContent?.trim());
    expect(errors).toEqual(['Nieprawidłowy adres e-mail', 'Podaj hasło']);
    expect(document.activeElement).toBe(root().querySelector('input[formControlName="email"]'));
  });

  it('sends the entered credentials', () => {
    login.mockReturnValue(of(USER));
    create();

    fillAndSubmit('jan@example.com', 'Secret123');

    expect(login).toHaveBeenCalledWith({ email: 'jan@example.com', password: 'Secret123' });
  });

  it('keeps the submit button focusable while the request is pending', () => {
    login.mockReturnValue(new Observable<UserDto>());
    create();

    fillAndSubmit();

    const submit = root().querySelector<HTMLButtonElement>('button[type="submit"]')!;
    expect(component.loading()).toBe(true);
    expect(submit.disabled).toBe(false);
    expect(submit.getAttribute('aria-disabled')).toBe('true');
    expect(submit.textContent).toContain('Logowanie…');

    component.onSubmit();
    expect(login).toHaveBeenCalledTimes(1);
  });

  describe('successful login', () => {
    it('navigates to /jobs when there is no returnUrl', () => {
      login.mockReturnValue(of(USER));
      create();

      fillAndSubmit();

      expect(navigate).toHaveBeenCalledWith(['/jobs']);
      expect(navigateByUrl).not.toHaveBeenCalled();
    });

    it('navigates an employer to /employer when there is no returnUrl', () => {
      login.mockReturnValue(of(USER));
      isEmployer.mockReturnValue(true);
      create();

      fillAndSubmit();

      expect(navigate).toHaveBeenCalledWith(['/employer']);
      expect(navigateByUrl).not.toHaveBeenCalled();
    });

    it('navigates an employer to a safe returnUrl when present', () => {
      login.mockReturnValue(of(USER));
      isEmployer.mockReturnValue(true);
      create('/companies/3');

      fillAndSubmit();

      expect(navigateByUrl).toHaveBeenCalledWith('/companies/3');
      expect(navigate).not.toHaveBeenCalled();
    });

    it('falls back to /employer for an employer with an unsafe returnUrl', () => {
      login.mockReturnValue(of(USER));
      isEmployer.mockReturnValue(true);
      create('//evil.com');

      fillAndSubmit();

      expect(navigateByUrl).not.toHaveBeenCalled();
      expect(navigate).toHaveBeenCalledWith(['/employer']);
    });

    it('navigates to a safe returnUrl including its query string', () => {
      login.mockReturnValue(of(USER));
      create('/jobs/1?x=1');

      fillAndSubmit();

      expect(navigateByUrl).toHaveBeenCalledWith('/jobs/1?x=1');
      expect(navigate).not.toHaveBeenCalled();
    });

    it.each(['//evil.com', '/\\evil.com', 'https://evil.com', '/login', '/register', 'jobs'])(
      'falls back to /jobs for unsafe returnUrl %s',
      returnUrl => {
        login.mockReturnValue(of(USER));
        create(returnUrl);

        fillAndSubmit();

        expect(navigateByUrl).not.toHaveBeenCalled();
        expect(navigate).toHaveBeenCalledWith(['/jobs']);
      }
    );
  });

  describe('failed login', () => {
    it('shows the lockout message for 423', () => {
      login.mockReturnValue(httpError(423, { message: 'Locked' }));
      create();

      fillAndSubmit();

      expect(component.errorMessage()).toBe(LOCKED_OUT_MESSAGE);
      expect(errorText()).toBe(LOCKED_OUT_MESSAGE);
      expect(component.loading()).toBe(false);
      expect(navigate).not.toHaveBeenCalled();
      expect(navigateByUrl).not.toHaveBeenCalled();
    });

    it('shows a Polish message for 401 and clears the password', () => {
      login.mockReturnValue(httpError(401, { message: 'Invalid email or password' }));
      create();

      fillAndSubmit();

      expect(errorText()).toBe(INVALID_CREDENTIALS_MESSAGE);
      expect(component.loginForm.controls.password.value).toBe('');
      expect(component.loginForm.controls.email.value).toBe('jan@example.com');
      expect(component.loading()).toBe(false);
    });

    it('shows the same message for 401 without a body', () => {
      login.mockReturnValue(httpError(401));
      create();

      fillAndSubmit();

      expect(errorText()).toBe(INVALID_CREDENTIALS_MESSAGE);
    });

    it('reports a network error as such', () => {
      login.mockReturnValue(httpError(0));
      create();

      fillAndSubmit();

      expect(errorText()).toBe('Brak połączenia z serwerem');
    });

    it('clears the previous error on the next attempt', () => {
      login.mockReturnValueOnce(httpError(423)).mockReturnValueOnce(of(USER));
      create();

      fillAndSubmit();
      expect(component.errorMessage()).toBe(LOCKED_OUT_MESSAGE);

      fillAndSubmit();
      expect(component.errorMessage()).toBe('');
      expect(errorText()).toBeUndefined();
      expect(navigate).toHaveBeenCalledWith(['/jobs']);
    });
  });

  it('has a page heading, links and autocomplete hints', () => {
    create();

    expect(root().querySelector('h1')?.textContent?.trim()).toBe('Zaloguj się');
    expect(root().querySelector('a[href="/forgot-password"]')?.textContent?.trim()).toBe('Nie pamiętasz hasła?');
    expect(root().querySelector('a[href="/register"]')).not.toBeNull();
    expect(root().querySelector('input[formControlName="password"]')?.getAttribute('autocomplete')).toBe('current-password');
    expect(root().querySelector('input[formControlName="email"]')?.getAttribute('autocomplete')).toBe('username');
  });

  it('toggles the password visibility with a labelled toggle button', () => {
    create();
    const toggle = root().querySelector<HTMLButtonElement>('button.password-toggle')!;
    const input = root().querySelector<HTMLInputElement>('input[formControlName="password"]')!;

    expect(toggle.getAttribute('aria-label')).toBe('Pokaż hasło');
    expect(toggle.getAttribute('aria-pressed')).toBe('false');
    expect(input.type).toBe('password');

    toggle.click();
    fixture.detectChanges();

    expect(toggle.getAttribute('aria-pressed')).toBe('true');
    expect(input.type).toBe('text');
  });

  it('lists the demo accounts in a collapsed details element', () => {
    create();
    const details = root().querySelector('details.demo-accounts') as HTMLDetailsElement;

    expect(details.open).toBe(false);
    expect(details.querySelector('summary')?.textContent?.trim()).toBe('Konta demonstracyjne');
    expect(details.textContent).toContain('jan.kowalski@example.com / User123!');
  });

  it('shows the development admin password only in dev mode', () => {
    expect(getDemoAccounts(false).map(a => a.role)).toEqual(['Kandydat', 'Pracodawca']);
    expect(getDemoAccounts(true).map(a => a.email)).toContain('admin@jobboard.com');
  });
});
