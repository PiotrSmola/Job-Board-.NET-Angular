import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { JobApplicationService } from '../../services/job-application.service';
import { JobApplication } from '../../models/job-application.model';
import { getApiErrorMessage, isHandledByInterceptor } from '../../utils/http-errors';
import { formatFileSize } from '../../utils/cv-file';
import { CvUploadComponent } from '../shared/cv-upload.component';

/**
 * Dodanie lub zmiana CV aplikacji; zamyka się z zaktualizowaną aplikacją po udanym wysłaniu.
 * Zamknięcie dialogu w trakcie wysyłania przerywa żądanie (unsubscribe przy zniszczeniu komponentu).
 */
@Component({
  selector: 'app-cv-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, MatDialogModule, MatButtonModule, MatIconModule, CvUploadComponent],
  template: `
    <h2 mat-dialog-title>
      <mat-icon aria-hidden="true">description</mat-icon>
      {{ data.hasCv ? 'Zmień CV' : 'Dodaj CV' }}
    </h2>
    <mat-dialog-content>
      <p class="summary"><strong>{{ data.jobTitle }}</strong> – {{ data.company }}</p>
      @if (data.hasCv) {
        <p class="current">
          Obecne CV: <strong>{{ data.cvFileName }}</strong>
          ({{ formatSize(data.cvSize) }}{{ data.cvUploadedAt ? ', ' + (data.cvUploadedAt | date: 'dd.MM.yyyy HH:mm') : '' }}).
          Nowy plik zastąpi obecny.
        </p>
      }
      <app-cv-upload
        [(file)]="file"
        (fileChange)="error.set(null)"
        [progress]="progress()"
        [errorMessage]="error()"
        [label]="data.hasCv ? 'Wybierz nowy plik' : 'Wybierz plik CV'" />
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close class="cancel-button">{{ uploading() ? 'Przerwij wysyłanie' : 'Anuluj' }}</button>
      <button mat-raised-button color="primary" class="upload-button" (click)="upload()" [disabled]="!file() || uploading()">
        {{ data.hasCv ? 'Zastąp CV' : 'Prześlij CV' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    mat-dialog-content { min-width: 420px; }
    h2 { display: flex; align-items: center; gap: 12px; }
    .summary { margin: 0 0 12px; }
    .current { margin: 0 0 16px; color: var(--text-secondary); }
    @media (max-width: 600px) { mat-dialog-content { min-width: 0; } }
  `]
})
export class CvDialogComponent {
  private applicationService = inject(JobApplicationService);
  private dialogRef = inject<MatDialogRef<CvDialogComponent, JobApplication>>(MatDialogRef);
  private destroyRef = inject(DestroyRef);

  readonly data = inject<JobApplication>(MAT_DIALOG_DATA);
  readonly file = signal<File | null>(null);
  readonly progress = signal<number | null>(null);
  readonly error = signal<string | null>(null);
  readonly uploading = signal(false);

  upload(): void {
    const file = this.file();
    if (!file || this.uploading()) return;

    this.uploading.set(true);
    this.progress.set(0);
    this.error.set(null);
    // w trakcie wysyłania dialogu nie zamyka kliknięcie obok ani Esc – tylko „Przerwij wysyłanie”
    this.dialogRef.disableClose = true;

    this.applicationService.uploadCv(this.data.id, file).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (event) => {
        if (event.type === 'progress') {
          this.progress.set(event.percent);
        } else {
          // nie trzymamy referencji do pliku dłużej niż trzeba
          this.file.set(null);
          this.dialogRef.close(event.application);
        }
      },
      error: (error) => {
        console.error('Error uploading CV:', error);
        this.uploading.set(false);
        this.progress.set(null);
        this.dialogRef.disableClose = false;
        this.error.set(isHandledByInterceptor(error)
          ? 'Brak uprawnień do zmiany CV'
          : getApiErrorMessage(error, 'Nie udało się przesłać CV'));
      }
    });
  }

  formatSize(bytes: number | null): string {
    return formatFileSize(bytes);
  }
}
