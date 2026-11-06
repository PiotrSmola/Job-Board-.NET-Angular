import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription } from 'rxjs';
import { MAT_DIALOG_DATA, MatDialogConfig, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { JobApplicationService } from '../../services/job-application.service';
import { AttachmentUploadItem, JobApplication } from '../../models/job-application.model';
import { getApiErrorMessage, isHandledByInterceptor } from '../../utils/http-errors';
import { AttachmentsPickerComponent } from '../shared/attachments-picker.component';

/** Wysyłanie przerwane przez użytkownika – serwer mógł już zapisać pliki (faza przetwarzania), więc rodzic odświeża dane. */
export const ATTACHMENTS_UPLOAD_ABORTED = 'aborted';

/** Wynik dialogu: zaktualizowana aplikacja, przerwane wysyłanie albo undefined (zamknięto bez wysyłania). */
export type AttachmentsDialogResult = JobApplication | typeof ATTACHMENTS_UPLOAD_ABORTED | undefined;

/** Konfiguracja otwarcia dialogu (modalny dla czytników ekranu). */
export const ATTACHMENTS_DIALOG_CONFIG: MatDialogConfig = { width: '600px', maxWidth: '95vw', ariaModal: true };

/**
 * Dodanie załączników do aplikacji; zamyka się z zaktualizowaną aplikacją po udanym wysłaniu.
 * Escape, „Przerwij wysyłanie” i zamknięcie dialogu w trakcie wysyłania przerywają żądanie.
 */
@Component({
  selector: 'app-attachments-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, AttachmentsPickerComponent],
  template: `
    <h2 mat-dialog-title>
      <mat-icon aria-hidden="true">attach_file</mat-icon>
      Dodaj załączniki
    </h2>
    <mat-dialog-content>
      <p class="summary"><strong>{{ data.jobTitle }}</strong> – {{ data.company }}</p>
      @if (data.attachments.length) {
        <p class="current">Aplikacja ma już załączników: {{ data.attachments.length }}. Nowe pliki zostaną dodane do istniejących.</p>
      }
      <app-attachments-picker
        [items]="items()"
        (itemsChange)="onItemsChange($event)"
        [existing]="data.attachments"
        [progress]="progress()"
        [errorMessage]="error()"
        [statusMessage]="statusMessage()" />
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" class="cancel-button" (click)="cancel()">
        {{ uploading() ? 'Przerwij wysyłanie' : 'Anuluj' }}
      </button>
      <button mat-raised-button color="primary" type="button" class="upload-button" (click)="upload()"
        [disabled]="!items().length || uploading()">
        Prześlij
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    mat-dialog-content { min-width: 460px; }
    h2 { display: flex; align-items: center; gap: 12px; }
    .summary { margin: 0 0 12px; }
    .current { margin: 0 0 16px; color: var(--text-secondary); }
    @media (max-width: 600px) { mat-dialog-content { min-width: 0; } }
  `]
})
export class AttachmentsDialogComponent {
  private applicationService = inject(JobApplicationService);
  private dialogRef = inject<MatDialogRef<AttachmentsDialogComponent, AttachmentsDialogResult>>(MatDialogRef);
  private destroyRef = inject(DestroyRef);
  private upload$: Subscription | null = null;

  readonly data = inject<JobApplication>(MAT_DIALOG_DATA);
  readonly items = signal<AttachmentUploadItem[]>([]);
  readonly progress = signal<number | null>(null);
  readonly error = signal<string | null>(null);
  readonly statusMessage = signal<string | null>(null);
  readonly uploading = signal(false);

  constructor() {
    // Escape i kliknięcie obok obsługujemy sami: w trakcie wysyłania Escape przerywa żądanie i zamyka dialog,
    // a przypadkowe kliknięcie obok niczego nie przerywa
    this.dialogRef.disableClose = true;
    this.dialogRef.keydownEvents().pipe(takeUntilDestroyed()).subscribe(event => {
      if (event.key === 'Escape' && !event.defaultPrevented) {
        event.preventDefault();
        this.cancel();
      }
    });
    this.dialogRef.backdropClick().pipe(takeUntilDestroyed()).subscribe(() => {
      if (!this.uploading()) this.cancel();
    });
  }

  onItemsChange(items: AttachmentUploadItem[]): void {
    this.items.set(items);
    this.error.set(null);
    this.statusMessage.set(null);
  }

  upload(): void {
    const items = this.items();
    if (!items.length || this.uploading()) return;

    this.uploading.set(true);
    this.progress.set(0);
    this.error.set(null);
    this.statusMessage.set(null);

    this.upload$ = this.applicationService.uploadAttachments(this.data.id, items)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (event) => {
          if (event.type === 'progress') {
            this.progress.set(event.percent);
          } else {
            this.upload$ = null;
            // nie trzymamy referencji do plików dłużej niż trzeba
            this.items.set([]);
            this.dialogRef.close(event.application);
          }
        },
        error: (error) => {
          console.error('Error uploading attachments:', error);
          this.upload$ = null;
          this.uploading.set(false);
          this.progress.set(null);
          this.statusMessage.set('Przerwano wysyłanie załączników');
          this.error.set(isHandledByInterceptor(error)
            ? 'Brak uprawnień do dodania załączników'
            : getApiErrorMessage(error, 'Nie udało się przesłać załączników'));
        }
      });
  }

  /** Anuluj / Przerwij wysyłanie / Escape: przerywa żądanie od razu (nie czekając na animację zamknięcia). */
  cancel(): void {
    const wasUploading = this.uploading();
    this.upload$?.unsubscribe();
    this.upload$ = null;
    this.uploading.set(false);
    this.items.set([]);
    this.dialogRef.close(wasUploading ? ATTACHMENTS_UPLOAD_ABORTED : undefined);
  }
}
