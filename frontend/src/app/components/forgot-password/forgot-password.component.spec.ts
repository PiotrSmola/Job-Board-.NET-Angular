import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { FORGOT_PASSWORD_SENT_MESSAGE, ForgotPasswordComponent } from './forgot-password.component';
import { AuthService } from '../../services/auth.service';

vi.setConfig({ testTimeout: 20000 });

describe('ForgotPasswordComponent', () => {
  let fixture: ComponentFixture<ForgotPasswordComponent>;
  let forgotPassword: ReturnType<typeof vi.fn>;

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function email(): HTMLInputElement {
    return root().querySelector<HTMLInputElement>('input[formControlName="email"]')!;
  }

  function typeAndSubmit(value: string): void {
    email().value = value;
    email().dispatchEvent(new Event('input'));
    root().querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  }

  beforeEach(() => {
    forgotPassword = vi.fn();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    TestBed.configureTestingModule({
      imports: [ForgotPasswordComponent],
      providers: [provideRouter([]), { provide: AuthService, useValue: { forgotPassword } }]
    });
    fixture = TestBed.createComponent(ForgotPasswordComponent);
    fixture.detectChanges();
  });

  afterEach(() => vi.restoreAllMocks());

  it('has an e-mail field with autocomplete and a persistent status region', () => {
    expect(email().getAttribute('autocomplete')).toBe('email');
    expect(email().type).toBe('email');
    expect(root().querySelector('[role="status"]')).not.toBeNull();
  });

  it('sends the trimmed address and shows the same neutral message regardless of the account (202)', async () => {
    forgotPassword.mockReturnValue(of(null));

    typeAndSubmit('  jan@example.com ');
    await fixture.whenStable();

    expect(forgotPassword).toHaveBeenCalledWith('jan@example.com');
    const status = root().querySelector<HTMLElement>('[role="status"]')!;
    expect(status.textContent).toContain(FORGOT_PASSWORD_SENT_MESSAGE);
    expect(FORGOT_PASSWORD_SENT_MESSAGE).toContain('Jeśli konto z tym adresem istnieje');
    expect(root().querySelector('form')).toBeNull();
    // fokus nie ginie po zniknięciu formularza
    expect(document.activeElement).toBe(status);
  });

  it('lets the user send the link again', () => {
    forgotPassword.mockReturnValue(of(null));
    typeAndSubmit('jan@example.com');

    Array.from(root().querySelectorAll('button')).find(b => b.textContent?.includes('Wyślij ponownie'))!.click();
    fixture.detectChanges();

    expect(email().value).toBe('jan@example.com');
  });

  it('validates the address on the client and moves focus to it', () => {
    typeAndSubmit('nie-email');

    expect(forgotPassword).not.toHaveBeenCalled();
    expect(root().querySelector('.error[role="alert"]')?.textContent).toBe('Podaj poprawny adres e-mail');
    expect(document.activeElement).toBe(email());
  });

  it('announces rate limiting', () => {
    forgotPassword.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 429 })));

    typeAndSubmit('jan@example.com');

    expect(root().querySelector('.error[role="alert"]')?.textContent).toBe('Zbyt wiele żądań – spróbuj ponownie za kilka minut');
    expect(root().querySelector('form')).not.toBeNull();
  });

  it('cancels the request when the page is left', () => {
    const pending = new Subject<null>();
    forgotPassword.mockReturnValue(pending);

    typeAndSubmit('jan@example.com');
    fixture.destroy();

    expect(pending.observed).toBe(false);
  });
});
