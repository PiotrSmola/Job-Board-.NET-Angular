import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject, of, throwError } from 'rxjs';
import { EmailConfirmationBannerComponent } from './email-confirmation-banner.component';
import { AuthService } from '../../services/auth.service';

vi.setConfig({ testTimeout: 20000 });

describe('EmailConfirmationBannerComponent', () => {
  let fixture: ComponentFixture<EmailConfirmationBannerComponent>;
  let resendConfirmation: ReturnType<typeof vi.fn>;
  let updateCurrentUser: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function button(): HTMLButtonElement {
    return root().querySelector<HTMLButtonElement>('button')!;
  }

  function status(): string {
    return root().querySelector('[role="status"]')?.textContent?.trim() ?? '';
  }

  beforeEach(() => {
    resendConfirmation = vi.fn();
    updateCurrentUser = vi.fn();
    snackBarOpen = vi.fn();
    TestBed.configureTestingModule({
      imports: [EmailConfirmationBannerComponent],
      providers: [
        { provide: AuthService, useValue: { resendConfirmation, updateCurrentUser } },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } }
      ]
    });
    fixture = TestBed.createComponent(EmailConfirmationBannerComponent);
    fixture.detectChanges();
  });

  it('is a labelled region with a resend button and a persistent status region', () => {
    expect(root().querySelector('section')?.getAttribute('aria-label')).toBe('Potwierdzenie adresu e-mail');
    expect(root().textContent).toContain('Potwierdź adres e-mail.');
    expect(button().textContent?.trim()).toBe('Wyślij ponownie');
    expect(status()).toContain('Kliknij link z wiadomości');
  });

  it('resends the link and announces it; the button stays focusable but inactive', () => {
    resendConfirmation.mockReturnValue(of(null));

    button().click();
    fixture.detectChanges();

    expect(resendConfirmation).toHaveBeenCalledTimes(1);
    expect(status()).toBe('Wysłaliśmy nowy link – sprawdź skrzynkę e-mail.');
    expect(button().textContent?.trim()).toBe('Wysłano');
    expect(button().getAttribute('aria-disabled')).toBe('true');
    button().click();
    expect(resendConfirmation).toHaveBeenCalledTimes(1);
  });

  it('hides itself (session update) when the e-mail turns out to be confirmed already', () => {
    resendConfirmation.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 400, error: { message: 'Email is already confirmed' }
    })));

    button().click();

    expect(updateCurrentUser).toHaveBeenCalledWith({ emailConfirmed: true });
    expect(snackBarOpen).toHaveBeenCalledWith('Adres e-mail jest już potwierdzony', 'OK', { duration: 5000 });
  });

  it.each([
    [429, 'Zbyt wiele żądań – spróbuj ponownie za kilka minut.'],
    [0, 'Brak połączenia z serwerem.'],
    [500, 'Nie udało się wysłać linku – spróbuj ponownie później.']
  ])('announces a %i error and allows retrying', (code, message) => {
    resendConfirmation.mockReturnValue(throwError(() => new HttpErrorResponse({ status: code })));

    button().click();
    fixture.detectChanges();

    expect(status()).toBe(message);
    expect(button().getAttribute('aria-disabled')).not.toBe('true');
  });

  it('cancels the request when destroyed', () => {
    const pending = new Subject<null>();
    resendConfirmation.mockReturnValue(pending);

    button().click();
    fixture.destroy();

    expect(pending.observed).toBe(false);
  });
});
