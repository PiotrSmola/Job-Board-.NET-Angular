import { Component, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
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
import { COMPANY_DESCRIPTION_MAX, buildCompanyForm, toSaveCompany } from '../../../utils/company-form';

/** Dodawanie (firma od razu zatwierdzona) i edycja danych firmy przez admina. Zamyka się zapisaną firmą. */
@Component({
  selector: 'app-company-dialog',
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
      <mat-icon>{{ company ? 'edit' : 'add_business' }}</mat-icon>
      {{ company ? 'Edytuj firmę' : 'Dodaj firmę' }}
    </h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="company-form">
        <mat-form-field appearance="outline">
          <mat-label>Nazwa firmy</mat-label>
          <input matInput formControlName="name" maxlength="200">
          <mat-icon matPrefix>business</mat-icon>
          @if (form.get('name')?.invalid && form.get('name')?.touched) {
            <mat-error>Nazwa firmy jest wymagana (maks. 200 znaków)</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Lokalizacja</mat-label>
          <input matInput formControlName="location" maxlength="200" placeholder="np. Rzeszów">
          <mat-icon matPrefix>location_on</mat-icon>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Strona www</mat-label>
          <input matInput formControlName="website" maxlength="300" placeholder="https://...">
          <mat-icon matPrefix>language</mat-icon>
          @if (form.get('website')?.invalid) {
            <mat-error>Podaj adres zaczynający się od http:// lub https://</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Opis</mat-label>
          <textarea matInput formControlName="description" rows="5" [maxlength]="descriptionMax"></textarea>
          <mat-hint align="end">{{ form.get('description')?.value?.length ?? 0 }} / {{ descriptionMax }}</mat-hint>
        </mat-form-field>

        @if (!company) {
          <p class="hint">Firma dodana przez administratora jest od razu zatwierdzona.</p>
        }
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close [disabled]="submitting()">Anuluj</button>
      <button mat-raised-button color="primary" (click)="onSubmit()" [disabled]="form.invalid || submitting()">
        @if (submitting()) {
          <mat-spinner diameter="20"></mat-spinner>
        } @else {
          <ng-container>
            <mat-icon>{{ company ? 'save' : 'add' }}</mat-icon>
            {{ company ? 'Zapisz' : 'Dodaj' }}
          </ng-container>
        }
      </button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: [`
    h2 { display: flex; align-items: center; gap: 12px; }
    mat-dialog-content { min-width: min(500px, 80vw); }
    .company-form { display: flex; flex-direction: column; gap: 8px; }
    .company-form mat-form-field { width: 100%; }
    .hint { margin: 0; color: var(--text-secondary); font-size: 0.875rem; }
  `]
})
export class CompanyDialogComponent {
  private fb = inject(FormBuilder);
  private adminService = inject(AdminService);
  private dialogRef = inject(MatDialogRef<CompanyDialogComponent, Company>);
  private snackBar = inject(MatSnackBar);

  // null = dodawanie, firma = edycja
  readonly company: Company | null = inject<Company | null>(MAT_DIALOG_DATA, { optional: true }) ?? null;
  readonly descriptionMax = COMPANY_DESCRIPTION_MAX;
  readonly form = buildCompanyForm(this.fb, this.company);
  readonly submitting = signal(false);

  onSubmit(): void {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    const body = toSaveCompany(this.form.value);
    const request = this.company
      ? this.adminService.updateCompany(this.company.id, body)
      : this.adminService.createCompany(body);

    request.subscribe({
      next: saved => {
        this.submitting.set(false);
        this.notify(this.company ? 'Dane firmy zapisane' : 'Firma dodana');
        this.dialogRef.close(saved);
      },
      error: error => {
        console.error('Error saving company:', error);
        this.submitting.set(false);
        if (!isHandledByInterceptor(error)) {
          this.notify(getApiErrorMessage(error, 'Błąd podczas zapisywania firmy'));
        }
      }
    });
  }

  private notify(message: string): void {
    this.snackBar.open(message, 'OK', { duration: 5000 });
  }
}
