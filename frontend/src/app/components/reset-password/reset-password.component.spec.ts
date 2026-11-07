import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject, of, throwError } from 'rxjs';
import { ResetPasswordComponent } from './reset-password.component';
import { AuthService } from '../../services/auth.service';

vi.setConfig({ testTimeout: 20000 });

describe('ResetPasswordComponent', () => {
  let fixture: ComponentFixture<ResetPasswordComponent>;
  let resetPassword: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.spyOn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;
  let consoleSpies: ReturnType<typeof vi.spyOn>[];

  function create(params: Record<string, string>): void {
    TestBed.overrideProvider(ActivatedRoute, { useValue: { snapshot: { queryParamMap: convertToParamMap(params) } } });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(ResetPasswordComponent);
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function type(name: string, value: string): void {
    const input = root().querySelector<HTMLInputElement>(`[formControlName="${name}"]`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('blur'));
  }

  function submit(): void {
    root().querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  }

  beforeEach(() => {
    resetPassword = vi.fn();
    snackBarOpen = vi.fn();
    consoleSpies = (['log', 'info', 'warn', 'error', 'debug'] as const).map(m => vi.spyOn(console, m).mockImplementation(() => undefined));
    TestBed.configureTestingModule({
      imports: [ResetPasswordComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { resetPassword } },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } }
      ]
    });
  });

  afterEach(() => vi.restoreAllMocks());

  it('removes the token from the address bar and history right away', () => {
    create({ email: 'jan@example.com', token: 'SECRET+TOKEN' });

    expect(navigate).toHaveBeenCalledWith(['/reset-password'], { replaceUrl: true });
    expect(root().textContent).not.toContain('SECRET+TOKEN');
    expect(root().innerHTML).not.toContain('SECRET+TOKEN');
  });

  it('offers only new-password fields (no current password to autofill) plus a hidden username for password managers', () => {
    create({ email: 'jan@example.com', token: 't' });

    const passwords = Array.from(root().querySelectorAll<HTMLInputElement>('input[type="password"]'));
    expect(passwords.map(p => p.getAttribute('autocomplete'))).toEqual(['new-password', 'new-password']);
    expect(root().querySelector('[autocomplete="current-password"]')).toBeNull();
    expect(root().querySelector<HTMLInputElement>('[autocomplete="username"]')?.value).toBe('jan@example.com');
    expect(root().textContent).toContain('jan@example.com');
  });

  it('sends e-mail, token and the new password, then goes to the login page', () => {
    create({ email: 'jan@example.com', token: 'tok+/=' });
    resetPassword.mockReturnValue(of(null));
    navigate.mockClear();

    type('newPassword', 'Nowe1234');
    type('confirmPassword', 'Nowe1234');
    submit();

    expect(resetPassword).toHaveBeenCalledWith({ email: 'jan@example.com', token: 'tok+/=', newPassword: 'Nowe1234' });
    expect(snackBarOpen).toHaveBeenCalledWith('Hasło zostało zmienione – zaloguj się nowym hasłem', 'OK', { duration: 8000, politeness: 'polite' });
    expect(navigate).toHaveBeenCalledWith(['/login']);
  });

  it('never logs the token', () => {
    create({ email: 'jan@example.com', token: 'SECRET' });
    resetPassword.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    type('newPassword', 'Nowe1234');
    type('confirmPassword', 'Nowe1234');
    submit();

    for (const spy of consoleSpies) {
      expect(JSON.stringify(spy.mock.calls)).not.toContain('SECRET');
    }
  });

  it('validates the policy and the confirmation on the client', () => {
    create({ email: 'jan@example.com', token: 't' });

    type('newPassword', 'Nowe1234');
    type('confirmPassword', 'Inne1234');
    submit();

    expect(resetPassword).not.toHaveBeenCalled();
    expect(root().querySelector('mat-error')?.textContent?.trim()).toBe('Hasła nie są takie same');
    expect(document.activeElement).toBe(root().querySelector('[formControlName="confirmPassword"]'));
  });

  it('shows the strength indicator linked to the new password field', () => {
    create({ email: 'jan@example.com', token: 't' });

    type('newPassword', 'Abcdefgh12!');
    fixture.detectChanges();

    const input = root().querySelector('[formControlName="newPassword"]')!;
    expect(input.getAttribute('aria-describedby')).toContain('reset-password-strength');
    expect(root().querySelector('#reset-password-strength')?.textContent).toContain('Siła hasła: silna');
  });

  it('switches to the "request a new link" state for an invalid or expired link', () => {
    create({ email: 'jan@example.com', token: 't' });
    resetPassword.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 400, error: { message: 'Invalid or expired reset link' }
    })));

    type('newPassword', 'Nowe1234');
    type('confirmPassword', 'Nowe1234');
    submit();

    expect(root().querySelector('form')).toBeNull();
    expect(root().querySelector('[role="alert"]')?.textContent).toContain('Link do zmiany hasła jest nieprawidłowy lub wygasł');
    expect(root().querySelector('a[href="/forgot-password"]')).not.toBeNull();
  });

  it('translates Identity errors for a weak password and keeps the form', () => {
    create({ email: 'jan@example.com', token: 't' });
    resetPassword.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 400, error: { errors: { NewPassword: ["Passwords must have at least one uppercase ('A'-'Z')."] } }
    })));

    type('newPassword', 'Nowe1234');
    type('confirmPassword', 'Nowe1234');
    submit();

    expect(root().querySelector('[role="alert"] .auth-alert')?.textContent?.trim()).toBe('Hasło musi zawierać wielką literę');
    expect(root().querySelector('form')).not.toBeNull();
  });

  it.each([
    [{}],
    [{ email: 'jan@example.com' }],
    [{ token: 't' }],
    [{ email: 'bez-malpy', token: 't' }]
  ])('treats an incomplete link %j as invalid', params => {
    create(params);

    expect(root().querySelector('form')).toBeNull();
    expect(root().textContent).toContain('nieprawidłowy lub niekompletny');
  });

  it('cancels the request when the page is left', () => {
    create({ email: 'jan@example.com', token: 't' });
    const pending = new Subject<null>();
    resetPassword.mockReturnValue(pending);

    type('newPassword', 'Nowe1234');
    type('confirmPassword', 'Nowe1234');
    submit();
    fixture.destroy();

    expect(pending.observed).toBe(false);
  });
});
