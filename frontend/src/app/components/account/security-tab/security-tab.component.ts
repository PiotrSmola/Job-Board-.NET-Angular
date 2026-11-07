import { ChangeDetectionStrategy, Component, ElementRef, Injector, afterNextRender, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { filter, switchMap, tap } from 'rxjs';
import { AccountService } from '../../../services/account.service';
import { AuthService } from '../../../services/auth.service';
import { ConfirmService } from '../../../services/confirm.service';
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
 * „Hasło i bezpieczeństwo”: zmiana hasła (nowy token z odpowiedzi – sesja trwa) i usunięcie konta (hasło + ConfirmService).
 * Dwa osobne formularze, każdy z ukrytym polem `username`, więc menedżer haseł nie myli „obecnego hasła” z hasłem
 * do usunięcia konta. Hasła żyją tylko w kontrolkach formularza – po zakończeniu żądania pola są czyszczone.
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
  private readonly confirmService = inject(ConfirmService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly fb = inject(FormBuilder).nonNullable;

  readonly confirmMatcher = new PasswordMatchErrorStateMatcher();
  /** Dla menedżerów haseł – ukryte pole username w obu formularzach. */
  readonly email = this.authService.currentUser()?.email ?? '';
  readonly minLength = PASSWORD_MIN_LENGTH;
  readonly maxLength = PASSWORD_MAX_LENGTH;
  /** Konta administratora nie usuwa się z tego miejsca (API zwraca 400). */
  readonly isAdmin = this.authService.isAdmin();

  readonly hideCurrent = signal(true);
  readonly hideNew = signal(true);
  readonly changing = signal(false);
  readonly changeError = signal('');
  readonly deleteOpen = signal(false);
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

  readonly newPassword = toSignal(this.passwordForm.controls.newPassword.valueChanges, { initialValue: '' });

  readonly deleteForm = this.fb.group({
    password: ['', [Validators.required, Validators.maxLength(PASSWORD_MAX_LENGTH)]]
  });

  toggleCurrent(): void {
    this.hideCurrent.update(hidden => !hidden);
  }

  toggleNew(): void {
    this.hideNew.update(hidden => !hidden);
  }

  changePassword(): void {
    if (this.changing()) return;
    this.changeError.set('');

    if (this.passwordForm.invalid) {
      this.passwordForm.markAllAsTouched();
      const form = this.host.nativeElement.querySelector<HTMLElement>('.password-form') ?? this.host.nativeElement;
      focusFirstInvalidControl(form);
      // pola poprawne, różnią się tylko hasła – fokus na pole powtórzenia (tam jest komunikat)
      if (this.passwordForm.controls.currentPassword.valid && this.passwordForm.controls.newPassword.valid) {
        form.querySelector<HTMLElement>('[formControlName="confirmPassword"]')?.focus();
      }
      return;
    }

    const { currentPassword, newPassword } = this.passwordForm.getRawValue();
    this.changing.set(true);
    this.accountService.changePassword({ currentPassword, newPassword }).subscribe({
      next: () => {
        this.changing.set(false);
        this.passwordForm.reset();
        this.snackBar.open('Hasło zostało zmienione. Pozostałe sesje zostały wylogowane.', 'OK',
          { duration: 6000, politeness: 'polite' });
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

  /** Pokazuje/ukrywa formularz usunięcia konta; po otwarciu fokus na polu hasła. */
  toggleDelete(): void {
    const open = !this.deleteOpen();
    this.deleteOpen.set(open);
    this.deleteError.set('');
    if (open) {
      afterNextRender(
        () => this.host.nativeElement.querySelector<HTMLElement>('.delete-form input[formControlName="password"]')?.focus(),
        { injector: this.injector });
    } else {
      this.deleteForm.reset();
    }
  }

  /** „Anuluj” w formularzu usunięcia – zamyka go i oddaje fokus przyciskowi „Usuń konto”. */
  cancelDelete(): void {
    this.toggleDelete();
    this.host.nativeElement.querySelector<HTMLElement>('.delete-toggle')?.focus();
  }

  deleteAccount(): void {
    if (this.deleting() || this.isAdmin) return;
    this.deleteError.set('');

    if (this.deleteForm.invalid) {
      this.deleteForm.markAllAsTouched();
      focusFirstInvalidControl(this.host.nativeElement.querySelector('.delete-form') ?? this.host.nativeElement);
      return;
    }

    const { password } = this.deleteForm.getRawValue();
    this.confirmService.confirm({
      title: 'Usunąć konto?',
      message: 'Usuniemy profil, aplikacje z załącznikami i dokumenty z profilu. Tej operacji nie da się cofnąć.',
      confirmLabel: 'Usuń konto',
      destructive: true
    }).pipe(
      filter(Boolean),
      tap(() => this.deleting.set(true)),
      switchMap(() => this.accountService.deleteAccount(password))
    ).subscribe({
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
