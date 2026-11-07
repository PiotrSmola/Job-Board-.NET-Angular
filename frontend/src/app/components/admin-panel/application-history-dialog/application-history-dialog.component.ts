import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { JobApplicationService } from '../../../services/job-application.service';
import { ApplicationStatusHistoryEntry, JobApplication } from '../../../models/job-application.model';
import { getApiErrorMessage, isHandledByInterceptor } from '../../../utils/http-errors';
import { StatusTimelineComponent } from '../../shared/status-timeline.component';
import { AttachmentListComponent } from '../../shared/attachment-list.component';

/** Historia statusów aplikacji (dla admina – z autorami zmian) i załączniki kandydata do pobrania. */
@Component({
  selector: 'app-application-history-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, MatProgressSpinnerModule, StatusTimelineComponent, AttachmentListComponent],
  template: `
    <h2 mat-dialog-title>Historia aplikacji</h2>
    <mat-dialog-content>
      <p class="summary">
        <strong>{{ data.userFullName || data.userEmail }}</strong> · {{ data.jobTitle }}
      </p>
      <section class="attachments" aria-labelledby="history-attachments-heading">
        <h3 id="history-attachments-heading">Załączniki</h3>
        <app-attachment-list
          [applicationId]="data.id"
          [attachments]="attachments()"
          [showDate]="true"
          emptyText="Kandydat nie dołączył załączników" />
      </section>
      @if (loading()) {
        <div class="loading"><mat-spinner diameter="40" aria-label="Wczytywanie historii"></mat-spinner></div>
      } @else if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      } @else {
        <app-status-timeline [history]="history()" />
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Zamknij</button>
    </mat-dialog-actions>
  `,
  styles: [`
    mat-dialog-content { min-width: min(420px, 80vw); }
    .summary { margin: 0 0 8px; color: var(--color-text-secondary); }
    .summary strong { color: var(--color-text); font-weight: 500; }
    .attachments { margin: 0 0 16px; }
    .attachments h3 { margin: 0 0 4px; font-size: var(--font-size-body); font-weight: 500; color: var(--color-text-secondary); }
    .loading { display: flex; justify-content: center; padding: 24px; }
    .error { color: var(--color-danger); }
  `]
})
export class ApplicationHistoryDialogComponent {
  private applicationService = inject(JobApplicationService);

  readonly data = inject<JobApplication>(MAT_DIALOG_DATA);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly history = signal<ApplicationStatusHistoryEntry[]>([]);
  /** Załączniki z wiersza listy, po wczytaniu szczegółów – aktualne z serwera. */
  readonly attachments = signal(this.data.attachments ?? []);

  constructor() {
    this.applicationService.getApplication(this.data.id).pipe(takeUntilDestroyed()).subscribe({
      next: (application) => {
        this.history.set(application.history ?? []);
        if (application.attachments) {
          this.attachments.set(application.attachments);
        }
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Error loading application history:', error);
        this.loading.set(false);
        this.error.set(isHandledByInterceptor(error)
          ? 'Brak dostępu do historii aplikacji'
          : getApiErrorMessage(error, 'Nie udało się wczytać historii aplikacji'));
      }
    });
  }
}
