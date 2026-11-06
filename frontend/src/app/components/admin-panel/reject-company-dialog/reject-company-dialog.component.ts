import { Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Company } from '../../../models/company.model';
import { AdminService } from '../../../services/admin.service';
import { getApiErrorMessage, isHandledByInterceptor } from '../../../utils/http-errors';

export const REJECTION_REASON_MAX = 1000;

/** Odrzucenie zgłoszenia firmy z wymaganym powodem (widocznym dla pracodawcy). Zamyka się zaktualizowaną firmą. */
@Component({
  selector: 'app-reject-company-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule
  ],
  template: `
    <h2 mat-dialog-title>
      <mat-icon>block</mat-icon>
      Odrzuć firmę „{{ company.name }}”
    </h2>
    <mat-dialog-content>
      <p class="hint">Powód zobaczy pracodawca w profilu firmy – po poprawieniu danych może wysłać zgłoszenie ponownie.</p>
      <mat-form-field appearance="outline" class="reason-field">
        <mat-label>Powód odrzucenia</mat-label>
        <textarea matInput [formControl]="reason" rows="4" [maxlength]="reasonMax"></textarea>
        <mat-hint align="end">{{ reason.value.length }} / {{ reasonMax }}</mat-hint>
        @if (reason.invalid && reason.touched) {
          <mat-error>Podaj powód odrzucenia</mat-error>
        }
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close [disabled]="submitting()">Anuluj</button>
      <button mat-raised-button color="warn" (click)="onSubmit()" [disabled]="reason.invalid || submitting()">
        @if (submitting()) {
          <mat-spinner diameter="20"></mat-spinner>
        } @else {
          <ng-container>
            <mat-icon>block</mat-icon>
            Odrzuć
          </ng-container>
        }
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    h2 { display: flex; align-items: center; gap: 12px; }
    mat-dialog-content { min-width: min(460px, 80vw); }
    .reason-field { width: 100%; }
    .hint { margin: 0 0 12px; color: var(--text-secondary); font-size: 0.875rem; }
  `]
})
export class RejectCompanyDialogComponent {
  private adminService = inject(AdminService);
  private dialogRef = inject(MatDialogRef<RejectCompanyDialogComponent, Company>);
  private snackBar = inject(MatSnackBar);

  readonly company: Company = inject<Company>(MAT_DIALOG_DATA);
  readonly reasonMax = REJECTION_REASON_MAX;
  readonly reason = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.pattern(/\S/), Validators.maxLength(REJECTION_REASON_MAX)]
  });
  readonly submitting = signal(false);

  onSubmit(): void {
    if (this.reason.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.adminService.rejectCompany(this.company.id, this.reason.value.trim()).subscribe({
      next: updated => {
        this.submitting.set(false);
        this.notify(`Firma „${updated.name}” została odrzucona`);
        this.dialogRef.close(updated);
      },
      error: error => {
        console.error('Error rejecting company:', error);
        this.submitting.set(false);
        if (!isHandledByInterceptor(error)) {
          this.notify(getApiErrorMessage(error, 'Błąd podczas odrzucania firmy'));
        }
      }
    });
  }

  private notify(message: string): void {
    this.snackBar.open(message, 'OK', { duration: 5000 });
  }
}
