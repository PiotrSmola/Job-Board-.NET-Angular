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
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MAT_DATE_LOCALE, provideNativeDateAdapter } from '@angular/material/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { JobService } from '../../../services/job.service';
import { JobOffer, CreateJobOffer, JobOfferStatus, WorkType } from '../../../models/job-offer.model';
import { getApiErrorMessage, isHandledByInterceptor } from '../../../utils/http-errors';
import { JOB_STATUS_OPTIONS } from '../../../utils/status-labels';

/** Koniec dnia (czas lokalny admina) – oferta jest ważna do końca wybranego dnia. */
export function endOfLocalDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate(), 23, 59, 59, 999);
}

/** Puste pole wynagrodzenia = null; 0 jest poprawną wartością i zostaje wysłane. */
export function toSalary(value: number | string | null | undefined): number | null {
  return value === null || value === undefined || value === '' ? null : Number(value);
}

function isSameLocalDay(a: Date, b: Date): boolean {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
}

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
    MatProgressSpinnerModule,
    MatDatepickerModule
  ],
  providers: [provideNativeDateAdapter(), { provide: MAT_DATE_LOCALE, useValue: 'pl-PL' }],
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
            <input matInput type="number" min="0" formControlName="salaryMin" placeholder="8000">
            <span matTextPrefix>$&nbsp;</span>
            <span matTextSuffix>&nbsp;PLN</span>
            @if (jobForm.get('salaryMin')?.hasError('min')) {
              <mat-error>Wynagrodzenie nie może być ujemne</mat-error>
            }
          </mat-form-field>

          <mat-form-field appearance="outline" class="salary-field">
            <mat-label>Wynagrodzenie do</mat-label>
            <input matInput type="number" min="0" formControlName="salaryMax" placeholder="15000">
            <span matTextPrefix>$&nbsp;</span>
            <span matTextSuffix>&nbsp;PLN</span>
            @if (jobForm.get('salaryMax')?.hasError('min')) {
              <mat-error>Wynagrodzenie nie może być ujemne</mat-error>
            }
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

        <div class="salary-row">
          <mat-form-field appearance="outline">
            <mat-label>Status</mat-label>
            <mat-select formControlName="status">
              @for (option of statusOptions; track option.value) {
                <mat-option [value]="option.value">{{ option.label }}</mat-option>
              }
            </mat-select>
            <mat-icon matPrefix>flag</mat-icon>
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Aktualna do</mat-label>
            <input matInput [matDatepicker]="expiresPicker" formControlName="expiresAt" readonly
              placeholder="bezterminowo" (click)="expiresPicker.open()">
            @if (jobForm.get('expiresAt')?.value) {
              <button mat-icon-button matSuffix type="button" (click)="clearExpiresAt()" aria-label="Usuń datę">
                <mat-icon>close</mat-icon>
              </button>
            }
            <mat-datepicker-toggle matSuffix [for]="expiresPicker"></mat-datepicker-toggle>
            <mat-datepicker #expiresPicker></mat-datepicker>
            <mat-hint>Do końca wybranego dnia; puste = bezterminowo</mat-hint>
          </mat-form-field>
        </div>
        @if (jobForm.hasError('expiresAtInPast')) {
          <div class="salary-error expiry-error">
            <mat-error>Aktywna oferta musi mieć datę wygaśnięcia w przyszłości</mat-error>
          </div>
        }
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
  readonly statusOptions = JOB_STATUS_OPTIONS;

  jobForm: FormGroup = this.fb.group({
    title: ['', [Validators.required]],
    company: ['', [Validators.required]],
    location: ['', [Validators.required]],
    workType: [WorkType.Remote, [Validators.required]],
    // Te same reguły co walidacja API (wynagrodzenia >= 0, "Od" <= "Do") – bez nich backend zwraca 400
    salaryMin: [null, [Validators.min(0)]],
    salaryMax: [null, [Validators.min(0)]],
    description: ['', [Validators.required, Validators.minLength(50)]],
    // domyślnie Active – jak w API (oferta od razu opublikowana)
    status: [JobOfferStatus.Active, [Validators.required]],
    expiresAt: [null as Date | null]
  }, { validators: [this.salaryRangeValidator, group => this.expiresAtValidator(group as FormGroup)] });

  constructor() {
    if (this.data) {
      this.jobForm.patchValue({
        title: this.data.title,
        company: this.data.company,
        location: this.data.location,
        workType: this.data.workType,
        salaryMin: this.data.salaryMin,
        salaryMax: this.data.salaryMax,
        description: this.data.description,
        status: this.data.status ?? JobOfferStatus.Active,
        expiresAt: this.data.expiresAt ? new Date(this.data.expiresAt) : null
      });
    }
  }

  /**
   * Aktywna oferta z datą wygaśnięcia musi być ważna jeszcze dziś (API odrzuca datę z przeszłości).
   * Wyjątek jak w API: wygasłą aktywną ofertę można edytować bez zmiany daty (zostaje wygasła).
   */
  expiresAtValidator(group: FormGroup): { [key: string]: boolean } | null {
    const status = group.get('status')?.value;
    const expiresAt: Date | null = group.get('expiresAt')?.value ?? null;

    if (status !== JobOfferStatus.Active || !expiresAt || endOfLocalDay(expiresAt).getTime() > Date.now()) {
      return null;
    }

    return this.keepsOriginalExpiresAt(expiresAt) ? null : { expiresAtInPast: true };
  }

  /** Edycja oferty, która już jest aktywna, z niezmienionym dniem wygaśnięcia. */
  private keepsOriginalExpiresAt(value: Date): boolean {
    const original = this.data?.expiresAt;
    return this.data?.status === JobOfferStatus.Active && !!original && isSameLocalDay(new Date(original), value);
  }

  clearExpiresAt(): void {
    this.jobForm.patchValue({ expiresAt: null });
  }

  salaryRangeValidator(group: FormGroup): { [key: string]: boolean } | null {
    const salaryMin = group.get('salaryMin')?.value;
    const salaryMax = group.get('salaryMax')?.value;

    // Puste pole = brak ograniczenia; 0 jest poprawną wartością
    if (salaryMin === null || salaryMin === '' || salaryMax === null || salaryMax === '') {
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
      salaryMin: toSalary(formValue.salaryMin),
      salaryMax: toSalary(formValue.salaryMax),
      expiresAt: this.toExpiresAtIso(formValue.expiresAt)
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

  /**
   * Wybrany dzień → koniec tego dnia w strefie admina, wysłany jako ISO UTC.
   * Niezmieniony dzień przy edycji zachowuje dokładną datę z API (bez przesuwania godziny) –
   * także dla wygasłej aktywnej oferty, którą API pozwala wtedy zapisać.
   */
  private toExpiresAtIso(value: Date | null): string | null {
    if (!value) return null;

    const original = this.data?.expiresAt ?? null;
    if (original && isSameLocalDay(new Date(original), value)) {
      // wyjątek: aktywna oferta, której godzina wygaśnięcia minęła dziś – przesuwamy na koniec dnia (znów aktualna)
      const now = Date.now();
      const expiredEarlierToday = this.jobForm.value.status === JobOfferStatus.Active
        && new Date(original).getTime() <= now && endOfLocalDay(value).getTime() > now;
      if (!expiredEarlierToday) {
        return original;
      }
    }

    return endOfLocalDay(value).toISOString();
  }

  private notify(message: string): void {
    this.snackBar.open(message, 'OK', { duration: 5000 });
  }
}
