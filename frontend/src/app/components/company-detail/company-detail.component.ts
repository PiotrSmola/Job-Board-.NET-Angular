import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { providePolishPaginatorIntl } from '../../services/polish-paginator-intl';
import { EMPTY, Observable, Subject, catchError, map, of, switchMap, tap } from 'rxjs';
import { Company, CompanyStatus } from '../../models/company.model';
import { CompanyPhoto } from '../../models/company-media.model';
import { JobOffer } from '../../models/job-offer.model';
import { JobParams } from '../../models/job-params.model';
import { PaginatedResult } from '../../models/pagination.model';
import { CompanyService } from '../../services/company.service';
import { CompanyMediaService } from '../../services/company-media.service';
import { JobService } from '../../services/job.service';
import { PageTitleService } from '../../services/page-title.service';
import { getApiErrorMessage } from '../../utils/http-errors';
import { safeWebsiteUrl } from '../../utils/company-form';
import { getCompanyStatusPresentation } from '../../utils/status-labels';
import { formatJobSalary } from '../../utils/job-labels';
import { StatusChipComponent } from '../shared/status-chip.component';
import { formatFullDate, formatJobMeta } from '../shared/job-taxonomy.component';
import { CompanyGalleryComponent } from './company-gallery.component';

/** Publiczny profil firmy (makieta Firmy): dane, galeria zdjęć i lista aktywnych ofert. */
@Component({
  selector: 'app-company-detail',
  providers: [providePolishPaginatorIntl()],
  standalone: true,
  imports: [
    RouterLink,
    MatButtonModule,
    MatIconModule,
    MatPaginatorModule,
    StatusChipComponent,
    CompanyGalleryComponent
  ],
  templateUrl: './company-detail.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './company-detail.component.scss'
})
export class CompanyDetailComponent {
  private route = inject(ActivatedRoute);
  private companyService = inject(CompanyService);
  private mediaService = inject(CompanyMediaService);
  private jobService = inject(JobService);
  private pageTitle = inject(PageTitleService);

  readonly company = signal<Company | null>(null);
  readonly loading = signal(false);
  readonly notFound = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly jobs = signal<JobOffer[]>([]);
  readonly jobsTotal = signal(0);
  readonly jobsLoading = signal(false);
  readonly jobsPageIndex = signal(0);
  readonly jobsPageSize = 10;

  readonly photos = signal<CompanyPhoto[]>([]);
  /** Galeria w trakcie wczytywania – miejsce jest zarezerwowane (bez przesunięć układu). */
  readonly photosLoading = signal(false);

  readonly websiteUrl = computed(() => safeWebsiteUrl(this.company()?.website));
  /** Adres strony bez protokołu jako tekst linku („acme.example.com”). */
  readonly websiteLabel = computed(() => (this.websiteUrl() ?? '').replace(/^https?:\/\//i, '').replace(/\/$/, ''));
  /** Status pokazujemy tylko, gdy API go zwróciło i firma nie jest zatwierdzona (widok admina / członka firmy). */
  readonly statusPresentation = computed(() => {
    const status = this.company()?.status;
    return status === null || status === undefined || status === CompanyStatus.Approved
      ? null
      : getCompanyStatusPresentation(status);
  });
  readonly since = computed(() => {
    const createdAt = this.company()?.createdAt;
    return createdAt ? formatFullDate(createdAt) : null;
  });
  readonly jobRows = computed(() => this.jobs().map(job => ({ job, meta: formatJobMeta(job), salary: formatJobSalary(job) })));

  private companyId = 0;
  /** Kolejne wczytania profilu – switchMap anuluje poprzednie żądanie (szybka zmiana :id, ponowna próba). */
  private readonly companyLoad$ = new Subject<number>();
  /** true – wczytaj oferty bieżącej firmy, false – anuluj trwające wczytywanie (zmiana firmy). */
  private readonly jobsLoad$ = new Subject<boolean>();
  /** Firma, której zdjęcia wczytać (null – anuluj, np. zmiana firmy). */
  private readonly photosLoad$ = new Subject<Company | null>();

  constructor() {
    this.jobsLoad$.pipe(
      switchMap(load => load ? this.fetchJobs() : EMPTY),
      takeUntilDestroyed()
    ).subscribe(result => this.onJobsLoaded(result));

    this.photosLoad$.pipe(
      switchMap(company => company ? this.fetchPhotos(company) : EMPTY),
      takeUntilDestroyed()
    ).subscribe(photos => {
      this.photos.set(photos);
      this.photosLoading.set(false);
    });

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
    // oferty i zdjęcia poprzedniej firmy nie mogą nadpisać list
    this.jobsLoad$.next(false);
    this.photosLoad$.next(null);
    this.jobs.set([]);
    this.jobsTotal.set(0);
    this.jobsLoading.set(false);
    this.photos.set([]);
    this.photosLoading.set(false);
  }

  private onCompanyLoaded(company: Company): void {
    this.company.set(company);
    this.loading.set(false);
    this.pageTitle.set(company.name);
    this.jobsLoad$.next(true);
    if (this.hasPublicPhotos(company)) {
      this.photosLoading.set(true);
      this.photosLoad$.next(company);
    }
  }

  /**
   * Zdjęcia firmy niezatwierdzonej API serwuje tylko członkom i adminom z JWT, którego `<img src>` nie wysyła –
   * na profilu publicznym pokazujemy więc galerię tylko firm zatwierdzonych (status pominięty = zatwierdzona).
   */
  private hasPublicPhotos(company: Company): boolean {
    const approved = company.status === null || company.status === undefined || company.status === CompanyStatus.Approved;
    return approved && (company.photosCount ?? 1) > 0;
  }

  private onCompanyError(error: unknown): void {
    console.error('Error loading company:', error);
    this.loading.set(false);
    this.pageTitle.set('Firma niedostępna');
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

  formatSalary(job: JobOffer): string {
    return formatJobSalary(job);
  }

  private fetchPhotos(company: Company): Observable<CompanyPhoto[]> {
    // błąd galerii nie psuje profilu – sekcja się nie pokazuje
    return this.mediaService.getPhotos(company.id).pipe(
      map(photos => Array.isArray(photos) ? photos : []),
      catchError(error => {
        console.error('Error loading company photos:', error);
        return of([] as CompanyPhoto[]);
      })
    );
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
