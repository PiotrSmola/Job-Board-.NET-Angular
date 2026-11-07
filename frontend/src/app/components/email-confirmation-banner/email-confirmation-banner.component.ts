import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AuthService } from '../../services/auth.service';

const ALREADY_CONFIRMED_MESSAGE = 'Email is already confirmed';

/**
 * Baner „Potwierdź adres e-mail” dla zalogowanych z `emailConfirmed=false` (nie blokuje korzystania z aplikacji).
 * Ładowany leniwie do miejsca o minimalnej wysokości w powłoce – bez przesunięć układu (CLS), a dłuższy tekst się zawija.
 * Wynik wysyłki ogłasza stały region `role="status"`; trwające żądanie jest przerywane przy zniszczeniu.
 */
@Component({
  selector: 'app-email-confirmation-banner',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule],
  template: `
    <section class="email-banner" aria-label="Potwierdzenie adresu e-mail">
      <mat-icon class="icon" aria-hidden="true">mark_email_unread</mat-icon>
      <p class="text">
        <strong>Potwierdź adres e-mail.</strong>
        <span class="details" role="status">{{ status() || 'Kliknij link z wiadomości, którą wysłaliśmy po rejestracji.' }}</span>
      </p>
      <button mat-stroked-button type="button" class="resend" (click)="resend()"
        [disabled]="sending() || sent()" [disabledInteractive]="true">
        {{ sending() ? 'Wysyłanie…' : sent() ? 'Wysłano' : 'Wyślij ponownie' }}
      </button>
    </section>
  `,
  styles: [`
    :host { display: block; }
    /* minimalna wysokość (zmienna z .email-banner-slot w app.scss); dłuższy tekst zawija się bez obcinania (WCAG 1.4.10) */
    .email-banner { min-height: var(--email-banner-height, 56px); display: flex; flex-wrap: wrap; align-items: center;
      gap: 8px 16px; padding: 10px var(--page-gutter, 24px); background: var(--color-warning-bg, #2a2417);
      color: var(--color-warning-text, #f3dfb5); }
    .icon { color: var(--color-warning-icon, #e3b15a); flex-shrink: 0; }
    .text { flex: 1 1 280px; min-width: 0; margin: 0; line-height: 1.4; }
    .text strong { font-weight: 500; }
    .details { margin-left: 4px; }
    .resend { flex-shrink: 0; --mat-button-outlined-label-text-color: var(--color-warning-text, #f3dfb5); }
    @media (max-width: 600px) { .icon { display: none; } }
  `]
})
export class EmailConfirmationBannerComponent {
  private readonly authService = inject(AuthService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);

  readonly sending = signal(false);
  readonly sent = signal(false);
  readonly status = signal('');

  resend(): void {
    if (this.sending() || this.sent()) return;
    this.sending.set(true);
    this.status.set('');

    this.authService.resendConfirmation().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.sending.set(false);
        this.sent.set(true);
        this.status.set('Wysłaliśmy nowy link – sprawdź skrzynkę e-mail.');
      },
      error: (error: unknown) => {
        this.sending.set(false);
        if (error instanceof HttpErrorResponse && error.status === 400 && error.error?.message === ALREADY_CONFIRMED_MESSAGE) {
          // potwierdzono w międzyczasie (np. w innej karcie) – baner znika
          this.authService.updateCurrentUser({ emailConfirmed: true });
          this.snackBar.open('Adres e-mail jest już potwierdzony', 'OK', { duration: 5000 });
          return;
        }
        // 401/403 obsługuje interceptor; komunikaty bez mapy tłumaczeń API (nie powiększamy paczki startowej)
        const status = error instanceof HttpErrorResponse ? error.status : -1;
        if (status === 401 || status === 403) return;
        this.status.set(status === 429
          ? 'Zbyt wiele żądań – spróbuj ponownie za kilka minut.'
          : status === 0 ? 'Brak połączenia z serwerem.' : 'Nie udało się wysłać linku – spróbuj ponownie później.');
      }
    });
  }
}
