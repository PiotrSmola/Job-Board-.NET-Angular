import { Component, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { JobApplicationService } from '../../../services/job-application.service';
import { ApplicationStatusHistoryEntry, JobApplication } from '../../../models/job-application.model';
import { getApiErrorMessage, isHandledByInterceptor } from '../../../utils/http-errors';
import { StatusTimelineComponent } from '../../shared/status-timeline.component';

/** Historia statusów aplikacji (dla admina – z autorami zmian). */
@Component({
  selector: 'app-application-history-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, StatusTimelineComponent],
  template: `
    <h2 mat-dialog-title>
      <mat-icon>history</mat-icon>
      Historia aplikacji
    </h2>
    <mat-dialog-content>
      <p class="summary">
        <strong>{{ data.userFullName || data.userEmail }}</strong> – {{ data.jobTitle }}
      </p>
      @if (loading()) {
        <div class="loading"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (error()) {
        <p class="error">{{ error() }}</p>
      } @else {
        <app-status-timeline [history]="history()" />
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Zamknij</button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: [`
    mat-dialog-content { min-width: 420px; }
    h2 { display: flex; align-items: center; gap: 12px; }
    .summary { margin: 0 0 16px; }
    .loading { display: flex; justify-content: center; padding: 24px; }
    .error { color: var(--error-color); }
    @media (max-width: 600px) { mat-dialog-content { min-width: 0; } }
  `]
})
export class ApplicationHistoryDialogComponent {
  private applicationService = inject(JobApplicationService);

  readonly data = inject<JobApplication>(MAT_DIALOG_DATA);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly history = signal<ApplicationStatusHistoryEntry[]>([]);

  constructor() {
    this.applicationService.getApplication(this.data.id).subscribe({
      next: (application) => {
        this.history.set(application.history ?? []);
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
