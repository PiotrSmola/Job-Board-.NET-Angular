import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DatePipe, SlicePipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Observable, Subject, catchError, distinctUntilChanged, map, of, switchMap, tap } from 'rxjs';
import { JobService } from '../../services/job.service';
import { CategoryService } from '../../services/category.service';
import { JobOffer } from '../../models/job-offer.model';
import { JobParams } from '../../models/job-params.model';
import { PaginatedResult } from '../../models/pagination.model';
import { Category, JobFacets } from '../../models/taxonomy.model';
import { getApiErrorMessage } from '../../utils/http-errors';
import { getActiveFilters } from '../../utils/active-filters';
import { PAGE_SIZE_OPTIONS, cloneParams, filtersKey, paramsFromQuery, urlQuery } from '../../utils/job-query';
import { formatJobSalary, getWorkTypeLabel, offersNoun } from '../../utils/job-labels';
import { formatExpiresSoon } from '../../utils/status-labels';
import { JobTaxonomyComponent } from '../shared/job-taxonomy.component';
import { RemovableChipsComponent } from '../shared/removable-chips.component';
import { FilterChange, JobFiltersComponent } from './job-filters.component';
import { SaveJobButtonComponent } from '../shared/save-job-button.component';

/** Liczba szkieletów kart zarezerwowanych na czas pierwszego ładowania (bez przesunięć układu). */
const SKELETON_COUNT = 6;

/**
 * Lista ofert. Źródłem prawdy o filtrach jest adres URL (query params): zmiany w panelu nawigują,
 * a wczytanie ofert i facet następuje po zmianie adresu – dzięki temu działają linki i przycisk „wstecz”.
 */
@Component({
  selector: 'app-job-list',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    SlicePipe,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatPaginatorModule,
    RouterLink,
    JobFiltersComponent,
    JobTaxonomyComponent,
    RemovableChipsComponent,
    SaveJobButtonComponent
  ],
  templateUrl: './job-list.component.html',
  styleUrl: './job-list.component.scss'
})
export class JobListComponent {
  private jobService = inject(JobService);
  private categoryService = inject(CategoryService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  readonly pageSizeOptions = PAGE_SIZE_OPTIONS;
  readonly skeletons = Array.from({ length: SKELETON_COUNT }, (_, i) => i);
  readonly getWorkTypeLabel = getWorkTypeLabel;
  readonly formatSalary = formatJobSalary;

  /** Parametry odczytane z adresu URL. */
  readonly params = signal(new JobParams());
  readonly jobs = signal<JobOffer[]>([]);
  readonly facets = signal<JobFacets | null>(null);
  readonly categories = signal<Category[]>([]);
  readonly loading = signal(true);
  /** Czy zakończyło się już pierwsze ładowanie (do tego czasu pokazujemy szkielety kart). */
  readonly loaded = signal(false);
  readonly totalItems = signal(0);
  /** Liczba wyników ostatniego udanego ładowania (null = brak / błąd) – ogłaszana czytnikom ekranu. */
  readonly resultCount = signal<number | null>(null);
  /** Komunikat błędu ostatniego ładowania (null = brak błędu). */
  readonly loadError = signal<string | null>(null);

  readonly activeFilters = computed(() => getActiveFilters(this.params(), this.categories()));
  readonly chips = computed(() => this.activeFilters().map(f => ({ key: f.key, label: f.label, removeLabel: f.removeLabel })));
  readonly resultsNoun = computed(() => offersNoun(this.resultCount() ?? 0));

  private readonly jobsRequest$ = new Subject<JobParams>();
  private readonly facetsRequest$ = new Subject<JobParams>();
  /** Docelowe parametry: adres URL + zmiany, których nawigacja jeszcze nie zakończyła. */
  private target = new JobParams();

  constructor() {
    // switchMap anuluje poprzednie żądanie – wolniejsza, starsza odpowiedź nie nadpisze nowszej
    this.jobsRequest$.pipe(
      tap(() => {
        this.loading.set(true);
        this.loadError.set(null);
      }),
      switchMap(params => this.fetchJobs(params)),
      takeUntilDestroyed()
    ).subscribe(result => this.onJobsLoaded(result));

    this.facetsRequest$.pipe(
      distinctUntilChanged((a, b) => filtersKey(a) === filtersKey(b)),
      switchMap(params => this.jobService.getFacets(params).pipe(catchError(() => of(null)))),
      takeUntilDestroyed()
    ).subscribe(facets => {
      // Błąd facet nie jest krytyczny – zostają poprzednie liczniki
      if (facets) this.facets.set(facets);
    });

    this.categoryService.getCategories().pipe(
      catchError(() => of([] as Category[])),
      takeUntilDestroyed()
    ).subscribe(categories => this.categories.set(categories));

    this.route.queryParamMap.pipe(
      map(query => paramsFromQuery(name => query.get(name))),
      takeUntilDestroyed()
    ).subscribe(params => {
      this.params.set(params);
      this.target = cloneParams(params);
      this.jobsRequest$.next(params);
      this.facetsRequest$.next(params);
    });
  }

  /** Ponawia ostatnie żądanie (przycisk „Spróbuj ponownie”). */
  retry(): void {
    this.jobsRequest$.next(this.params());
  }

  onFilterChange(change: FilterChange): void {
    this.navigate({ ...change.patch }, change.replace);
  }

  removeFilter(key: string): void {
    const filter = this.activeFilters().find(f => f.key === key);
    if (filter) {
      this.navigate({ ...filter.remove }, false);
    }
  }

  clearFilters(): void {
    this.target = new JobParams();
    this.writeUrl(this.target, false);
  }

  onPageChange(event: PageEvent): void {
    this.navigate({ pageNumber: event.pageIndex + 1, pageSize: event.pageSize }, false, true);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  viewJobDetails(id: number): void {
    this.router.navigate(['/jobs', id]);
  }

  /** „Wygasa za X dni” dla ofert wygasających w ciągu tygodnia, inaczej null. */
  expiresSoon(job: JobOffer): string | null {
    return formatExpiresSoon(job.expiresAt);
  }

  private navigate(patch: Partial<JobParams>, replace: boolean, keepPage = false): void {
    const next = Object.assign(cloneParams(this.target), patch);
    if (!keepPage) {
      next.pageNumber = 1;
    }
    this.target = next;
    this.writeUrl(next, replace);
  }

  private writeUrl(params: JobParams, replace: boolean): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: urlQuery(params), replaceUrl: replace });
  }

  private fetchJobs(params: JobParams): Observable<PaginatedResult<JobOffer> | Error> {
    return this.jobService.getJobs(params).pipe(
      catchError(error => {
        console.error('Error loading jobs:', error);
        return of(new Error(getApiErrorMessage(error, 'Nie udało się wczytać ofert')));
      })
    );
  }

  private onJobsLoaded(result: PaginatedResult<JobOffer> | Error): void {
    if (result instanceof Error) {
      this.loadError.set(result.message);
      this.resultCount.set(null);
    } else if (result.result.length === 0 && result.pagination.TotalItems > 0 && this.params().pageNumber > 1) {
      // Strona poza zakresem (np. stary link) – przechodzimy na ostatnią istniejącą bez wpisu w historii
      this.navigate({ pageNumber: Math.max(1, result.pagination.TotalPages) }, true, true);
      return;
    } else {
      this.jobs.set(result.result);
      this.totalItems.set(result.pagination.TotalItems);
      this.resultCount.set(result.pagination.TotalItems);
    }
    this.loaded.set(true);
    this.loading.set(false);
  }
}
