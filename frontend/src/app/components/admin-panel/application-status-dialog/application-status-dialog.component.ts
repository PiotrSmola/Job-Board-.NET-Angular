import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { JobApplicationService } from '../../../services/job-application.service';
import { ApplicationStatus, JobApplication } from '../../../models/job-application.model';
import { getApiErrorMessage, isHandledByInterceptor } from '../../../utils/http-errors';
import { RECRUITER_STATUS_OPTIONS, getApplicationStatusPresentation } from '../../../utils/status-labels';
import { StatusChipComponent } from '../../shared/status-chip.component';

export const COMMENT_MAX_LENGTH = 1000;

/** Zmiana statusu aplikacji przez rekrutera. Zamyka się z zaktualizowaną aplikacją (z historią) albo undefined. */
@Component({
  selector: 'app-application-status-dialog',
  standalone: true,
  imports: [
    FormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    StatusChipComponent
  ],
  template: `
    <h2 mat-dialog-title>
      <mat-icon>swap_horiz</mat-icon>
      Zmień status aplikacji
    </h2>
    <mat-dialog-content>
      <p class="summary">
        <strong>{{ data.userFullName || data.userEmail }}</strong> – {{ data.jobTitle }}
      </p>
      <p class="current">
        Obecny status:
        <app-status-chip [label]="currentPresentation.label" [tone]="currentPresentation.tone" />
      </p>

      <mat-form-field appearance="outline" class="full-width">
        <mat-label>Nowy status</mat-label>
        <mat-select [ngModel]="status()" (ngModelChange)="status.set($event)">
          @for (option of statusOptions; track option.value) {
            <mat-option [value]="option.value">{{ option.label }}</mat-option>
          }
        </mat-select>
      </mat-form-field>

      <mat-form-field appearance="outline" class="full-width">
        <mat-label>Komentarz (opcjonalnie)</mat-label>
        <textarea matInput rows="4" [maxlength]="commentMaxLength"
          [ngModel]="comment()" (ngModelChange)="comment.set($event)"
          placeholder="np. Zapraszamy na rozmowę w czwartek o 10:00"></textarea>
        <mat-hint>Komentarz będzie widoczny dla kandydata w historii aplikacji</mat-hint>
        <mat-hint align="end">{{ comment().length }} / {{ commentMaxLength }}</mat-hint>
      </mat-form-field>

      @if (commentTooLong()) {
        <p class="info error">Komentarz może mieć maksymalnie {{ commentMaxLength }} znaków.</p>
      }
      @if (isNoteOnly() && !hasComment()) {
        <p class="info">Wybierz inny status albo dodaj komentarz, aby zapisać notatkę.</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close [disabled]="saving()">Anuluj</button>
      <button mat-raised-button color="primary" (click)="save()" [disabled]="!canSave()">
        @if (saving()) {
          <mat-spinner diameter="20"></mat-spinner>
        } @else {
          {{ isNoteOnly() ? 'Dodaj notatkę' : 'Zapisz' }}
        }
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    mat-dialog-content { min-width: 420px; }
    h2 { display: flex; align-items: center; gap: 12px; }
    .full-width { width: 100%; margin-top: 8px; }
    .summary { margin: 0 0 8px; }
    .current { display: flex; align-items: center; gap: 8px; margin: 0 0 8px; color: var(--text-secondary); }
    .info { margin: 8px 0 0; color: var(--text-secondary); font-size: 0.875rem; }
    .info.error { color: #f44336; }
    @media (max-width: 600px) { mat-dialog-content { min-width: 0; } }
  `]
})
export class ApplicationStatusDialogComponent {
  private applicationService = inject(JobApplicationService);
  private dialogRef = inject(MatDialogRef<ApplicationStatusDialogComponent, JobApplication>);
  private snackBar = inject(MatSnackBar);

  readonly data = inject<JobApplication>(MAT_DIALOG_DATA);
  readonly statusOptions = RECRUITER_STATUS_OPTIONS;
  readonly commentMaxLength = COMMENT_MAX_LENGTH;
  readonly currentPresentation = getApplicationStatusPresentation(this.data.status);

  readonly status = signal<ApplicationStatus>(this.data.status);
  readonly comment = signal('');
  readonly saving = signal(false);

  readonly hasComment = computed(() => this.comment().trim().length > 0);
  /** Atrybut maxlength nie chroni przed wartością ustawioną programowo (np. wklejoną przez skrypt) – API odrzuca > 1000. */
  readonly commentTooLong = computed(() => this.comment().trim().length > COMMENT_MAX_LENGTH);
  /** Ten sam status = notatka; API wymaga wtedy niepustego komentarza. */
  readonly isNoteOnly = computed(() => this.status() === this.data.status);
  readonly canSave = computed(() =>
    !this.saving()
    && this.status() !== ApplicationStatus.Withdrawn
    && !this.commentTooLong()
    && (!this.isNoteOnly() || this.hasComment()));

  save(): void {
    if (!this.canSave()) return;

    this.saving.set(true);
    const comment = this.comment().trim();
    this.applicationService.updateStatus(this.data.id, { status: this.status(), comment: comment || null }).subscribe({
      next: (updated) => {
        this.saving.set(false);
        this.notify(this.isNoteOnly() ? 'Notatka została dodana' : 'Status aplikacji został zmieniony');
        this.dialogRef.close(updated);
      },
      error: (error) => {
        console.error('Error changing application status:', error);
        this.saving.set(false);
        if (!isHandledByInterceptor(error)) {
          this.notify(getApiErrorMessage(error, 'Błąd podczas zmiany statusu aplikacji'));
        }
      }
    });
  }

  private notify(message: string): void {
    this.snackBar.open(message, 'OK', { duration: 5000 });
  }
}
