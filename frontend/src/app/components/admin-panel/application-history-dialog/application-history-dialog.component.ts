import { Component, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { JobApplicationService } from '../../../services/job-application.service';
import { ApplicationStatusHistoryEntry, JobApplication } from '../../../models/job-application.model';
import { getApiErrorMessage, isHandledByInterceptor } from '../../../utils/http-errors';
import { formatFileSize } from '../../../utils/cv-file';
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
      <p class="cv">
        <mat-icon>description</mat-icon>
        @if (data.hasCv) {
          <span>CV: {{ data.cvFileName }} ({{ cvSize }})</span>
        } @else {
          <span>Kandydat nie dołączył CV</span>
        }
      </p>
      @if (cvError(); as message) {
        <p class="cv-error" role="alert">{{ message }}</p>
      }
      @if (loading()) {
        <div class="loading"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (error()) {
        <p class="error">{{ error() }}</p>
      } @else {
        <app-status-timeline [history]="history()" />
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      @if (data.hasCv) {
        <button mat-button class="download-cv" (click)="downloadCv()" [disabled]="downloadingCv()">
          @if (downloadingCv()) {
            <mat-spinner diameter="18"></mat-spinner>
          } @else {
            <mat-icon>download</mat-icon>
          }
          Pobierz CV
        </button>
      }
      <button mat-button mat-dialog-close>Zamknij</button>
    </mat-dialog-actions>
  `,
  styles: [`
    mat-dialog-content { min-width: 420px; }
    h2 { display: flex; align-items: center; gap: 12px; }
    .summary { margin: 0 0 8px; }
    .cv { display: flex; align-items: center; gap: 8px; margin: 0 0 16px; color: var(--text-secondary); }
    .cv-error { color: var(--error-color); margin: 0 0 16px; }
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
  readonly downloadingCv = signal(false);
  readonly cvError = signal<string | null>(null);
  readonly cvSize = formatFileSize(this.data.cvSize);

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

  downloadCv(): void {
    this.downloadingCv.set(true);
    this.cvError.set(null);
    this.applicationService.downloadCv(this.data.id, this.data.cvFileName).subscribe({
      next: () => this.downloadingCv.set(false),
      error: (error) => {
        console.error('Error downloading CV:', error);
        this.downloadingCv.set(false);
        if (!isHandledByInterceptor(error)) {
          this.cvError.set(getApiErrorMessage(error, 'Nie udało się pobrać CV'));
        }
      }
    });
  }
}
