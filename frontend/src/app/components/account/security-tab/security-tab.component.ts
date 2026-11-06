import { ChangeDetectionStrategy, Component, ElementRef, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AccountService } from '../../../services/account.service';
import { AuthService } from '../../../services/auth.service';
import { getApiErrorMessage, isHandledByInterceptor } from '../../../utils/http-errors';
import {
  PASSWORD_MAX_LENGTH, PASSWORD_MIN_LENGTH, getPasswordErrorMessages, passwordPolicyValidator, passwordsMatchValidator
} from '../../../utils/password';
import { focusFirstInvalidControl } from '../../../utils/form-focus';
import { PasswordStrengthComponent } from '../../shared/password-strength.component';
import { PasswordMatchErrorStateMatcher } from '../../shared/password-match.matcher';

const WRONG_PASSWORD_MESSAGE = 'Current password is incorrect';

function isWrongPassword(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status === 400 && error.error?.message === WRONG_PASSWORD_MESSAGE;
}

/**
 * Bezpieczeństwo konta: zmiana hasła (nowy token z odpowiedzi – sesja trwa) i usunięcie konta potwierdzone hasłem.
 * Hasła żyją tylko w kontrolkach formularza – po zakończeniu żądania pola są czyszczone.
 * Tych dwóch żądań celowo NIE przerywamy przy opuszczeniu strony: serwer mógł już zmienić hasło (nowy token trzeba
 * zapisać, inaczej sesja wygaśnie) albo usunąć konto (sesję trzeba zakończyć). Kończą się same po odpowiedzi.
 */
@Component({
  selector: 'app-security-tab',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    PasswordStrengthComponent
  ],
  templateUrl: './security-tab.component.html',
  styleUrl: './security-tab.component.scss'
})
export class SecurityTabComponent {
  private readonly accountService = inject(AccountService);
  private readonly authService = inject(AuthService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly fb = inject(FormBuilder).nonNullable;

  readonly confirmMatcher = new PasswordMatchErrorStateMatcher();
  /** Dla menedżerów haseł – ukryte pole username obok nowego hasła. */
  readonly email = this.authService.currentUser()?.email ?? '';
  readonly minLength = PASSWORD_MIN_LENGTH;
  readonly maxLength = PASSWORD_MAX_LENGTH;
  /** Konta administratora nie usuwa się z tego miejsca (API zwraca 400). */
  readonly isAdmin = this.authService.isAdmin();

  readonly hideCurrent = signal(true);
  readonly hideNew = signal(true);
  readonly changing = signal(false);
  readonly changeError = signal('');
  readonly changeStatus = signal('');
  readonly deleting = signal(false);
  readonly deleteError = signal('');

  readonly passwordForm = this.fb.group({
    currentPassword: ['', [Validators.required, Validators.maxLength(PASSWORD_MAX_LENGTH)]],
    newPassword: ['', [
      Validators.required,
      Validators.minLength(PASSWORD_MIN_LENGTH),
      Validators.maxLength(PASSWORD_MAX_LENGTH),
      passwordPolicyValidator
    ]],
    confirmPassword: ['', [Validators.required]]
  }, { validators: passwordsMatchValidator('newPassword', 'confirmPassword') });

  readonly deleteForm = this.fb.group({
    password: ['', [Validators.required, Validators.maxLength(PASSWORD_MAX_LENGTH)]],
    acknowledge: [false, [Validators.requiredTrue]]
  });

  changePassword(): void {
    if (this.changing()) return;
    this.changeError.set('');
    this.changeStatus.set('');

    if (this.passwordForm.invalid) {
      this.passwordForm.markAllAsTouched();
      this.changeError.set(this.passwordForm.hasError('passwordMismatch')
        ? 'Nowe hasła nie są takie same'
        : 'Popraw zaznaczone pola formularza');
      focusFirstInvalidControl(this.host.nativeElement.querySelector('.password-section') ?? this.host.nativeElement);
      return;
    }

    const { currentPassword, newPassword } = this.passwordForm.getRawValue();
    this.changing.set(true);
    this.accountService.changePassword({ currentPassword, newPassword }).subscribe({
      next: () => {
        this.changing.set(false);
        this.passwordForm.reset();
        this.changeStatus.set('Hasło zostało zmienione. Pozostałe sesje zostały wylogowane.');
      },
      error: error => {
        console.error('Error changing password:', error);
        this.changing.set(false);
        if (isWrongPassword(error)) {
          this.passwordForm.controls.currentPassword.reset();
          this.passwordForm.controls.currentPassword.markAsTouched();
        }
        const passwordErrors = getPasswordErrorMessages(error);
        this.changeError.set(passwordErrors.length
          ? passwordErrors.join('. ')
          : isHandledByInterceptor(error) ? 'Nie udało się zmienić hasła' : getApiErrorMessage(error, 'Nie udało się zmienić hasła'));
      }
    });
  }

  deleteAccount(): void {
    if (this.deleting() || this.isAdmin) return;
    this.deleteError.set('');

    if (this.deleteForm.invalid) {
      this.deleteForm.markAllAsTouched();
      this.deleteError.set('Podaj hasło i potwierdź, że rozumiesz skutki usunięcia konta');
      focusFirstInvalidControl(this.host.nativeElement.querySelector('.delete-section') ?? this.host.nativeElement);
      return;
    }

    const { password } = this.deleteForm.getRawValue();
    this.deleting.set(true);
    this.accountService.deleteAccount(password).subscribe({
      next: () => {
        this.deleting.set(false);
        this.deleteForm.reset();
        this.authService.endSession('Konto zostało usunięte. Dziękujemy za korzystanie z Job Board.');
      },
      error: error => {
        console.error('Error deleting account:', error);
        this.deleting.set(false);
        // hasło nie zostaje w polu po nieudanej próbie
        this.deleteForm.controls.password.reset();
        this.deleteForm.controls.password.markAsTouched();
        this.deleteError.set(isHandledByInterceptor(error)
          ? 'Nie udało się usunąć konta'
          : getApiErrorMessage(error, 'Nie udało się usunąć konta'));
      }
    });
  }
}
