import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LiveAnnouncer } from '@angular/cdk/a11y';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { SavedJobsService } from '../../services/saved-jobs.service';
import { getApiErrorMessage, isHandledByInterceptor } from '../../utils/http-errors';

/**
 * Przełącznik „zapisz ofertę” (serduszko) – widoczny tylko dla kandydata.
 * Stan z SavedJobsService (optymistycznie, z cofnięciem przy błędzie); `aria-pressed` + etykieta z tytułem oferty,
 * stan widać też po kształcie ikony (pełna/obrys), nie tylko po kolorze. Wynik ogłasza LiveAnnouncer (jeden region
 * dla całej listy), błąd – snackbar. Kliknięcie nie przechodzi do karty oferty (stopPropagation).
 */
@Component({
  selector: 'app-save-job-button',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule],
  template: `
    @if (savedJobs.enabled()) {
      <button
        mat-icon-button
        type="button"
        class="save-job-button"
        [class.saved]="saved()"
        [attr.aria-pressed]="saved()"
        [attr.aria-label]="label()"
        [attr.aria-busy]="pending() || null"
        [disabled]="pending()"
        [disabledInteractive]="true"
        (click)="onClick($event)">
        <mat-icon aria-hidden="true">{{ saved() ? 'favorite' : 'favorite_border' }}</mat-icon>
      </button>
    }
  `,
  styles: [`
    :host { display: inline-flex; }
    /* w wierszu flex: dosuń do przycisków po prawej stronie */
    :host(.push-end) { margin-left: auto; }
    .save-job-button { color: var(--color-text-secondary); }
    /* stan widać po kształcie ikony (pełna / obrys); akcent ma kontrast > 3:1 na każdej powierzchni (WCAG 1.4.11) */
    .save-job-button.saved { color: var(--color-accent); }
  `]
})
export class SaveJobButtonComponent {
  readonly savedJobs = inject(SavedJobsService);
  private readonly announcer = inject(LiveAnnouncer);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);

  readonly jobId = input.required<number>();
  readonly jobTitle = input.required<string>();

  // isSaved/isPending czytają sygnały serwisu, więc computed reaguje na ich zmiany
  readonly saved = computed(() => this.savedJobs.isSaved(this.jobId()));
  readonly pending = computed(() => this.savedJobs.isPending(this.jobId()));
  readonly label = computed(() => this.saved()
    ? `Usuń z zapisanych: ${this.jobTitle()}`
    : `Zapisz ofertę: ${this.jobTitle()}`);

  constructor() {
    // po błędzie wczytania zbioru id spróbuj ponownie (nic nie robi, gdy stan jest już wczytany)
    this.savedJobs.ensureLoaded();
  }

  onClick(event: Event): void {
    event.stopPropagation();
    event.preventDefault();
    if (this.pending()) return;

    const title = this.jobTitle();
    const saving = !this.saved();
    this.savedJobs.toggle(this.jobId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: saved => this.announcer.announce(saved ? `Zapisano ofertę: ${title}` : `Usunięto z zapisanych: ${title}`),
      error: error => {
        if (isHandledByInterceptor(error)) return;
        const fallback = saving ? 'Nie udało się zapisać oferty' : 'Nie udało się usunąć oferty z zapisanych';
        this.snackBar.open(getApiErrorMessage(error, fallback), 'OK', { duration: 5000 });
      }
    });
  }
}
