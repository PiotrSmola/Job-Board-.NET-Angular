import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, isDevMode, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../services/auth.service';
import { getSafeReturnUrl } from '../../utils/return-url';
import { focusFirstInvalidControl } from '../../utils/form-focus';
import { getApiErrorMessage } from '../../utils/http-errors';

export const LOCKED_OUT_MESSAGE =
  'Konto zostało tymczasowo zablokowane po zbyt wielu nieudanych próbach. Spróbuj ponownie za kilka minut.';
export const INVALID_CREDENTIALS_MESSAGE = 'Nieprawidłowy e-mail lub hasło.';

export interface DemoAccount {
  role: string;
  email: string;
  password: string;
}

/**
 * Konta z danych przykładowych (seed) – hasła są publiczne (README). Hasło admina `Admin123!` obowiązuje tylko
 * w trybie deweloperskim (w Dockerze admin ma hasło z `.env`), więc admina pokazujemy tylko w `ng serve`.
 */
export function getDemoAccounts(devMode: boolean): DemoAccount[] {
  const accounts: DemoAccount[] = [
    { role: 'Kandydat', email: 'jan.kowalski@example.com', password: 'User123!' },
    { role: 'Pracodawca', email: 'rekruter@techcorp.pl', password: 'Employer123!' }
  ];
  return devMode ? [...accounts, { role: 'Admin', email: 'admin@jobboard.com', password: 'Admin123!' }] : accounts;
}

/**
 * Logowanie (makieta Logowanie.dc.html). Przycisk wysyłania jest zawsze aktywny – błędy pól pokazujemy po próbie
 * (fokus na pierwsze niepoprawne pole), błąd logowania w regionie `role="alert"`.
 */
@Component({
  selector: 'app-login',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './login.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './login.component.scss'
})
export class LoginComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly demoAccounts = getDemoAccounts(isDevMode());

  readonly loginForm = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    password: ['', [Validators.required, Validators.maxLength(100)]]
  });

  readonly loading = signal(false);
  readonly errorMessage = signal('');
  readonly hidePassword = signal(true);

  togglePassword(): void {
    this.hidePassword.update(hidden => !hidden);
  }

  onSubmit(): void {
    if (this.loading()) return;
    this.errorMessage.set('');

    if (this.loginForm.invalid) {
      this.loginForm.markAllAsTouched();
      focusFirstInvalidControl(this.host.nativeElement);
      return;
    }

    this.loading.set(true);
    const { email, password } = this.loginForm.getRawValue();

    this.authService.login({ email, password })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          const returnUrl = getSafeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl'));
          if (returnUrl) {
            this.router.navigateByUrl(returnUrl);
          } else {
            // bez returnUrl: pracodawca – do swojego panelu, pozostali – na listę ofert
            this.router.navigate([this.authService.isEmployer() ? '/employer' : '/jobs']);
          }
        },
        error: (error: HttpErrorResponse) => {
          this.loading.set(false);
          // hasło nie zostaje w polu po nieudanej próbie
          this.loginForm.controls.password.reset();
          this.errorMessage.set(error.status === 423
            ? LOCKED_OUT_MESSAGE
            : error.status === 401 || error.status === 400
              ? INVALID_CREDENTIALS_MESSAGE
              : getApiErrorMessage(error, 'Nie udało się zalogować – spróbuj ponownie za chwilę.'));
        }
      });
  }
}
