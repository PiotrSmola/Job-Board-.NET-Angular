import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { ConfirmEmailComponent } from './confirm-email.component';
import { AuthService } from '../../services/auth.service';
import { UserDto } from '../../models/user.model';

vi.setConfig({ testTimeout: 20000 });

describe('ConfirmEmailComponent', () => {
  let fixture: ComponentFixture<ConfirmEmailComponent>;
  let confirmEmail: ReturnType<typeof vi.fn>;
  let resendConfirmation: ReturnType<typeof vi.fn>;
  let updateCurrentUser: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.spyOn>;
  let user: ReturnType<typeof signal<UserDto | null>>;

  function create(params: Record<string, string> = { userId: 'u-1', token: 'SECRET' }): void {
    TestBed.overrideProvider(ActivatedRoute, { useValue: { snapshot: { queryParamMap: convertToParamMap(params) } } });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(ConfirmEmailComponent);
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function status(): string {
    return root().querySelector('[role="status"][aria-live="polite"]')?.textContent?.trim() ?? '';
  }

  beforeEach(() => {
    confirmEmail = vi.fn().mockReturnValue(of(null));
    resendConfirmation = vi.fn();
    updateCurrentUser = vi.fn();
    user = signal<UserDto | null>(null);
    TestBed.configureTestingModule({
      imports: [ConfirmEmailComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { confirmEmail, resendConfirmation, updateCurrentUser, currentUser: user } }
      ]
    });
  });

  afterEach(() => vi.restoreAllMocks());

  it('confirms automatically with userId and token and removes them from the address bar', () => {
    create();

    expect(navigate).toHaveBeenCalledWith(['/confirm-email'], { replaceUrl: true });
    expect(confirmEmail).toHaveBeenCalledWith({ userId: 'u-1', token: 'SECRET' });
    expect(status()).toBe('Adres e-mail został potwierdzony. Dziękujemy!');
    expect(root().innerHTML).not.toContain('SECRET');
    expect(root().querySelector('a[href="/login"]')).not.toBeNull();
  });

  it('shows the progress in the same status region while confirming', () => {
    confirmEmail.mockReturnValue(new Subject());
    create();

    expect(status()).toBe('Potwierdzanie adresu e-mail…');
  });

  it('announces an invalid or expired link', () => {
    confirmEmail.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 400, error: { message: 'Invalid or expired confirmation link' }
    })));
    create();

    expect(status()).toBe('Link potwierdzający jest nieprawidłowy lub wygasł');
    expect(root().textContent).toContain('Zaloguj się, aby wysłać nowy link');
  });

  it('does not call the API for an incomplete link', () => {
    create({ userId: 'u-1' });

    expect(confirmEmail).not.toHaveBeenCalled();
    expect(status()).toBe('Link potwierdzający jest nieprawidłowy lub niekompletny');
  });

  it('lets a logged-in user request a new link after an error', () => {
    user.set({ email: 'jan@example.com', firstName: 'Jan', lastName: 'K', token: 't', emailConfirmed: false });
    confirmEmail.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 400, error: { message: 'Invalid or expired confirmation link' }
    })));
    resendConfirmation.mockReturnValue(of(null));
    create();

    root().querySelector<HTMLButtonElement>('.actions button')!.click();
    fixture.detectChanges();

    expect(resendConfirmation).toHaveBeenCalled();
    expect(root().textContent).toContain('Wysłaliśmy nowy link potwierdzający');
  });

  it('treats "already confirmed" on resend as success and updates the session', () => {
    user.set({ email: 'jan@example.com', firstName: 'Jan', lastName: 'K', token: 't', emailConfirmed: false });
    confirmEmail.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    resendConfirmation.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 400, error: { message: 'Email is already confirmed' }
    })));
    create();

    root().querySelector<HTMLButtonElement>('.actions button')!.click();
    fixture.detectChanges();

    expect(updateCurrentUser).toHaveBeenCalledWith({ emailConfirmed: true });
    expect(status()).toBe('Adres e-mail został potwierdzony. Dziękujemy!');
  });

  it('cancels the confirmation request when the page is left', () => {
    const pending = new Subject<null>();
    confirmEmail.mockReturnValue(pending);
    create();

    fixture.destroy();

    expect(pending.observed).toBe(false);
  });
});
