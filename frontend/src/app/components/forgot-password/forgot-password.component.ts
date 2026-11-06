import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, afterNextRender, inject, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Subscription } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { getApiErrorMessage } from '../../utils/http-errors';
import { focusFirstInvalidControl } from '../../utils/form-focus';

export const FORGOT_PASSWORD_SENT_MESSAGE =
  'Jeśli konto z tym adresem istnieje, wysłaliśmy na nie link do ustawienia nowego hasła. Link jest ważny 1 godzinę.';

/** „Nie pamiętasz hasła?” – API zawsze odpowiada 202, więc komunikat nie zdradza, czy konto istnieje. */
@Component({
  selector: 'app-forgot-password',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './forgot-password.component.html',
  styleUrl: './forgot-password.component.scss'
})
export class ForgotPasswordComponent {
  private readonly authService = inject(AuthService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private subscription: Subscription | null = null;
  private readonly statusRegion = viewChild<ElementRef<HTMLElement>>('statusRegion');

  readonly sentMessage = FORGOT_PASSWORD_SENT_MESSAGE;

  readonly sending = signal(false);
  readonly sent = signal(false);
  readonly error = signal('');

  readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]]
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.subscription?.unsubscribe());
  }

  /** Powrót do formularza (adres zostaje wpisany). */
  sendAgain(): void {
    this.sent.set(false);
  }

  submit(): void {
    if (this.sending()) return;
    this.error.set('');

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.error.set('Podaj poprawny adres e-mail');
      focusFirstInvalidControl(this.host.nativeElement);
      return;
    }

    this.sending.set(true);
    this.subscription = this.authService.forgotPassword(this.form.getRawValue().email.trim()).subscribe({
      next: () => {
        this.sending.set(false);
        this.sent.set(true);
        // formularz znika – fokus na komunikat, żeby nie przepadł
        afterNextRender(() => this.statusRegion()?.nativeElement.focus(), { injector: this.injector });
      },
      error: error => {
        console.error('Error requesting password reset:', error);
        this.sending.set(false);
        this.error.set(getApiErrorMessage(error, 'Nie udało się wysłać linku – spróbuj ponownie później'));
      }
    });
  }
}
