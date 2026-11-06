import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Subscription } from 'rxjs';
import { AccountService } from '../../../services/account.service';
import { AuthService } from '../../../services/auth.service';
import { NotificationPreferences } from '../../../models/account.model';
import { getApiErrorMessage, isHandledByInterceptor } from '../../../utils/http-errors';

type PreferenceKey = keyof NotificationPreferences;

interface PreferenceOption {
  key: PreferenceKey;
  label: string;
  description: string;
}

const CANDIDATE_OPTIONS: PreferenceOption[] = [
  {
    key: 'applicationStatusChanged',
    label: 'Moje aplikacje',
    description: 'Potwierdzenie złożenia aplikacji i zmiany jej statusu (z komentarzem rekrutera)'
  }
];

const EMPLOYER_OPTIONS: PreferenceOption[] = [
  { key: 'newApplication', label: 'Nowe aplikacje', description: 'Nowa aplikacja na ofertę Twojej firmy' },
  { key: 'companyReview', label: 'Weryfikacja firmy', description: 'Decyzja administratora o zatwierdzeniu lub odrzuceniu firmy' }
];

/** Preferencje powiadomień e-mail – pokazujemy tylko te, które dotyczą roli użytkownika. */
@Component({
  selector: 'app-notifications-tab',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatButtonModule, MatCheckboxModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './notifications-tab.component.html',
  styleUrl: './notifications-tab.component.scss'
})
export class NotificationsTabComponent {
  private readonly accountService = inject(AccountService);
  private readonly authService = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private saveSubscription: Subscription | null = null;

  /** Admin nie dostaje powiadomień o aplikacjach ani o firmach – tylko e-maile bezpieczeństwa. */
  readonly options: PreferenceOption[] = this.authService.isAdmin()
    ? []
    : this.authService.isEmployer() ? EMPLOYER_OPTIONS : CANDIDATE_OPTIONS;

  readonly state = signal<'loading' | 'loaded' | 'error'>('loading');
  readonly loadError = signal('');
  readonly saving = signal(false);
  readonly saveError = signal('');
  readonly saveStatus = signal('');

  readonly form = inject(FormBuilder).nonNullable.group({
    applicationStatusChanged: true,
    newApplication: true,
    companyReview: true
  });

  constructor() {
    if (this.options.length) {
      this.load();
    } else {
      this.state.set('loaded');
    }
    this.destroyRef.onDestroy(() => this.saveSubscription?.unsubscribe());
  }

  load(): void {
    this.state.set('loading');
    this.accountService.getNotificationPreferences().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: preferences => {
        this.form.reset(this.normalize(preferences));
        this.state.set('loaded');
      },
      error: error => {
        console.error('Error loading notification preferences:', error);
        this.loadError.set(getApiErrorMessage(error, 'Nie udało się wczytać ustawień powiadomień'));
        this.state.set('error');
      }
    });
  }

  save(): void {
    if (this.saving()) return;
    this.saveError.set('');
    this.saveStatus.set('');

    const preferences = this.form.getRawValue();
    this.saving.set(true);
    this.saveSubscription = this.accountService.updateNotificationPreferences(preferences).subscribe({
      next: saved => {
        this.saving.set(false);
        // odpowiedź bez treści – zostają wysłane wartości
        this.form.reset(saved ? this.normalize(saved) : preferences);
        this.saveStatus.set('Zapisano ustawienia powiadomień');
      },
      error: error => {
        console.error('Error saving notification preferences:', error);
        this.saving.set(false);
        this.saveError.set(isHandledByInterceptor(error)
          ? 'Nie udało się zapisać ustawień'
          : getApiErrorMessage(error, 'Nie udało się zapisać ustawień'));
      }
    });
  }

  /** Brakujące pole traktujemy jak domyślne „włączone”. */
  private normalize(preferences: Partial<NotificationPreferences>): NotificationPreferences {
    return {
      applicationStatusChanged: preferences.applicationStatusChanged !== false,
      newApplication: preferences.newApplication !== false,
      companyReview: preferences.companyReview !== false
    };
  }
}
