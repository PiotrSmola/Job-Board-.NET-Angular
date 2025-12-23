import { Component, inject, OnInit, ChangeDetectorRef, OnDestroy } from '@angular/core';
import { Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { JobService } from '../../services/job.service';
import { JobOffer, WorkType } from '../../models/job-offer.model';
import { JobParams } from '../../models/job-params.model';
import { Subject, debounceTime, distinctUntilChanged, takeUntil } from 'rxjs';

@Component({
  selector: 'app-job-list',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatPaginatorModule,
    MatChipsModule,
    MatProgressSpinnerModule,
    MatButtonToggleModule
  ],
  templateUrl: './job-list.component.html',
  styleUrl: './job-list.component.scss'
})
export class JobListComponent implements OnInit, OnDestroy {
  private jobService = inject(JobService);
  private router = inject(Router);
  private cdr = inject(ChangeDetectorRef);

  jobs: JobOffer[] = [];
  loading = false;
  totalItems = 0;

  jobParams = new JobParams();
  private searchSubject = new Subject<string>();
  private salaryFilterSubject = new Subject<{ min?: number, max?: number }>();
  private destroy$ = new Subject<void>();

  WorkType = WorkType;
  workTypeOptions = [
    { value: null, label: 'Wszystkie' },
    { value: WorkType.Remote, label: 'Zdalna' },
    { value: WorkType.Hybrid, label: 'Hybrydowa' },
    { value: WorkType.Onsite, label: 'Stacjonarna' }
  ];

  sortOptions = [
    { value: 'newest', label: 'Najnowsze' },
    { value: 'oldest', label: 'Najstarsze' },
    { value: 'salaryHigh', label: 'Najwyższa płaca' },
    { value: 'salaryLow', label: 'Najniższa płaca' }
  ];

  ngOnInit(): void {
    this.loadJobs();

    this.searchSubject.pipe(
      debounceTime(500),
      distinctUntilChanged(),
      takeUntil(this.destroy$)
    ).subscribe(searchTerm => {
      this.jobParams.searchTerm = searchTerm;
      this.jobParams.pageNumber = 1;
      this.loadJobs();
    });

    this.salaryFilterSubject.pipe(
      debounceTime(800),
      takeUntil(this.destroy$)
    ).subscribe(salary => {
      if (salary.min && salary.max && salary.min > salary.max) {
        alert('Wynagrodzenie "Od" nie może być wyższe niż "Do"');
        return;
      }

      this.jobParams.minSalary = salary.min ?? null;
      this.jobParams.maxSalary = salary.max ?? null;
      this.jobParams.pageNumber = 1;
      this.loadJobs();
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  loadJobs(): void {
    this.loading = true;
    this.jobService.getJobs(this.jobParams).subscribe({
      next: (result) => {
        console.log('✅ Jobs loaded - Full result:', result);
        console.log('📊 Pagination object:', result.pagination);
        console.log('📈 Total items:', result.pagination.TotalItems);
        console.log('📦 Jobs array length:', result.result.length);
        this.jobs = result.result;
        this.totalItems = result.pagination.TotalItems;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (error) => {
        console.error('❌ Error loading jobs:', error);
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  onPageChange(event: PageEvent): void {
    this.jobParams.pageNumber = event.pageIndex + 1;
    this.jobParams.pageSize = event.pageSize;
    this.loadJobs();
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  onSearchTermChange(term: string): void {
    this.searchSubject.next(term);
  }

  onSearch(): void {
    this.jobParams.pageNumber = 1;
    this.loadJobs();
  }

  onSalaryChange(): void {
    this.salaryFilterSubject.next({
      min: this.jobParams.minSalary ?? undefined,
      max: this.jobParams.maxSalary ?? undefined
    });
  }

  onFilterChange(): void {
    this.jobParams.pageNumber = 1;
    this.loadJobs();
  }

  clearSearch(): void {
    this.jobParams.searchTerm = '';
    this.onSearchTermChange('');
  }

  clearFilters(): void {
    this.jobParams = new JobParams();
    this.loadJobs();
  }

  viewJobDetails(id: number): void {
    this.router.navigate(['/jobs', id]);
  }

  getWorkTypeLabel(workType: WorkType): string {
    const option = this.workTypeOptions.find(o => o.value === workType);
    return option?.label || 'Nieznany';
  }

  formatSalary(min: number | null, max: number | null): string {
    if (!min && !max) return 'Nieokreślona';
    if (min && max) return `${min} - ${max} PLN`;
    if (min) return `Od ${min} PLN`;
    if (max) return `Do ${max} PLN`;
    return 'Nieokreślona';
  }
}
