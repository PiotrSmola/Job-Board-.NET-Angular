import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../services/auth.service';
import { getApiErrorMessage } from '../../utils/http-errors';

const ALREADY_CONFIRMED_MESSAGE = 'Email is already confirmed';
const MAX_USER_ID_LENGTH = 128;
const MAX_TOKEN_LENGTH = 2048;

export type ConfirmEmailState = 'confirming' | 'confirmed' | 'error';

/**
 * Potwierdzenie adresu e-mail z linku `/confirm-email?userId=…&token=…` – wysyłane automatycznie po wejściu.
 * Parametry są od razu usuwane z paska adresu i historii (`replaceUrl`), token nie trafia do logów ani szablonu.
 * Zalogowany użytkownik po błędzie (np. wygasły link) może od razu poprosić o nowy.
 */
@Component({
  selector: 'app-confirm-email',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './confirm-email.component.html',
  styleUrl: './confirm-email.component.scss'
})
export class ConfirmEmailComponent {
  readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly state = signal<ConfirmEmailState>('confirming');
  readonly error = signal('');
  readonly resending = signal(false);
  readonly resendStatus = signal('');

  constructor() {
    const params = inject(ActivatedRoute).snapshot.queryParamMap;
    const userId = params.get('userId') ?? '';
    const token = params.get('token') ?? '';

    if (params.has('token') || params.has('userId')) {
      this.router.navigate(['/confirm-email'], { replaceUrl: true });
    }

    if (!userId || !token || userId.length > MAX_USER_ID_LENGTH || token.length > MAX_TOKEN_LENGTH) {
      this.fail('Link potwierdzający jest nieprawidłowy lub niekompletny');
      return;
    }

    this.authService.confirmEmail({ userId, token }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.state.set('confirmed'),
      error: (error: unknown) => {
        // link kliknięty drugi raz – adres jest już potwierdzony
        if (error instanceof HttpErrorResponse && error.status === 400 && error.error?.message === ALREADY_CONFIRMED_MESSAGE) {
          this.state.set('confirmed');
          return;
        }
        this.fail(getApiErrorMessage(error, 'Nie udało się potwierdzić adresu e-mail – spróbuj ponownie później'));
      }
    });
  }

  resend(): void {
    if (this.resending()) return;
    this.resending.set(true);
    this.resendStatus.set('');

    this.authService.resendConfirmation().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.resending.set(false);
        this.resendStatus.set('Wysłaliśmy nowy link potwierdzający – sprawdź skrzynkę e-mail');
      },
      error: (error: unknown) => {
        this.resending.set(false);
        if (error instanceof HttpErrorResponse && error.status === 400 && error.error?.message === ALREADY_CONFIRMED_MESSAGE) {
          this.authService.updateCurrentUser({ emailConfirmed: true });
          this.state.set('confirmed');
          return;
        }
        this.resendStatus.set(getApiErrorMessage(error, 'Nie udało się wysłać linku – spróbuj ponownie później'));
      }
    });
  }

  private fail(message: string): void {
    this.error.set(message);
    this.state.set('error');
  }
}
