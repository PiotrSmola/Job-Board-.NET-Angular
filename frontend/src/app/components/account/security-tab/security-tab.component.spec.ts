import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject, of, throwError } from 'rxjs';
import { SecurityTabComponent } from './security-tab.component';
import { AccountService } from '../../../services/account.service';
import { AuthService } from '../../../services/auth.service';
import { ConfirmService } from '../../../services/confirm.service';

vi.setConfig({ testTimeout: 20000 });

const PASSWORD_FORM = '.password-form';
const DELETE_FORM = '.delete-form';

describe('SecurityTabComponent', () => {
  let fixture: ComponentFixture<SecurityTabComponent>;
  let changePassword: ReturnType<typeof vi.fn>;
  let deleteAccount: ReturnType<typeof vi.fn>;
  let endSession: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;
  let confirm: ReturnType<typeof vi.fn>;
  let admin: boolean;

  function create(): void {
    fixture = TestBed.createComponent(SecurityTabComponent);
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function field(form: string, name: string): HTMLInputElement {
    return root().querySelector<HTMLInputElement>(`${form} [formControlName="${name}"]`)!;
  }

  function type(form: string, name: string, value: string): void {
    const element = field(form, name);
    element.value = value;
    element.dispatchEvent(new Event('input'));
    element.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
  }

  function submit(form: string): void {
    root().querySelector<HTMLFormElement>(form)!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  }

  function fillPasswordForm(current = 'Stare123', next = 'Nowe1234', confirmation = next): void {
    type(PASSWORD_FORM, 'currentPassword', current);
    type(PASSWORD_FORM, 'newPassword', next);
    type(PASSWORD_FORM, 'confirmPassword', confirmation);
  }

  function deleteToggle(): HTMLButtonElement {
    return root().querySelector<HTMLButtonElement>('button.delete-toggle')!;
  }

  function openDeleteForm(): void {
    deleteToggle().click();
    fixture.detectChanges();
  }

  function badRequest(error: unknown): HttpErrorResponse {
    return new HttpErrorResponse({ status: 400, statusText: 'Bad Request', error });
  }

  beforeEach(() => {
    changePassword = vi.fn();
    deleteAccount = vi.fn();
    endSession = vi.fn();
    snackBarOpen = vi.fn();
    confirm = vi.fn().mockReturnValue(of(true));
    admin = false;
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    TestBed.configureTestingModule({
      imports: [SecurityTabComponent],
      providers: [
        { provide: AccountService, useValue: { changePassword, deleteAccount } },
        {
          provide: AuthService,
          useValue: { isAdmin: () => admin, currentUser: () => ({ email: 'jan@example.com' }), endSession }
        },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } },
        { provide: ConfirmService, useValue: { confirm } }
      ]
    });
  });

  afterEach(() => vi.restoreAllMocks());

  it('keeps the two password forms apart for password managers (own form and hidden username each)', () => {
    create();
    openDeleteForm();

    const forms = Array.from(root().querySelectorAll('form'));
    expect(forms.length).toBe(2);
    for (const form of forms) {
      const username = form.querySelector<HTMLInputElement>('input[autocomplete="username"]')!;
      expect(username.value).toBe('jan@example.com');
      expect(username.getAttribute('tabindex')).toBe('-1');
      expect(username.getAttribute('aria-hidden')).toBe('true');
    }
    expect(Array.from(forms[0].querySelectorAll('input[type="password"]')).map(i => i.getAttribute('autocomplete')))
      .toEqual(['current-password', 'new-password', 'new-password']);
    expect(Array.from(forms[1].querySelectorAll('input[type="password"]')).map(i => i.getAttribute('autocomplete')))
      .toEqual(['current-password']);
  });

  describe('change password', () => {
    it('links the strength indicator to the new password', () => {
      create();

      type(PASSWORD_FORM, 'newPassword', 'Abcdef1');

      expect(field(PASSWORD_FORM, 'newPassword').getAttribute('aria-describedby')).toContain('change-password-strength');
      expect(root().querySelector('#change-password-strength')?.textContent).toContain('Siła hasła: średnia');
    });

    it('has show-password toggle buttons with a fixed label and aria-pressed', () => {
      create();
      const toggle = root().querySelector<HTMLButtonElement>(`${PASSWORD_FORM} button[aria-label="Pokaż obecne hasło"]`)!;
      expect(toggle.getAttribute('aria-pressed')).toBe('false');

      toggle.click();
      fixture.detectChanges();

      expect(field(PASSWORD_FORM, 'currentPassword').type).toBe('text');
      expect(toggle.getAttribute('aria-label')).toBe('Pokaż obecne hasło');
      expect(toggle.getAttribute('aria-pressed')).toBe('true');
    });

    it('sends the passwords, clears the form and announces the success politely', () => {
      create();
      changePassword.mockReturnValue(of({ token: 'new' }));

      fillPasswordForm();
      submit(PASSWORD_FORM);

      expect(changePassword).toHaveBeenCalledWith({ currentPassword: 'Stare123', newPassword: 'Nowe1234' });
      expect(field(PASSWORD_FORM, 'currentPassword').value).toBe('');
      expect(field(PASSWORD_FORM, 'newPassword').value).toBe('');
      expect(snackBarOpen).toHaveBeenCalledWith('Hasło zostało zmienione. Pozostałe sesje zostały wylogowane.', 'OK',
        { duration: 6000, politeness: 'polite' });
    });

    it('keeps the submit button focusable (aria-disabled) while the request is pending', () => {
      create();
      changePassword.mockReturnValue(new Subject());

      fillPasswordForm();
      submit(PASSWORD_FORM);

      const button = root().querySelector<HTMLButtonElement>(`${PASSWORD_FORM} button[type="submit"]`)!;
      expect(button.disabled).toBe(false);
      expect(button.getAttribute('aria-disabled')).toBe('true');
      submit(PASSWORD_FORM);
      expect(changePassword).toHaveBeenCalledTimes(1);
    });

    it('validates the password policy on the client', () => {
      create();

      fillPasswordForm('Stare123', 'slabe');
      submit(PASSWORD_FORM);

      expect(changePassword).not.toHaveBeenCalled();
      expect(root().textContent).toContain('małą i wielką literę oraz cyfrę');
      expect(document.activeElement).toBe(field(PASSWORD_FORM, 'newPassword'));
    });

    it('shows the mismatch next to the confirmation field and focuses it', () => {
      create();

      fillPasswordForm('Stare123', 'Nowe1234', 'Nowe12345');
      submit(PASSWORD_FORM);

      expect(changePassword).not.toHaveBeenCalled();
      const confirmField = field(PASSWORD_FORM, 'confirmPassword').closest('mat-form-field')!;
      expect(confirmField.textContent).toContain('Hasła nie są takie same');
      expect(field(PASSWORD_FORM, 'confirmPassword').getAttribute('aria-invalid')).toBe('true');
      expect(document.activeElement).toBe(field(PASSWORD_FORM, 'confirmPassword'));
    });

    it('clears only the current password when it is wrong', () => {
      create();
      changePassword.mockReturnValue(throwError(() => badRequest({ message: 'Current password is incorrect' })));

      fillPasswordForm();
      submit(PASSWORD_FORM);

      expect(field(PASSWORD_FORM, 'currentPassword').value).toBe('');
      expect(field(PASSWORD_FORM, 'newPassword').value).toBe('Nowe1234');
      expect(root().querySelector(`${PASSWORD_FORM} .form-error[role="alert"]`)?.textContent).toBe('Obecne hasło jest nieprawidłowe');
    });

    it('translates Identity errors for a weak new password', () => {
      create();
      changePassword.mockReturnValue(throwError(() => badRequest({
        errors: { NewPassword: ['Passwords must have at least one non alphanumeric character.'] }
      })));

      fillPasswordForm();
      submit(PASSWORD_FORM);

      expect(root().querySelector(`${PASSWORD_FORM} .form-error[role="alert"]`)?.textContent).toBe('Hasło musi zawierać znak specjalny');
    });

    it('lets the request finish after leaving the page (the new token must still be stored by the service)', () => {
      create();
      const pending = new Subject<unknown>();
      changePassword.mockReturnValue(pending);

      fillPasswordForm();
      submit(PASSWORD_FORM);
      fixture.destroy();

      expect(pending.observed).toBe(true);
      pending.next({ token: 'new' });
      pending.complete();
      expect(pending.observed).toBe(false);
    });
  });

  describe('delete account', () => {
    it('reveals the form with a disclosure button and focuses the password field', async () => {
      create();
      expect(deleteToggle().getAttribute('aria-expanded')).toBe('false');
      expect(deleteToggle().getAttribute('aria-controls')).toBe('delete-account-form');
      expect(root().querySelector(DELETE_FORM)).toBeNull();

      openDeleteForm();
      await fixture.whenStable();

      expect(deleteToggle().getAttribute('aria-expanded')).toBe('true');
      expect(root().querySelector(`#delete-account-form ${DELETE_FORM}`)).not.toBeNull();
      expect(document.activeElement).toBe(field(DELETE_FORM, 'password'));
    });

    it('"Anuluj" closes the form, clears the password and returns the focus to the toggle', () => {
      create();
      openDeleteForm();
      type(DELETE_FORM, 'password', 'Haslo123');

      Array.from(root().querySelectorAll<HTMLButtonElement>(`${DELETE_FORM} button`)).find(b => b.textContent?.trim() === 'Anuluj')!.click();
      fixture.detectChanges();

      expect(root().querySelector(DELETE_FORM)).toBeNull();
      expect(document.activeElement).toBe(deleteToggle());
      openDeleteForm();
      expect(field(DELETE_FORM, 'password').value).toBe('');
    });

    it('requires the password before asking for confirmation', () => {
      create();
      openDeleteForm();

      submit(DELETE_FORM);

      expect(confirm).not.toHaveBeenCalled();
      expect(deleteAccount).not.toHaveBeenCalled();
      expect(root().querySelector(`${DELETE_FORM} mat-error`)?.textContent).toContain('Podaj hasło');
      expect(document.activeElement).toBe(field(DELETE_FORM, 'password'));
    });

    it('asks through ConfirmService, deletes the account and ends the session with a message', () => {
      create();
      deleteAccount.mockReturnValue(of(undefined));
      openDeleteForm();

      type(DELETE_FORM, 'password', 'Haslo123');
      submit(DELETE_FORM);

      expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ title: 'Usunąć konto?', destructive: true }));
      expect(deleteAccount).toHaveBeenCalledWith('Haslo123');
      expect(endSession).toHaveBeenCalledWith('Konto zostało usunięte. Dziękujemy za korzystanie z Job Board.');
    });

    it('does nothing when the confirmation is declined', () => {
      create();
      confirm.mockReturnValue(of(false));
      openDeleteForm();

      type(DELETE_FORM, 'password', 'Haslo123');
      submit(DELETE_FORM);

      expect(deleteAccount).not.toHaveBeenCalled();
      expect(field(DELETE_FORM, 'password').value).toBe('Haslo123');
    });

    it.each([
      ['Current password is incorrect', 'Obecne hasło jest nieprawidłowe'],
      ['You are the last member of a company with job offers', 'Jesteś ostatnim członkiem firmy, która ma oferty pracy – nie możesz teraz usunąć konta']
    ])('announces "%s", clears the password and keeps the session', (message, expected) => {
      create();
      deleteAccount.mockReturnValue(throwError(() => badRequest({ message })));
      openDeleteForm();

      type(DELETE_FORM, 'password', 'Haslo123');
      submit(DELETE_FORM);

      expect(root().querySelector(`${DELETE_FORM} .form-error[role="alert"]`)?.textContent).toBe(expected);
      expect(field(DELETE_FORM, 'password').value).toBe('');
      expect(endSession).not.toHaveBeenCalled();
    });

    it('ends the session even when the page was left while deleting', () => {
      create();
      const pending = new Subject<void>();
      deleteAccount.mockReturnValue(pending);
      openDeleteForm();

      type(DELETE_FORM, 'password', 'Haslo123');
      submit(DELETE_FORM);
      fixture.destroy();
      pending.next();
      pending.complete();

      expect(endSession).toHaveBeenCalled();
    });

    it('does not offer deleting an administrator account', () => {
      admin = true;
      create();

      expect(root().querySelector('button.delete-toggle')).toBeNull();
      expect(root().querySelector('.delete-account')?.textContent).toContain('Konta administratora nie można usunąć');
    });
  });
});
