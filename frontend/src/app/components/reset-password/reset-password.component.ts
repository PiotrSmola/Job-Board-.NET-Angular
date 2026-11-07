import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AuthService } from '../../services/auth.service';
import { getApiErrorMessage } from '../../utils/http-errors';
import {
  PASSWORD_MAX_LENGTH, PASSWORD_MIN_LENGTH, getPasswordErrorMessages, passwordPolicyValidator, passwordsMatchValidator
} from '../../utils/password';
import { focusFirstInvalidControl } from '../../utils/form-focus';
import { PasswordStrengthComponent } from '../shared/password-strength.component';
import { PasswordMatchErrorStateMatcher } from '../shared/password-match.matcher';

const INVALID_LINK_MESSAGE = 'Invalid or expired reset link';
// rozsądne górne limity – dłuższe wartości to na pewno nie link z naszego e-maila
const MAX_EMAIL_LENGTH = 256;
const MAX_TOKEN_LENGTH = 2048;

/**
 * Ustawienie nowego hasła z linku `/reset-password?email=…&token=…`.
 * Token jest odczytywany raz, trzymany tylko w polu klasy (nie w sygnale, szablonie ani logach)
 * i od razu usuwany z paska adresu i historii (`replaceUrl`). Formularz ma wyłącznie pola nowego hasła.
 */
@Component({
  selector: 'app-reset-password',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    PasswordStrengthComponent
  ],
  templateUrl: './reset-password.component.html',
  styleUrl: './reset-password.component.scss'
})
export class ResetPasswordComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);
  /** Jednorazowy token z linku – czyszczony po użyciu. */
  private token: string | null = null;

  readonly email: string;
  readonly linkValid = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly hidePassword = signal(true);
  readonly confirmMatcher = new PasswordMatchErrorStateMatcher();
  readonly minLength = PASSWORD_MIN_LENGTH;
  readonly maxLength = PASSWORD_MAX_LENGTH;

  readonly form = inject(FormBuilder).nonNullable.group({
    newPassword: ['', [
      Validators.required,
      Validators.minLength(PASSWORD_MIN_LENGTH),
      Validators.maxLength(PASSWORD_MAX_LENGTH),
      passwordPolicyValidator
    ]],
    confirmPassword: ['', [Validators.required]]
  }, { validators: passwordsMatchValidator('newPassword', 'confirmPassword') });

  readonly newPassword = toSignal(this.form.controls.newPassword.valueChanges, { initialValue: '' });

  constructor() {
    const params = inject(ActivatedRoute).snapshot.queryParamMap;
    const email = (params.get('email') ?? '').trim();
    const token = params.get('token') ?? '';

    const valid = !!email && email.length <= MAX_EMAIL_LENGTH && email.includes('@')
      && !!token && token.length <= MAX_TOKEN_LENGTH;
    this.email = valid ? email : '';
    this.token = valid ? token : null;
    this.linkValid.set(valid);

    // token nie zostaje w pasku adresu ani w historii przeglądarki
    if (params.has('token') || params.has('email')) {
      this.router.navigate(['/reset-password'], { replaceUrl: true });
    }

    this.destroyRef.onDestroy(() => {
      this.token = null;
    });
  }

  togglePassword(): void {
    this.hidePassword.update(hidden => !hidden);
  }

  submit(): void {
    if (this.saving() || !this.token) return;
    this.error.set('');

    if (this.form.invalid) {
      // błędy przy polach (mat-error), fokus na pierwsze niepoprawne
      this.form.markAllAsTouched();
      focusFirstInvalidControl(this.host.nativeElement);
      // same pola są poprawne, różnią się tylko hasła – fokus na pole powtórzenia (tam jest komunikat)
      if (this.form.controls.newPassword.valid && this.form.hasError('passwordMismatch')) {
        this.host.nativeElement.querySelector<HTMLElement>('[formControlName="confirmPassword"]')?.focus();
      }
      return;
    }

    this.saving.set(true);
    this.authService.resetPassword({
      email: this.email,
      token: this.token,
      newPassword: this.form.getRawValue().newPassword
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.saving.set(false);
        this.token = null;
        this.form.reset();
        this.snackBar.open('Hasło zostało zmienione – zaloguj się nowym hasłem', 'OK', { duration: 8000, politeness: 'polite' });
        this.router.navigate(['/login']);
      },
      error: error => {
        this.saving.set(false);
        if (error instanceof HttpErrorResponse && error.status === 400 && error.error?.message === INVALID_LINK_MESSAGE) {
          // link wygasł lub został już użyty – formularz nie ma sensu, proponujemy nowy link
          this.token = null;
          this.form.reset();
          this.linkValid.set(false);
        }
        const passwordErrors = getPasswordErrorMessages(error);
        this.error.set(passwordErrors.length
          ? passwordErrors.join('. ')
          : getApiErrorMessage(error, 'Nie udało się zmienić hasła – spróbuj ponownie'));
      }
    });
  }
}
