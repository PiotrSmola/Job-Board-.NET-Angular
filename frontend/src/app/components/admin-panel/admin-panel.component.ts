import { Component, inject, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators, FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatSortModule } from '@angular/material/sort';
import { JobService } from '../../services/job.service';
import { JobOffer, CreateJobOffer, WorkType } from '../../models/job-offer.model';

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

      // Ukryj steppers (strzałki) dla input[type=number]
      .salary-field input[type=number] {
        -moz-appearance: textfield;
        appearance: textfield;

        &::-webkit-outer-spin-button,
        &::-webkit-inner-spin-button {
          -webkit-appearance: none;
          margin: 0;
        }
      }

      // Style dla prefix/suffix
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

  data: JobOffer | null = null;
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

  salaryRangeValidator(group: FormGroup): { [key: string]: boolean } | null {
    const salaryMin = group.get('salaryMin')?.value;
    const salaryMax = group.get('salaryMax')?.value;

    // If both are null or undefined, no validation error
    if (!salaryMin || !salaryMax) {
      return null;
    }

    // If salaryMin is greater than salaryMax, return error
    if (Number(salaryMin) > Number(salaryMax)) {
      return { salaryRangeInvalid: true };
    }

    return null;
  }

  onSubmit(): void {
    if (this.jobForm.invalid) return;

    this.submitting = true;
    const formValue = this.jobForm.value;

    // Ensure salary fields are null if empty, not empty strings
    const jobData: CreateJobOffer = {
      ...formValue,
      salaryMin: formValue.salaryMin || null,
      salaryMax: formValue.salaryMax || null
    };

    const request = this.data
      ? this.jobService.updateJob(this.data.id, jobData)
      : this.jobService.createJob(jobData);

    request.subscribe({
      next: (job) => {
        this.submitting = false;
        this.cdr.detectChanges();
        alert(this.data ? 'Oferta zaktualizowana!' : 'Oferta dodana!');
        this.dialogRef.close(true); // Close dialog and signal success
      },
      error: (error) => {
        this.submitting = false;
        this.cdr.detectChanges();
        console.error('Error saving job:', error);
        const errorMessage = error.error?.message || error.error?.title || error.message || 'Błąd podczas zapisywania oferty';
        alert(errorMessage);
      }
    });
  }
}

@Component({
  selector: 'app-admin-panel',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatProgressSpinnerModule,
    MatDialogModule,
    MatTooltipModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatPaginatorModule,
    MatSortModule
  ],
  templateUrl: './admin-panel.component.html',
  styleUrl: './admin-panel.component.scss'
})
export class AdminPanelComponent implements OnInit {
  private jobService = inject(JobService);
  private dialog = inject(MatDialog);
  private cdr = inject(ChangeDetectorRef);

  jobs: JobOffer[] = [];
  filteredJobs: JobOffer[] = [];
  displayedColumns: string[] = ['id', 'title', 'company', 'location', 'workType', 'salary', 'createdAt', 'actions'];
  loading = false;
  deleting = false;
  deletingId: number | null = null;

  // Pagination
  totalItems = 0;
  pageSize = 10;
  pageIndex = 0;
  pageSizeOptions = [5, 10, 25, 50];

  // Filtering & Sorting
  searchTerm = '';
  selectedWorkType: WorkType | null = null;
  sortColumn = 'id';
  sortDirection: 'asc' | 'desc' = 'asc';

  WorkType = WorkType;
  workTypeOptions = [
    { value: null, label: 'Wszystkie' },
    { value: WorkType.Remote, label: 'Zdalna' },
    { value: WorkType.Hybrid, label: 'Hybrydowa' },
    { value: WorkType.Onsite, label: 'Stacjonarna' }
  ];

  ngOnInit(): void {
    this.loadJobs();
  }

  loadJobs(): void {
    this.loading = true;
    this.jobService.getAllJobs().subscribe({
      next: (jobs) => {
        this.jobs = jobs;
        this.applyFiltersAndSort();
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (error) => {
        console.error('Error loading jobs:', error);
        this.loading = false;
        this.cdr.detectChanges();
        alert('Błąd podczas ładowania ofert');
      }
    });
  }

  applyFiltersAndSort(): void {
    let result = [...this.jobs];

    // Search filter
    if (this.searchTerm) {
      const term = this.searchTerm.toLowerCase();
      result = result.filter(job =>
        job.title.toLowerCase().includes(term) ||
        job.company.toLowerCase().includes(term) ||
        job.location.toLowerCase().includes(term) ||
        (job.description && job.description.toLowerCase().includes(term))
      );
    }

    // Work type filter
    if (this.selectedWorkType !== null) {
      result = result.filter(job => job.workType === this.selectedWorkType);
    }

    // Sorting
    result.sort((a, b) => {
      let aValue: any;
      let bValue: any;

      switch (this.sortColumn) {
        case 'id':
          aValue = a.id;
          bValue = b.id;
          break;
        case 'title':
          aValue = a.title.toLowerCase();
          bValue = b.title.toLowerCase();
          break;
        case 'company':
          aValue = a.company.toLowerCase();
          bValue = b.company.toLowerCase();
          break;
        case 'location':
          aValue = a.location.toLowerCase();
          bValue = b.location.toLowerCase();
          break;
        case 'workType':
          aValue = a.workType;
          bValue = b.workType;
          break;
        case 'salary':
          aValue = a.salaryMax ?? 0;
          bValue = b.salaryMax ?? 0;
          break;
        case 'createdAt':
          aValue = new Date(a.createdAt).getTime();
          bValue = new Date(b.createdAt).getTime();
          break;
        default:
          aValue = a.id;
          bValue = b.id;
      }

      if (aValue < bValue) return this.sortDirection === 'asc' ? -1 : 1;
      if (aValue > bValue) return this.sortDirection === 'asc' ? 1 : -1;
      return 0;
    });

    this.totalItems = result.length;
    this.filteredJobs = result;
  }

  get paginatedJobs(): JobOffer[] {
    const startIndex = this.pageIndex * this.pageSize;
    return this.filteredJobs.slice(startIndex, startIndex + this.pageSize);
  }

  onSearch(): void {
    this.pageIndex = 0;
    this.applyFiltersAndSort();
  }

  onFilterChange(): void {
    this.pageIndex = 0;
    this.applyFiltersAndSort();
  }

  onSort(column: string): void {
    if (this.sortColumn === column) {
      this.sortDirection = this.sortDirection === 'asc' ? 'desc' : 'asc';
    } else {
      this.sortColumn = column;
      this.sortDirection = 'asc';
    }
    this.applyFiltersAndSort();
  }

  onPageChange(event: any): void {
    this.pageIndex = event.pageIndex;
    this.pageSize = event.pageSize;
  }

  clearFilters(): void {
    this.searchTerm = '';
    this.selectedWorkType = null;
    this.sortColumn = 'id';
    this.sortDirection = 'asc';
    this.pageIndex = 0;
    this.applyFiltersAndSort();
  }

  openAddDialog(): void {
    const dialogRef = this.dialog.open(JobDialogComponent, {
      width: '600px',
      disableClose: true
    });

    dialogRef.componentInstance.data = null;

    dialogRef.afterClosed().subscribe((result) => {
      if (result !== undefined) {
        this.loadJobs(); // Refresh the list
      }
    });
  }

  openEditDialog(job: JobOffer): void {
    const dialogRef = this.dialog.open(JobDialogComponent, {
      width: '600px',
      disableClose: true
    });

    dialogRef.componentInstance.data = job;
    dialogRef.componentInstance.jobForm.patchValue({
      title: job.title,
      company: job.company,
      location: job.location,
      workType: job.workType,
      salaryMin: job.salaryMin,
      salaryMax: job.salaryMax,
      description: job.description
    });

    dialogRef.afterClosed().subscribe((result) => {
      if (result !== undefined) {
        this.loadJobs(); // Refresh the list
      }
    });
  }

  deleteJob(id: number): void {
    if (!confirm('Czy na pewno chcesz usunąć tę ofertę? Wszystkie aplikacje na to stanowisko również zostaną usunięte.')) {
      return;
    }

    this.deleting = true;
    this.deletingId = id;
    this.jobService.deleteJob(id).subscribe({
      next: () => {
        this.jobs = this.jobs.filter(job => job.id !== id);
        this.deleting = false;
        this.deletingId = null;
        this.cdr.detectChanges();
        alert('Oferta została usunięta');
      },
      error: (error) => {
        console.error('Error deleting job:', error);
        this.deleting = false;
        this.deletingId = null;
        this.cdr.detectChanges();
        alert('Błąd podczas usuwania oferty');
      }
    });
  }

  getWorkTypeLabel(workType: WorkType): string {
    switch (workType) {
      case WorkType.Remote: return 'Zdalna';
      case WorkType.Hybrid: return 'Hybrydowa';
      case WorkType.Onsite: return 'Stacjonarna';
      default: return 'Nieznany';
    }
  }

  formatSalary(salaryMin: number | null, salaryMax: number | null): string {
    if (!salaryMin && !salaryMax) return 'Nieokreślona';
    if (salaryMin && salaryMax) return `${salaryMin} - ${salaryMax} PLN`;
    if (salaryMin) return `Od ${salaryMin} PLN`;
    if (salaryMax) return `Do ${salaryMax} PLN`;
    return 'Nieokreślona';
  }
}
