import { Component, DestroyRef, inject, signal, ChangeDetectorRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { AbstractControl, ReactiveFormsModule, FormBuilder, FormGroup, ValidationErrors, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MAT_DATE_LOCALE, provideNativeDateAdapter } from '@angular/material/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { JobService } from '../../../services/job.service';
import { CategoryService } from '../../../services/category.service';
import { JobOffer, CreateJobOffer, JobOfferStatus, WorkType, Currency, SalaryPeriod } from '../../../models/job-offer.model';
import { Category } from '../../../models/taxonomy.model';
import { Company, CompanyStatus } from '../../../models/company.model';
import { PanelMode } from '../../../models/panel-mode.model';
import { getApiErrorMessage, isHandledByInterceptor } from '../../../utils/http-errors';
import { JOB_STATUS_OPTIONS } from '../../../utils/status-labels';
import { CompanyAutocompleteComponent, CompanyOption } from '../../shared/company-autocomplete.component';
import { SkillsInputComponent } from '../../shared/skills-input.component';
import {
  CONTRACT_TYPE_OPTIONS, CURRENCY_OPTIONS, SALARY_PERIOD_OPTIONS, SENIORITY_OPTIONS, getCurrencyLabel, hasFlag, setFlag
} from '../../../utils/job-labels';
import { MAX_SKILLS, isValidSkillName } from '../../../utils/job-query';

export interface JobDialogData {
  /** null = tryb dodawania, oferta = tryb edycji. */
  job: JobOffer | null;
  mode: PanelMode;
  /** Firma pracodawcy (tryb employer) – decyduje, czy wolno publikować. */
  company?: Company | null;
}

/** Koniec dnia (czas lokalny admina) – oferta jest ważna do końca wybranego dnia. */
export function endOfLocalDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate(), 23, 59, 59, 999);
}

/** Puste pole wynagrodzenia = null; 0 jest poprawną wartością i zostaje wysłane. */
export function toSalary(value: number | string | null | undefined): number | null {
  return value === null || value === undefined || value === '' ? null : Number(value);
}

/** Te same reguły co API: 0-15 umiejętności, każda 1-50 znaków (bez przecinka - rozdziela listy w filtrach). */
function skillsValidator(control: AbstractControl): ValidationErrors | null {
  const skills: string[] = control.value ?? [];
  return skills.length <= MAX_SKILLS && skills.every(isValidSkillName) ? null : { skillsInvalid: true };
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
    MatDatepickerModule,
    MatCheckboxModule,
    CompanyAutocompleteComponent,
    SkillsInputComponent
  ],
  providers: [provideNativeDateAdapter(), { provide: MAT_DATE_LOCALE, useValue: 'pl-PL' }],
  template: `
    <h2 mat-dialog-title>{{ job ? 'Edytuj ofertę' : 'Nowa oferta' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="jobForm" class="job-form">
        <mat-form-field appearance="outline">
          <mat-label>Tytuł stanowiska</mat-label>
          <input matInput formControlName="title" placeholder="np. Senior Frontend Developer" required cdkFocusInitial>
          @if (jobForm.get('title')?.hasError('required') && jobForm.get('title')?.touched) {
            <mat-error>Tytuł jest wymagany</mat-error>
          }
        </mat-form-field>

        @if (isAdmin) {
          <app-company-autocomplete
            source="approved"
            [required]="true"
            requiredMessage="Wybierz zatwierdzoną firmę z listy"
            [initial]="initialCompany"
            (companyChange)="onCompanyChange($event)" />
        }

        <mat-form-field appearance="outline">
          <mat-label>Lokalizacja</mat-label>
          <input matInput formControlName="location" placeholder="np. Warszawa" required>
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
          @if (jobForm.get('workType')?.hasError('required') && jobForm.get('workType')?.touched) {
            <mat-error>Typ pracy jest wymagany</mat-error>
          }
        </mat-form-field>

        <div class="salary-row">
          <mat-form-field appearance="outline">
            <mat-label>Kategoria</mat-label>
            <mat-select formControlName="categoryId" [canSelectNullableOptions]="true">
              <mat-option [value]="null">Brak</mat-option>
              @for (category of categories(); track category.id) {
                <mat-option [value]="category.id">{{ category.name }}</mat-option>
              }
            </mat-select>
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Poziom</mat-label>
            <mat-select formControlName="seniority" [canSelectNullableOptions]="true">
              <mat-option [value]="null">Nie określono</mat-option>
              @for (option of seniorityOptions; track option.value) {
                <mat-option [value]="option.value">{{ option.label }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        </div>

        <fieldset class="contract-types">
          <legend>Typ umowy</legend>
          @for (option of contractOptions; track option.value) {
            <mat-checkbox [checked]="hasContract(option.value)" (change)="toggleContract(option.value, $event.checked)">
              {{ option.label }}
            </mat-checkbox>
          }
        </fieldset>

        <div class="salary-row">
          <mat-form-field appearance="outline">
            <mat-label>Waluta</mat-label>
            <mat-select formControlName="salaryCurrency">
              @for (option of currencyOptions; track option.value) {
                <mat-option [value]="option.value">{{ option.label }}</mat-option>
              }
            </mat-select>
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Okres wynagrodzenia</mat-label>
            <mat-select formControlName="salaryPeriod">
              @for (option of periodOptions; track option.value) {
                <mat-option [value]="option.value">{{ option.label }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        </div>

        <div class="salary-row">
          <mat-form-field appearance="outline" class="salary-field">
            <mat-label>Wynagrodzenie od</mat-label>
            <input matInput type="number" min="0" formControlName="salaryMin" placeholder="8000">
            <span matTextSuffix>&nbsp;{{ currencyLabel }}</span>
            @if (jobForm.get('salaryMin')?.hasError('min')) {
              <mat-error>Wynagrodzenie nie może być ujemne</mat-error>
            }
          </mat-form-field>

          <mat-form-field appearance="outline" class="salary-field">
            <mat-label>Wynagrodzenie do</mat-label>
            <input matInput type="number" min="0" formControlName="salaryMax" placeholder="15000">
            <span matTextSuffix>&nbsp;{{ currencyLabel }}</span>
            @if (jobForm.get('salaryMax')?.hasError('min')) {
              <mat-error>Wynagrodzenie nie może być ujemne</mat-error>
            }
          </mat-form-field>
        </div>
        @if (jobForm.hasError('salaryRangeInvalid') && (jobForm.get('salaryMin')?.touched || jobForm.get('salaryMax')?.touched)) {
          <p class="form-error" role="alert">Wynagrodzenie „od” nie może być wyższe niż „do”</p>
        }

        <app-skills-input
          label="Umiejętności"
          [allowCreate]="true"
          [skills]="jobForm.value.skills"
          (skillsChange)="onSkillsChange($event)" />

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

        @if (!canPublish) {
          <div class="publish-info warning-strip" role="note">
            <span>Firma nie została jeszcze zatwierdzona przez administratora – ofertę możesz zapisać tylko jako szkic. Publikacja będzie możliwa po weryfikacji firmy.</span>
          </div>
        }

        <div class="salary-row">
          <mat-form-field appearance="outline">
            <mat-label>Status</mat-label>
            <mat-select formControlName="status">
              @for (option of statusOptions; track option.value) {
                <mat-option [value]="option.value">{{ option.label }}</mat-option>
              }
            </mat-select>
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
          <p class="form-error expiry-error" role="alert">Aktywna oferta musi mieć datę wygaśnięcia w przyszłości</p>
        }
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close [disabled]="submitting()">Anuluj</button>
      <button mat-flat-button type="button" class="submit-button" (click)="onSubmit()" [disabled]="jobForm.invalid || submitting()">
        @if (submitting()) {
          <mat-spinner diameter="20" aria-label="Zapisywanie"></mat-spinner>
        } @else {
          {{ job ? 'Zapisz' : 'Dodaj ofertę' }}
        }
      </button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [`
    mat-dialog-content { width: min(560px, 80vw); max-width: 100%; }

    .job-form {
      display: flex;
      flex-direction: column;
      gap: 8px;

      mat-form-field { width: 100%; }

      .salary-row {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(min(200px, 100%), 1fr));
        gap: 0 16px;
      }

      .contract-types {
        margin: 0 0 12px;
        padding: 0;
        border: 0;

        legend { padding: 0; margin-bottom: 4px; color: var(--color-text-secondary); }
        mat-checkbox { display: inline-block; margin-right: 16px; }
      }

      .form-error {
        margin: -8px 0 8px;
        color: var(--color-danger);
        font-size: var(--font-size-sm);
      }

      .publish-info { margin-bottom: 8px; }

      .salary-field input[type=number] {
        -moz-appearance: textfield;
        appearance: textfield;

        &::-webkit-outer-spin-button,
        &::-webkit-inner-spin-button { -webkit-appearance: none; margin: 0; }
      }
    }
  `]})
export class JobDialogComponent {
  private fb = inject(FormBuilder);
  private jobService = inject(JobService);
  private dialogRef = inject(MatDialogRef<JobDialogComponent>);
  private snackBar = inject(MatSnackBar);
  private categoryService = inject(CategoryService);
  private destroyRef = inject(DestroyRef);
  private cdr = inject(ChangeDetectorRef);

  readonly categories = signal<Category[]>([]);
  readonly seniorityOptions = SENIORITY_OPTIONS;
  readonly contractOptions = CONTRACT_TYPE_OPTIONS;
  readonly currencyOptions = CURRENCY_OPTIONS;
  readonly periodOptions = SALARY_PERIOD_OPTIONS;

  readonly data: JobDialogData = inject<JobDialogData | null>(MAT_DIALOG_DATA, { optional: true }) ?? { job: null, mode: 'admin' };
  // null = tryb dodawania, oferta = tryb edycji
  readonly job: JobOffer | null = this.data.job ?? null;
  readonly isAdmin = this.data.mode !== 'employer';
  /** Pracodawca firmy niezatwierdzonej może zapisywać tylko szkice (API zwraca 403 przy publikacji). */
  readonly canPublish = this.isAdmin || this.data.company?.status === CompanyStatus.Approved;
  readonly initialCompany: CompanyOption | null = this.job ? { id: this.job.companyId, name: this.job.company } : null;
  readonly submitting = signal(false);
  WorkType = WorkType;
  readonly statusOptions = this.canPublish
    ? JOB_STATUS_OPTIONS
    // bez prawa publikacji: szkic + ewentualnie bieżący status edytowanej oferty (bez zmiany)
    : JOB_STATUS_OPTIONS.filter(o => o.value === JobOfferStatus.Draft || o.value === this.job?.status);

  jobForm: FormGroup = this.fb.group({
    title: ['', [Validators.required]],
    // id firmy wybranej w podpowiedziach (tylko admin – pracodawca publikuje zawsze w swojej firmie)
    companyId: [this.initialCompany?.id ?? null as number | null, [Validators.required]],
    location: ['', [Validators.required]],
    workType: [WorkType.Remote, [Validators.required]],
    // Te same reguły co walidacja API (wynagrodzenia >= 0, "Od" <= "Do") – bez nich backend zwraca 400
    salaryMin: [null, [Validators.min(0)]],
    salaryMax: [null, [Validators.min(0)]],
    description: ['', [Validators.required, Validators.minLength(50)]],
    // domyślnie Active – jak w API (oferta od razu opublikowana); bez prawa publikacji – szkic
    status: [this.canPublish ? JobOfferStatus.Active : JobOfferStatus.Draft, [Validators.required]],
    expiresAt: [null as Date | null],
    categoryId: [null as number | null],
    seniority: [null as number | null],
    contractTypes: [0],
    salaryCurrency: [Currency.PLN],
    salaryPeriod: [SalaryPeriod.Month],
    skills: [[] as string[], [skillsValidator]]
  }, { validators: [this.salaryRangeValidator, (group: AbstractControl) => this.expiresAtValidator(group as FormGroup)] });

  constructor() {
    // OnPush: zmiany z nakładek (kalendarz, lista wyboru) i programowe patchValue odświeżają gettery szablonu
    this.jobForm.events.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.cdr.markForCheck());

    this.categoryService.getCategories().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: categories => this.categories.set(categories),
      // Bez listy kategorii formularz nadal działa (kategoria jest opcjonalna)
      error: error => console.error('Error loading categories:', error)
    });

    if (!this.isAdmin) {
      this.jobForm.removeControl('companyId');
    }

    if (this.job) {
      this.jobForm.patchValue({
        title: this.job.title,
        location: this.job.location,
        workType: this.job.workType,
        salaryMin: this.job.salaryMin,
        salaryMax: this.job.salaryMax,
        description: this.job.description,
        status: this.job.status ?? JobOfferStatus.Active,
        expiresAt: this.job.expiresAt ? new Date(this.job.expiresAt) : null,
        categoryId: this.job.categoryId ?? null,
        seniority: this.job.seniority ?? null,
        contractTypes: this.job.contractTypes ?? 0,
        salaryCurrency: this.job.salaryCurrency ?? Currency.PLN,
        salaryPeriod: this.job.salaryPeriod ?? SalaryPeriod.Month,
        skills: [...(this.job.skills ?? [])]
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
    const original = this.job?.expiresAt;
    return this.job?.status === JobOfferStatus.Active && !!original && isSameLocalDay(new Date(original), value);
  }

  onCompanyChange(company: CompanyOption | null): void {
    const control = this.jobForm.get('companyId');
    control?.setValue(company?.id ?? null);
    control?.markAsTouched();
  }

  get currencyLabel(): string {
    return getCurrencyLabel(this.jobForm.value.salaryCurrency);
  }

  hasContract(flag: number): boolean {
    return hasFlag(this.jobForm.value.contractTypes ?? 0, flag);
  }

  toggleContract(flag: number, on: boolean): void {
    const control = this.jobForm.get('contractTypes');
    control?.setValue(setFlag(control.value ?? 0, flag, on));
    control?.markAsDirty();
  }

  onSkillsChange(skills: string[]): void {
    const control = this.jobForm.get('skills');
    control?.setValue(skills);
    control?.markAsDirty();
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
    if (this.jobForm.invalid || this.submitting()) return;

    this.submitting.set(true);
    const formValue = this.jobForm.value;

    const jobData: CreateJobOffer = {
      ...formValue,
      // API ignoruje companyId pracodawcy (zawsze jego firma) – wysyłamy jego firmę dla spójności
      companyId: this.isAdmin ? formValue.companyId : this.data.company?.id ?? this.job?.companyId ?? null,
      seniority: formValue.seniority ?? null,
      categoryId: formValue.categoryId ?? null,
      salaryMin: toSalary(formValue.salaryMin),
      salaryMax: toSalary(formValue.salaryMax),
      expiresAt: this.toExpiresAtIso(formValue.expiresAt)
    };

    const request = this.job
      ? this.jobService.updateJob(this.job.id, jobData)
      : this.jobService.createJob(jobData);

    request.subscribe({
      next: () => {
        this.submitting.set(false);
        this.notify(this.job ? 'Oferta została zapisana' : 'Oferta została dodana');
        this.dialogRef.close(true);
      },
      error: (error) => {
        this.submitting.set(false);
        console.error('Error saving job:', error);
        if (!isHandledByInterceptor(error)) {
          this.notify(getApiErrorMessage(error, 'Błąd podczas zapisywania oferty'), 'assertive');
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

    const original = this.job?.expiresAt ?? null;
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

  private notify(message: string, politeness: 'polite' | 'assertive' = 'polite'): void {
    this.snackBar.open(message, 'OK', { duration: 5000, politeness });
  }
}
