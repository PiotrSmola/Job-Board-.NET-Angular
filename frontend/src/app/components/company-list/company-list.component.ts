import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { providePolishPaginatorIntl } from '../../services/polish-paginator-intl';
import { MatSelectModule } from '@angular/material/select';
import { Subject, catchError, debounceTime, filter, map, of, switchMap, tap } from 'rxjs';
import { Company } from '../../models/company.model';
import { CompanyOrderBy, CompanyParams } from '../../models/company-params.model';
import { PaginatedResult } from '../../models/pagination.model';
import { CompanyService } from '../../services/company.service';
import { getApiErrorMessage } from '../../utils/http-errors';

/** „firma / firmy / firm” po liczbie (biernik: „Znaleziono 3 firmy”). */
export function companiesNoun(count: number): string {
  if (count === 1) return 'firmę';
  const last = count % 10;
  const lastTwo = count % 100;
  return last >= 2 && last <= 4 && !(lastTwo >= 12 && lastTwo <= 14) ? 'firmy' : 'firm';
}

/** Publiczna lista zatwierdzonych firm z wyszukiwaniem, sortowaniem i paginacją (makieta Firmy). */
@Component({
  selector: 'app-company-list',
  providers: [providePolishPaginatorIntl()],
  standalone: true,
  imports: [
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatSelectModule
  ],
  templateUrl: './company-list.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './company-list.component.scss'
})
export class CompanyListComponent {
  private companyService = inject(CompanyService);

  readonly sortOptions: { value: CompanyOrderBy; label: string }[] = [
    { value: 'nameAsc', label: 'Nazwa A–Z' },
    { value: 'nameDesc', label: 'Nazwa Z–A' },
    { value: 'jobsDesc', label: 'Najwięcej ofert' }
  ];
  readonly pageSizeOptions = [6, 12, 24, 48];

  readonly companies = signal<Company[]>([]);
  readonly totalItems = signal(0);
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  /** Czy zakończyło się pierwsze ładowanie (do tego czasu szkielety kart o docelowej wysokości). */
  readonly loaded = signal(false);
  readonly skeletons = Array.from({ length: 6 }, (_, i) => i);
  /** Liczba wyników ogłaszana w regionie role="status" („Znaleziono 3 firmy”). */
  readonly resultsText = computed(() => this.loaded() && !this.loadError()
    ? `Znaleziono ${this.totalItems()} ${companiesNoun(this.totalItems())}`
    : '');

  readonly searchInput = signal('');
  readonly orderBy = signal<CompanyOrderBy>('nameAsc');
  readonly pageIndex = signal(0);
  readonly pageSize = signal(12);

  // Fraza faktycznie wysłana do API (po debounce)
  private readonly searchTerm = signal('');
  private readonly search$ = new Subject<string>();
  private readonly load$ = new Subject<void>();

  constructor() {
    this.search$.pipe(
      debounceTime(400),
      map(term => term.trim()),
      filter(term => term !== this.searchTerm()),
      takeUntilDestroyed()
    ).subscribe(term => {
      this.searchTerm.set(term);
      this.reload(true);
    });

    this.load$.pipe(
      tap(() => {
        this.loading.set(true);
        this.loadError.set(null);
      }),
      switchMap(() => this.companyService.getCompanies(this.buildParams()).pipe(
        catchError(error => {
          console.error('Error loading companies:', error);
          this.loadError.set(getApiErrorMessage(error, 'Nie udało się wczytać firm'));
          return of(null);
        })
      )),
      takeUntilDestroyed()
    ).subscribe(result => this.onLoaded(result));

    this.reload();
  }

  reload(resetPage = false): void {
    if (resetPage) {
      this.pageIndex.set(0);
    }
    this.load$.next();
  }

  onSearchInput(term: string): void {
    this.searchInput.set(term);
    this.search$.next(term);
  }

  onSearchEvent(event: Event): void {
    this.onSearchInput((event.target as HTMLInputElement).value);
  }

  clearSearch(): void {
    this.searchInput.set('');
    this.search$.next('');
    this.searchTerm.set('');
    this.reload(true);
  }

  onOrderChange(orderBy: CompanyOrderBy): void {
    this.orderBy.set(orderBy);
    this.reload(true);
  }

  onPageChange(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.reload();
    window.scrollTo({ top: 0 });
  }

  jobsLabel(count: number): string {
    if (count === 1) return '1 oferta';
    const lastDigit = count % 10;
    const lastTwo = count % 100;
    const few = lastDigit >= 2 && lastDigit <= 4 && (lastTwo < 12 || lastTwo > 14);
    return `${count} ${few ? 'oferty' : 'ofert'}`;
  }

  private buildParams(): CompanyParams {
    const params = new CompanyParams();
    params.pageNumber = this.pageIndex() + 1;
    params.pageSize = this.pageSize();
    params.searchTerm = this.searchTerm();
    params.orderBy = this.orderBy();
    return params;
  }

  private onLoaded(result: PaginatedResult<Company> | null): void {
    if (result) {
      const { TotalItems, TotalPages } = result.pagination;

      // Strona poza zakresem – przechodzimy na ostatnią istniejącą
      if (result.result.length === 0 && TotalItems > 0 && this.pageIndex() >= TotalPages) {
        this.pageIndex.set(TotalPages - 1);
        this.load$.next();
        return;
      }

      this.companies.set(result.result);
      this.totalItems.set(TotalItems);
    }
    this.loading.set(false);
    this.loaded.set(true);
  }
}
