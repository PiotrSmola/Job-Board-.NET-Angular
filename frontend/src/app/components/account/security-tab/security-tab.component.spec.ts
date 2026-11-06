import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject, of, throwError } from 'rxjs';
import { SecurityTabComponent } from './security-tab.component';
import { AccountService } from '../../../services/account.service';
import { AuthService } from '../../../services/auth.service';

vi.setConfig({ testTimeout: 20000 });

describe('SecurityTabComponent', () => {
  let fixture: ComponentFixture<SecurityTabComponent>;
  let changePassword: ReturnType<typeof vi.fn>;
  let deleteAccount: ReturnType<typeof vi.fn>;
  let endSession: ReturnType<typeof vi.fn>;
  let admin: boolean;

  function create(): void {
    fixture = TestBed.createComponent(SecurityTabComponent);
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function field(section: string, name: string): HTMLInputElement {
    return root().querySelector<HTMLInputElement>(`${section} [formControlName="${name}"]`)!;
  }

  function type(section: string, name: string, value: string): void {
    const element = field(section, name);
    element.value = value;
    element.dispatchEvent(new Event('input'));
    element.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
  }

  function submit(section: string): void {
    root().querySelector<HTMLFormElement>(`${section} form`)!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  }

  function fillPasswordForm(current = 'Stare123', next = 'Nowe1234', confirm = next): void {
    type('.password-section', 'currentPassword', current);
    type('.password-section', 'newPassword', next);
    type('.password-section', 'confirmPassword', confirm);
  }

  function badRequest(error: unknown): HttpErrorResponse {
    return new HttpErrorResponse({ status: 400, statusText: 'Bad Request', error });
  }

  beforeEach(() => {
    changePassword = vi.fn();
    deleteAccount = vi.fn();
    endSession = vi.fn();
    admin = false;
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    TestBed.configureTestingModule({
      imports: [SecurityTabComponent],
      providers: [
        { provide: AccountService, useValue: { changePassword, deleteAccount } },
        {
          provide: AuthService,
          useValue: { isAdmin: () => admin, currentUser: () => ({ email: 'jan@example.com' }), endSession }
        }
      ]
    });
  });

  afterEach(() => vi.restoreAllMocks());

  describe('change password', () => {
    it('uses the right autocomplete tokens and links the strength indicator to the new password', () => {
      create();

      expect(field('.password-section', 'currentPassword').getAttribute('autocomplete')).toBe('current-password');
      expect(field('.password-section', 'newPassword').getAttribute('autocomplete')).toBe('new-password');
      expect(field('.password-section', 'confirmPassword').getAttribute('autocomplete')).toBe('new-password');
      expect(field('.password-section', 'newPassword').getAttribute('aria-describedby')).toContain('change-password-strength');
      expect(root().querySelector('#change-password-strength')?.textContent).toContain('Siła hasła');
      expect(root().querySelector<HTMLInputElement>('input[autocomplete="username"]')?.value).toBe('jan@example.com');
    });

    it('labels the show-password toggles', () => {
      create();
      const toggle = root().querySelector<HTMLButtonElement>('.password-section button[aria-label="Pokaż obecne hasło"]')!;

      toggle.click();
      fixture.detectChanges();

      expect(field('.password-section', 'currentPassword').type).toBe('text');
      expect(toggle.getAttribute('aria-label')).toBe('Ukryj obecne hasło');
      expect(toggle.getAttribute('aria-pressed')).toBe('true');
    });

    it('sends the passwords, clears the form and announces the success (the new token is stored by the service)', () => {
      create();
      changePassword.mockReturnValue(of({ token: 'new' }));

      fillPasswordForm();
      submit('.password-section');

      expect(changePassword).toHaveBeenCalledWith({ currentPassword: 'Stare123', newPassword: 'Nowe1234' });
      expect(field('.password-section', 'currentPassword').value).toBe('');
      expect(field('.password-section', 'newPassword').value).toBe('');
      expect(root().querySelector('.password-section .success[role="status"]')?.textContent).toContain('Hasło zostało zmienione');
    });

    it('validates the password policy on the client', () => {
      create();

      fillPasswordForm('Stare123', 'slabe');
      submit('.password-section');

      expect(changePassword).not.toHaveBeenCalled();
      expect(root().textContent).toContain('małą i wielką literę oraz cyfrę');
      expect(document.activeElement).toBe(field('.password-section', 'newPassword'));
    });

    it('shows the mismatch next to the confirmation field', () => {
      create();

      fillPasswordForm('Stare123', 'Nowe1234', 'Nowe12345');
      submit('.password-section');

      expect(changePassword).not.toHaveBeenCalled();
      expect(root().querySelector('.password-section .error[role="alert"]')?.textContent).toBe('Nowe hasła nie są takie same');
      const confirmField = field('.password-section', 'confirmPassword').closest('mat-form-field')!;
      expect(confirmField.textContent).toContain('Hasła nie są takie same');
      expect(field('.password-section', 'confirmPassword').getAttribute('aria-invalid')).toBe('true');
    });

    it('clears only the current password when it is wrong', () => {
      create();
      changePassword.mockReturnValue(throwError(() => badRequest({ message: 'Current password is incorrect' })));

      fillPasswordForm();
      submit('.password-section');

      expect(field('.password-section', 'currentPassword').value).toBe('');
      expect(field('.password-section', 'newPassword').value).toBe('Nowe1234');
      expect(root().querySelector('.password-section .error[role="alert"]')?.textContent).toBe('Obecne hasło jest nieprawidłowe');
    });

    it('translates Identity errors for a weak new password', () => {
      create();
      changePassword.mockReturnValue(throwError(() => badRequest({
        errors: { NewPassword: ['Passwords must have at least one non alphanumeric character.'] }
      })));

      fillPasswordForm();
      submit('.password-section');

      expect(root().querySelector('.password-section .error[role="alert"]')?.textContent).toBe('Hasło musi zawierać znak specjalny');
    });

    it('lets the request finish after leaving the page (the new token must still be stored by the service)', () => {
      create();
      const pending = new Subject<unknown>();
      changePassword.mockReturnValue(pending);

      fillPasswordForm();
      submit('.password-section');
      fixture.destroy();

      expect(pending.observed).toBe(true);
      pending.next({ token: 'new' });
      pending.complete();
      expect(pending.observed).toBe(false);
    });
  });

  describe('delete account', () => {
    it('requires the password and the acknowledgement', () => {
      create();

      submit('.delete-section');

      expect(deleteAccount).not.toHaveBeenCalled();
      expect(root().querySelector('.delete-section .error[role="alert"]')?.textContent)
        .toBe('Podaj hasło i potwierdź, że rozumiesz skutki usunięcia konta');
      expect(document.activeElement).toBe(field('.delete-section', 'password'));
    });

    it('deletes the account and ends the session with a message', () => {
      create();
      deleteAccount.mockReturnValue(of(undefined));

      type('.delete-section', 'password', 'Haslo123');
      root().querySelector<HTMLInputElement>('.delete-section input[type="checkbox"]')!.click();
      fixture.detectChanges();
      submit('.delete-section');

      expect(deleteAccount).toHaveBeenCalledWith('Haslo123');
      expect(endSession).toHaveBeenCalledWith('Konto zostało usunięte. Dziękujemy za korzystanie z Job Board.');
    });

    it.each([
      ['Current password is incorrect', 'Obecne hasło jest nieprawidłowe'],
      ['You are the last member of a company with job offers', 'Jesteś ostatnim członkiem firmy, która ma oferty pracy – nie możesz teraz usunąć konta']
    ])('announces "%s", clears the password and keeps the session', (message, expected) => {
      create();
      deleteAccount.mockReturnValue(throwError(() => badRequest({ message })));

      type('.delete-section', 'password', 'Haslo123');
      root().querySelector<HTMLInputElement>('.delete-section input[type="checkbox"]')!.click();
      fixture.detectChanges();
      submit('.delete-section');

      expect(root().querySelector('.delete-section .error[role="alert"]')?.textContent).toBe(expected);
      expect(field('.delete-section', 'password').value).toBe('');
      expect(endSession).not.toHaveBeenCalled();
    });

    it('ends the session even when the page was left while deleting', () => {
      create();
      const pending = new Subject<void>();
      deleteAccount.mockReturnValue(pending);

      type('.delete-section', 'password', 'Haslo123');
      root().querySelector<HTMLInputElement>('.delete-section input[type="checkbox"]')!.click();
      fixture.detectChanges();
      submit('.delete-section');
      fixture.destroy();
      pending.next();
      pending.complete();

      expect(endSession).toHaveBeenCalled();
    });

    it('does not offer deleting an administrator account', () => {
      admin = true;
      create();

      expect(root().querySelector('.delete-section form')).toBeNull();
      expect(root().querySelector('.delete-section')?.textContent).toContain('Konta administratora nie można usunąć');
    });
  });
});
