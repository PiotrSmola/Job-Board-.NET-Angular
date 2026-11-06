import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { LoginComponent } from './login.component';
import { AuthService } from '../../services/auth.service';
import { UserDto } from '../../models/user.model';

const LOCKED_MESSAGE =
  'Konto zostało tymczasowo zablokowane po zbyt wielu nieudanych próbach. Spróbuj ponownie za kilka minut.';
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

  function fillAndSubmit(email = 'jan@example.com', password = 'Secret123'): void {
    component.loginForm.setValue({ email, password });
    component.onSubmit();
    fixture.detectChanges();
  }

  function httpError(status: number, body: unknown = null): Observable<never> {
    return throwError(() => new HttpErrorResponse({ status, error: body }));
  }

  function errorText(): string | undefined {
    return (fixture.nativeElement as HTMLElement).querySelector('.error-message')?.textContent?.trim();
  }

  beforeEach(() => {
    login = vi.fn();
    isEmployer = vi.fn().mockReturnValue(false);
  });

  it('does not call the API when the form is invalid', () => {
    create();

    fillAndSubmit('not-an-email', '123');

    expect(login).not.toHaveBeenCalled();
    expect(component.loading).toBe(false);
  });

  it('sends the entered credentials', () => {
    login.mockReturnValue(of(USER));
    create();

    fillAndSubmit('jan@example.com', 'Secret123');

    expect(login).toHaveBeenCalledWith({ email: 'jan@example.com', password: 'Secret123' });
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

      expect(component.errorMessage).toBe(LOCKED_MESSAGE);
      expect(errorText()).toBe(LOCKED_MESSAGE);
      expect(component.loading).toBe(false);
      expect(navigate).not.toHaveBeenCalled();
      expect(navigateByUrl).not.toHaveBeenCalled();
    });

    it('shows the server message for 401', () => {
      login.mockReturnValue(httpError(401, { message: 'Invalid email or password' }));
      create();

      fillAndSubmit();

      expect(errorText()).toBe('Invalid email or password');
      expect(component.loading).toBe(false);
    });

    it('shows the default message for 401 without a body', () => {
      login.mockReturnValue(httpError(401));
      create();

      fillAndSubmit();

      expect(errorText()).toBe('Nieprawidłowy email lub hasło');
    });

    it('shows the default message for a network error', () => {
      login.mockReturnValue(httpError(0));
      create();

      fillAndSubmit();

      expect(errorText()).toBe('Nieprawidłowy email lub hasło');
    });

    it('clears the previous error on the next attempt', () => {
      login.mockReturnValueOnce(httpError(423)).mockReturnValueOnce(of(USER));
      create();

      fillAndSubmit();
      expect(component.errorMessage).toBe(LOCKED_MESSAGE);

      fillAndSubmit();
      expect(component.errorMessage).toBe('');
      expect(navigate).toHaveBeenCalledWith(['/jobs']);
    });
  });

  it('links to the password reset page and uses autocomplete hints', () => {
    create();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('a[href="/forgot-password"]')?.textContent?.trim()).toBe('Nie pamiętasz hasła?');
    expect(root.querySelector('input[formControlName="password"]')?.getAttribute('autocomplete')).toBe('current-password');
    expect(root.querySelector('input[formControlName="email"]')?.getAttribute('autocomplete')).toBe('email');
  });
});
