import { Component, inject, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { JobService } from '../../../services/job.service';
import { JobOffer, CreateJobOffer, WorkType } from '../../../models/job-offer.model';
import { getApiErrorMessage, isHandledByInterceptor } from '../../../utils/http-errors';

@Component({
  selector: 'app-job-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule
  ],
  template: `
    <h2 mat-dialog-title>
      <mat-icon>{{ data ? 'edit' : 'add' }}</mat-icon>
      {{ data ? 'Edytuj ofertę' : 'Dodaj nową ofertę' }}
    </h2>
    <mat-dialog-content>
      <form [formGroup]="jobForm" class="job-form">
        <mat-form-field appearance="outline">
          <mat-label>Tytuł stanowiska</mat-label>
          <input matInput formControlName="title" placeholder="np. Senior Frontend Developer">
          <mat-icon matPrefix>work</mat-icon>
          @if (jobForm.get('title')?.hasError('required') && jobForm.get('title')?.touched) {
            <mat-error>Tytuł jest wymagany</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Firma</mat-label>
          <input matInput formControlName="company" placeholder="np. TechCorp">
          <mat-icon matPrefix>business</mat-icon>
          @if (jobForm.get('company')?.hasError('required') && jobForm.get('company')?.touched) {
            <mat-error>Firma jest wymagana</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Lokalizacja</mat-label>
          <input matInput formControlName="location" placeholder="np. Warszawa">
          <mat-icon matPrefix>location_on</mat-icon>
          @if (jobForm.get('location')?.hasError('required') && jobForm.get('location')?.touched) {
            <mat-error>Lokalizacja jest wymagana</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Typ pracy</mat-label>
          <mat-select formControlName="workType">
            <mat-option [value]="WorkType.Remote">Zdalna</mat-option>
            <mat-option [value]="WorkType.Hybrid">Hybrydowa</mat-option>
            <mat-option [value]="WorkType.Onsite">Stacjonarna</mat-option>
          </mat-select>
          <mat-icon matPrefix>work_outline</mat-icon>
          @if (jobForm.get('workType')?.hasError('required') && jobForm.get('workType')?.touched) {
            <mat-error>Typ pracy jest wymagany</mat-error>
          }
        </mat-form-field>

        <div class="salary-row">
          <mat-form-field appearance="outline" class="salary-field">
            <mat-label>Wynagrodzenie od</mat-label>
            <input matInput type="number" formControlName="salaryMin" placeholder="8000">
            <span matTextPrefix>$&nbsp;</span>
            <span matTextSuffix>&nbsp;PLN</span>
          </mat-form-field>

          <mat-form-field appearance="outline" class="salary-field">
            <mat-label>Wynagrodzenie do</mat-label>
            <input matInput type="number" formControlName="salaryMax" placeholder="15000">
            <span matTextPrefix>$&nbsp;</span>
            <span matTextSuffix>&nbsp;PLN</span>
          </mat-form-field>
        </div>
        @if (jobForm.hasError('salaryRangeInvalid') && (jobForm.get('salaryMin')?.touched || jobForm.get('salaryMax')?.touched)) {
          <div class="salary-error">
            <mat-error>Wynagrodzenie "Od" nie może być wyższe niż "Do"</mat-error>
          </div>
        }

        <mat-form-field appearance="outline">
          <mat-label>Opis stanowiska</mat-label>
          <textarea matInput formControlName="description" rows="6" placeholder="Opisz szczegóły stanowiska..."></textarea>
          @if (jobForm.get('description')?.hasError('required') && jobForm.get('description')?.touched) {
            <mat-error>Opis jest wymagany</mat-error>
          }
          @if (jobForm.get('description')?.hasError('minlength')) {
            <mat-error>Opis musi mieć minimum 50 znaków</mat-error>
          }
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>
        <mat-icon>cancel</mat-icon>
        Anuluj
      </button>
      <button mat-raised-button color="primary" (click)="onSubmit()" [disabled]="jobForm.invalid || submitting">
        @if (submitting) {
          <mat-spinner diameter="20"></mat-spinner>
        } @else {
          <ng-container>
            <mat-icon>{{ data ? 'save' : 'add' }}</mat-icon>
            {{ data ? 'Zapisz' : 'Dodaj' }}
          </ng-container>
        }
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    mat-dialog-content {
      min-width: 500px;
      max-width: 600px;
      padding: 24px;
    }

    h2 {
      display: flex;
      align-items: center;
      gap: 12px;
      color: #e0e0e0;
    }

    .job-form {
      display: flex;
      flex-direction: column;
      gap: 16px;

      mat-form-field {
        width: 100%;
      }

      .salary-row {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: 16px;
      }

      .salary-error {
        margin-top: -12px;
        margin-bottom: 8px;

        mat-error {
          color: #f44336;
          font-size: 12px;
          display: flex;
          align-items: center;
          gap: 4px;
        }
      }

      .salary-field input[type=number] {
        -moz-appearance: textfield;
        appearance: textfield;

        &::-webkit-outer-spin-button,
        &::-webkit-inner-spin-button {
          -webkit-appearance: none;
          margin: 0;
        }
      }

      .salary-field ::ng-deep .mat-mdc-form-field-text-prefix,
      .salary-field ::ng-deep .mat-mdc-form-field-text-suffix {
        color: var(--text-secondary);
        font-weight: 500;
        padding: 0 4px;
      }
    }

    mat-dialog-actions {
      padding: 16px 24px;

      button {
        display: flex;
        align-items: center;
        gap: 8px;
      }
    }
  `]
})
export class JobDialogComponent {
  private fb = inject(FormBuilder);
  private jobService = inject(JobService);
  private dialogRef = inject(MatDialogRef<JobDialogComponent>);
  private cdr = inject(ChangeDetectorRef);
  private snackBar = inject(MatSnackBar);

  // null = tryb dodawania, oferta = tryb edycji
  data: JobOffer | null = inject<JobOffer | null>(MAT_DIALOG_DATA, { optional: true }) ?? null;
  submitting = false;
  WorkType = WorkType;

  jobForm: FormGroup = this.fb.group({
    title: ['', [Validators.required]],
    company: ['', [Validators.required]],
    location: ['', [Validators.required]],
    workType: [WorkType.Remote, [Validators.required]],
    salaryMin: [null],
    salaryMax: [null],
    description: ['', [Validators.required, Validators.minLength(50)]]
  }, { validators: this.salaryRangeValidator });

  constructor() {
    if (this.data) {
      this.jobForm.patchValue({
        title: this.data.title,
        company: this.data.company,
        location: this.data.location,
        workType: this.data.workType,
        salaryMin: this.data.salaryMin,
        salaryMax: this.data.salaryMax,
        description: this.data.description
      });
    }
  }

  salaryRangeValidator(group: FormGroup): { [key: string]: boolean } | null {
    const salaryMin = group.get('salaryMin')?.value;
    const salaryMax = group.get('salaryMax')?.value;

    if (!salaryMin || !salaryMax) {
      return null;
    }

    if (Number(salaryMin) > Number(salaryMax)) {
      return { salaryRangeInvalid: true };
    }

    return null;
  }

  onSubmit(): void {
    if (this.jobForm.invalid) return;

    this.submitting = true;
    const formValue = this.jobForm.value;

    const jobData: CreateJobOffer = {
      ...formValue,
      salaryMin: formValue.salaryMin || null,
      salaryMax: formValue.salaryMax || null
    };

    const request = this.data
      ? this.jobService.updateJob(this.data.id, jobData)
      : this.jobService.createJob(jobData);

    request.subscribe({
      next: () => {
        this.submitting = false;
        this.cdr.detectChanges();
        this.notify(this.data ? 'Oferta zaktualizowana!' : 'Oferta dodana!');
        this.dialogRef.close(true);
      },
      error: (error) => {
        this.submitting = false;
        this.cdr.detectChanges();
        console.error('Error saving job:', error);
        if (!isHandledByInterceptor(error)) {
          this.notify(getApiErrorMessage(error, 'Błąd podczas zapisywania oferty'));
        }
      }
    });
  }

  private notify(message: string): void {
    this.snackBar.open(message, 'OK', { duration: 5000 });
  }
}
