import { Component, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
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
    MatProgressSpinnerModule,
    StatusChipComponent
  ],
  template: `
    <h2 mat-dialog-title>Zmień status aplikacji</h2>
    <mat-dialog-content>
      <p class="summary"><strong>{{ data.userFullName || data.userEmail }}</strong> · {{ data.jobTitle }}</p>
      <p class="current">
        Obecny status:
        <app-status-chip [label]="currentPresentation.label" [tone]="currentPresentation.tone" />
      </p>

      <mat-form-field appearance="outline" class="full-width">
        <mat-label>Nowy status</mat-label>
        <mat-select [ngModel]="status()" (ngModelChange)="status.set($event)" cdkFocusInitial>
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
        <mat-hint>{{ commentHint() }}</mat-hint>
        <mat-hint align="end">{{ comment().length }} / {{ commentMaxLength }}</mat-hint>
      </mat-form-field>

      @if (commentTooLong()) {
        <p class="info error" role="alert">Komentarz może mieć maksymalnie {{ commentMaxLength }} znaków.</p>
      }
      @if (isNoteOnly() && !hasComment()) {
        <p class="info">Wybierz inny status albo dodaj komentarz, aby zapisać notatkę.</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close [disabled]="saving()">Anuluj</button>
      <button mat-flat-button type="button" class="save-button" (click)="save()" [disabled]="!canSave()">
        @if (saving()) {
          <mat-spinner diameter="20" aria-label="Zapisywanie"></mat-spinner>
        } @else {
          {{ isNoteOnly() ? 'Dodaj notatkę' : 'Zapisz' }}
        }
      </button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [`
    mat-dialog-content { min-width: min(420px, 80vw); }
    .full-width { width: 100%; margin-top: 8px; }
    .summary { margin: 0 0 8px; color: var(--color-text-secondary); }
    .summary strong { color: var(--color-text); font-weight: 500; }
    .current { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin: 0 0 8px; color: var(--color-text-secondary); }
    .info { margin: 8px 0 0; color: var(--color-text-secondary); font-size: var(--font-size-sm); }
    .info.error { color: var(--color-danger); }
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
  /** E-mail wysyłany jest tylko przy zmianie statusu (i gdy kandydat ma włączone powiadomienia). */
  readonly commentHint = computed(() => this.isNoteOnly()
    ? 'Kandydat zobaczy ten komentarz w historii aplikacji'
    : 'Kandydat zobaczy ten komentarz w historii aplikacji i w powiadomieniu e-mail o zmianie statusu');
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
          this.notify(getApiErrorMessage(error, 'Błąd podczas zmiany statusu aplikacji'), 'assertive');
        }
      }
    });
  }

  private notify(message: string, politeness: 'polite' | 'assertive' = 'polite'): void {
    this.snackBar.open(message, 'OK', { duration: 5000, politeness });
  }
}
