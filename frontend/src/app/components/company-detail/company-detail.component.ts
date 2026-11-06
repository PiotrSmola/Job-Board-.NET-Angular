import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { EMPTY, Observable, Subject, catchError, map, of, switchMap, tap } from 'rxjs';
import { Company, CompanyStatus } from '../../models/company.model';
import { JobOffer, WorkType } from '../../models/job-offer.model';
import { JobParams } from '../../models/job-params.model';
import { PaginatedResult } from '../../models/pagination.model';
import { CompanyService } from '../../services/company.service';
import { JobService } from '../../services/job.service';
import { getApiErrorMessage } from '../../utils/http-errors';
import { safeWebsiteUrl } from '../../utils/company-form';
import { getCompanyStatusPresentation } from '../../utils/status-labels';
import { formatJobSalary } from '../../utils/job-labels';
import { StatusChipComponent } from '../shared/status-chip.component';

const WORK_TYPE_LABELS: Record<WorkType, string> = {
  [WorkType.Remote]: 'Zdalna',
  [WorkType.Hybrid]: 'Hybrydowa',
  [WorkType.Onsite]: 'Stacjonarna'
};

/** Publiczny profil firmy z listą jej aktywnych ofert. */
@Component({
  selector: 'app-company-detail',
  standalone: true,
  imports: [
    DatePipe,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatDividerModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressSpinnerModule,
    StatusChipComponent
  ],
  templateUrl: './company-detail.component.html',
  styleUrl: './company-detail.component.scss'
})
export class CompanyDetailComponent {
  private route = inject(ActivatedRoute);
  private companyService = inject(CompanyService);
  private jobService = inject(JobService);

  readonly company = signal<Company | null>(null);
  readonly loading = signal(false);
  readonly notFound = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly jobs = signal<JobOffer[]>([]);
  readonly jobsTotal = signal(0);
  readonly jobsLoading = signal(false);
  readonly jobsPageIndex = signal(0);
  readonly jobsPageSize = 10;

  readonly websiteUrl = computed(() => safeWebsiteUrl(this.company()?.website));
  /** Status pokazujemy tylko, gdy API go zwróciło i firma nie jest zatwierdzona (widok admina / członka firmy). */
  readonly statusPresentation = computed(() => {
    const status = this.company()?.status;
    return status === null || status === undefined || status === CompanyStatus.Approved
      ? null
      : getCompanyStatusPresentation(status);
  });

  private companyId = 0;
  /** Kolejne wczytania profilu – switchMap anuluje poprzednie żądanie (szybka zmiana :id, ponowna próba). */
  private readonly companyLoad$ = new Subject<number>();
  /** true – wczytaj oferty bieżącej firmy, false – anuluj trwające wczytywanie (zmiana firmy). */
  private readonly jobsLoad$ = new Subject<boolean>();

  constructor() {
    this.jobsLoad$.pipe(
      switchMap(load => load ? this.fetchJobs() : EMPTY),
      takeUntilDestroyed()
    ).subscribe(result => this.onJobsLoaded(result));

    this.companyLoad$.pipe(
      tap(() => this.startCompanyLoad()),
      switchMap(id => this.companyService.getCompany(id).pipe(
        map(company => ({ company, error: null as unknown })),
        catchError(error => of({ company: null, error }))
      )),
      takeUntilDestroyed()
    ).subscribe(({ company, error }) => company ? this.onCompanyLoaded(company) : this.onCompanyError(error));

    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe(params => {
      this.companyId = Number(params.get('id'));
      this.jobsPageIndex.set(0);
      this.loadCompany();
    });
  }

  loadCompany(): void {
    this.companyLoad$.next(this.companyId);
  }

  private startCompanyLoad(): void {
    this.loading.set(true);
    this.notFound.set(false);
    this.loadError.set(null);
    this.company.set(null);
    // oferty poprzedniej firmy nie mogą nadpisać listy
    this.jobsLoad$.next(false);
    this.jobs.set([]);
    this.jobsTotal.set(0);
    this.jobsLoading.set(false);
  }

  private onCompanyLoaded(company: Company): void {
    this.company.set(company);
    this.loading.set(false);
    this.jobsLoad$.next(true);
  }

  private onCompanyError(error: unknown): void {
    console.error('Error loading company:', error);
    this.loading.set(false);
    if (error instanceof HttpErrorResponse && (error.status === 404 || error.status === 400)) {
      this.notFound.set(true);
    } else {
      this.loadError.set(getApiErrorMessage(error, 'Nie udało się wczytać profilu firmy'));
    }
  }

  onJobsPageChange(event: PageEvent): void {
    this.jobsPageIndex.set(event.pageIndex);
    this.jobsLoad$.next(true);
  }

  workTypeLabel(workType: WorkType): string {
    return WORK_TYPE_LABELS[workType] ?? 'Nieznany';
  }

  formatSalary(job: JobOffer): string {
    return formatJobSalary(job);
  }

  private fetchJobs(): Observable<PaginatedResult<JobOffer> | null> {
    this.jobsLoading.set(true);
    return this.jobService.getJobs(this.buildJobParams()).pipe(
      catchError(error => {
        console.error('Error loading company jobs:', error);
        return of(null);
      })
    );
  }

  private buildJobParams(): JobParams {
    const params = new JobParams();
    params.companyId = this.companyId;
    params.pageNumber = this.jobsPageIndex() + 1;
    params.pageSize = this.jobsPageSize;
    return params;
  }

  private onJobsLoaded(result: PaginatedResult<JobOffer> | null): void {
    this.jobs.set(result?.result ?? []);
    this.jobsTotal.set(result?.pagination.TotalItems ?? 0);
    this.jobsLoading.set(false);
  }
}
