import { Component, computed, inject, input, signal, viewChild, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { providePolishPaginatorIntl } from '../../../services/polish-paginator-intl';
import { MatSortModule, Sort } from '@angular/material/sort';
import { Subject, catchError, debounceTime, filter, map, of, switchMap, tap } from 'rxjs';
import { JobService } from '../../../services/job.service';
import { JobOffer, JobOfferStatus, WorkType } from '../../../models/job-offer.model';
import { JobOrderBy, JobState, ManageJobParams } from '../../../models/job-params.model';
import { PaginatedResult } from '../../../models/pagination.model';
import { Company, CompanyStatus } from '../../../models/company.model';
import { PanelMode } from '../../../models/panel-mode.model';
import { getApiErrorMessage, isHandledByInterceptor } from '../../../utils/http-errors';
import { JOB_STATE_OPTIONS, getJobState, getJobStatePresentation } from '../../../utils/status-labels';
import { StatusChipComponent } from '../../shared/status-chip.component';
import { CompanyAutocompleteComponent, CompanyOption } from '../../shared/company-autocomplete.component';
import { JobDialogComponent, JobDialogData } from '../job-dialog/job-dialog.component';

const ALL_COLUMNS = ['id', 'title', 'status', 'company', 'location', 'workType', 'salary', 'createdAt', 'actions'];

// Kolumny sortowane po stronie serwera -> wartości orderBy z API
const SORT_ORDER: Record<string, { asc: JobOrderBy; desc: JobOrderBy }> = {
  title: { asc: 'titleAsc', desc: 'titleDesc' },
  company: { asc: 'companyAsc', desc: 'companyDesc' },
  location: { asc: 'locationAsc', desc: 'locationDesc' },
  salary: { asc: 'salaryLow', desc: 'salaryHigh' },
  createdAt: { asc: 'oldest', desc: 'newest' }
};

const DEFAULT_SORT: Sort = { active: 'createdAt', direction: 'desc' };

@Component({
  selector: 'app-admin-jobs-tab',
  providers: [providePolishPaginatorIntl()],
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatPaginatorModule,
    MatSortModule,
    StatusChipComponent,
    CompanyAutocompleteComponent
  ],
  templateUrl: './admin-jobs-tab.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './admin-jobs-tab.component.scss'
})
export class AdminJobsTabComponent {
  private jobService = inject(JobService);
  private dialog = inject(MatDialog);
  private snackBar = inject(MatSnackBar);

  /** admin – wszystkie oferty z kolumną i filtrem firmy; employer – oferty własnej firmy. */
  readonly mode = input<PanelMode>('admin');
  /** Firma pracodawcy (tryb employer) – bez zatwierdzenia nie wolno publikować. */
  readonly company = input<Company | null>(null);

  readonly isAdmin = computed(() => this.mode() === 'admin');
  readonly canPublish = computed(() => this.isAdmin() || this.company()?.status === CompanyStatus.Approved);
  readonly displayedColumns = computed(() => this.isAdmin() ? ALL_COLUMNS : ALL_COLUMNS.filter(c => c !== 'company'));
  readonly pageSizeOptions = [5, 10, 25, 50];
  readonly workTypeOptions = [
    { value: null, label: 'Wszystkie' },
    { value: WorkType.Remote, label: 'Zdalna' },
    { value: WorkType.Hybrid, label: 'Hybrydowa' },
    { value: WorkType.Onsite, label: 'Stacjonarna' }
  ];
  readonly stateOptions = JOB_STATE_OPTIONS;
  readonly JobOfferStatus = JobOfferStatus;

  readonly jobs = signal<JobOffer[]>([]);
  readonly totalItems = signal(0);
  readonly loading = signal(false);
  readonly deletingId = signal<number | null>(null);
  readonly statusChangingId = signal<number | null>(null);

  readonly pageIndex = signal(0);
  readonly pageSize = signal(10);
  readonly searchInput = signal('');
  readonly workType = signal<WorkType | null>(null);
  readonly state = signal<JobState | null>(null);
  readonly companyFilter = signal<CompanyOption | null>(null);
  readonly sort = signal<Sort>(DEFAULT_SORT);

  private readonly companyAutocomplete = viewChild(CompanyAutocompleteComponent);

  // Fraza faktycznie wysłana do API (po debounce)
  private readonly searchTerm = signal('');
  readonly hasActiveFilters = computed(() =>
    this.searchTerm() !== '' || this.workType() !== null || this.state() !== null || this.companyFilter() !== null);

  private readonly search$ = new Subject<string>();
  private readonly load$ = new Subject<void>();

  constructor() {
    this.search$.pipe(
      debounceTime(400),
      map(term => term.trim()),
      filter(term => term !== this.searchTerm()),
      takeUntilDestroyed()
    ).subscribe(term => this.applySearch(term));

    // switchMap anuluje poprzednie żądanie – odpowiedzi nie nadpiszą się w złej kolejności
    this.load$.pipe(
      tap(() => this.loading.set(true)),
      switchMap(() => this.jobService.getManagedJobs(this.buildParams()).pipe(
        catchError(error => {
          console.error('Error loading jobs:', error);
          if (!isHandledByInterceptor(error)) {
            alert('Błąd podczas ładowania ofert');
          }
          return of(null);
        })
      )),
      takeUntilDestroyed()
    ).subscribe(result => this.onJobsLoaded(result));

    this.reload();
  }

  /** Przeładowuje bieżącą stronę (np. po zmianach w innej zakładce lub odświeżeniu seeda). */
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

  clearSearch(): void {
    this.searchInput.set('');
    this.search$.next(''); // unieważnia wpisaną frazę czekającą jeszcze w debounce
    this.applySearch('');
  }

  onWorkTypeChange(workType: WorkType | null): void {
    this.workType.set(workType);
    this.reload(true);
  }

  onStateChange(state: JobState | null): void {
    this.state.set(state);
    this.reload(true);
  }

  onCompanyFilterChange(company: CompanyOption | null): void {
    if (company?.id === this.companyFilter()?.id) return;
    this.companyFilter.set(company);
    this.reload(true);
  }

  onSortChange(sort: Sort): void {
    this.sort.set(sort);
    this.reload(true);
  }

  onPageChange(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.reload();
  }

  clearFilters(): void {
    this.searchInput.set('');
    this.search$.next('');
    this.searchTerm.set('');
    this.workType.set(null);
    this.state.set(null);
    this.companyFilter.set(null);
    this.companyAutocomplete()?.reset();
    this.sort.set(DEFAULT_SORT);
    this.reload(true);
  }

  /** Etykieta i kolor stanu oferty (szkic / aktywna / wygasła / zamknięta). */
  statePresentation(job: JobOffer) {
    return getJobStatePresentation(job);
  }

  /** Szybkie akcje zależne od stanu: szkic → publikacja, aktywna/wygasła → zamknięcie, zamknięta → wznowienie. */
  quickActions(job: JobOffer): { status: JobOfferStatus; icon: string; label: string }[] {
    const actions = this.stateActions(job);
    // Firma niezatwierdzona – publikacja/wznowienie zablokowane przez API (403)
    return this.canPublish() ? actions : actions.filter(action => action.status !== JobOfferStatus.Active);
  }

  private stateActions(job: JobOffer): { status: JobOfferStatus; icon: string; label: string }[] {
    switch (getJobState(job)) {
      case 'draft': return [{ status: JobOfferStatus.Active, icon: 'publish', label: 'Opublikuj' }];
      case 'active':
      case 'expired': return [{ status: JobOfferStatus.Closed, icon: 'lock', label: 'Zamknij' }];
      case 'closed': return [{ status: JobOfferStatus.Active, icon: 'lock_open', label: 'Wznów' }];
    }
  }

  changeStatus(job: JobOffer, status: JobOfferStatus): void {
    this.statusChangingId.set(job.id);
    this.jobService.updateJobStatus(job.id, status).subscribe({
      next: (updated) => {
        this.statusChangingId.set(null);
        this.notify(`Status oferty „${updated.title}”: ${getJobStatePresentation(updated).label}`);
        // przy aktywnym filtrze stanu oferta może zniknąć z bieżącej listy
        this.reload();
      },
      error: (error) => {
        console.error('Error changing job status:', error);
        this.statusChangingId.set(null);
        if (!isHandledByInterceptor(error)) {
          this.notify(getApiErrorMessage(error, 'Błąd podczas zmiany statusu oferty'));
        }
      }
    });
  }

  openAddDialog(): void {
    this.openJobDialog(null);
  }

  openEditDialog(job: JobOffer): void {
    this.openJobDialog(job);
  }

  deleteJob(id: number): void {
    // admin usuwa ofertę razem z aplikacjami; pracodawca tylko ofertę bez aplikacji (API zwraca 400 – trzeba ją zamknąć)
    const question = this.isAdmin()
      ? 'Czy na pewno chcesz usunąć tę ofertę? Wszystkie aplikacje na to stanowisko również zostaną usunięte.'
      : 'Czy na pewno chcesz usunąć tę ofertę? Ofertę, na którą wpłynęły aplikacje, można tylko zamknąć.';
    if (!confirm(question)) {
      return;
    }

    this.deletingId.set(id);
    this.jobService.deleteJob(id).subscribe({
      next: () => {
        this.deletingId.set(null);
        // Usunięto ostatnią ofertę na stronie – cofamy się o stronę
        if (this.jobs().length === 1 && this.pageIndex() > 0) {
          this.pageIndex.update(index => index - 1);
        }
        this.reload();
        alert('Oferta została usunięta');
      },
      error: (error) => {
        console.error('Error deleting job:', error);
        this.deletingId.set(null);
        if (!isHandledByInterceptor(error)) {
          alert(getApiErrorMessage(error, 'Błąd podczas usuwania oferty'));
        }
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

  private applySearch(term: string): void {
    this.searchTerm.set(term);
    this.reload(true);
  }

  private openJobDialog(job: JobOffer | null): void {
    const data: JobDialogData = { job, mode: this.mode(), company: this.company() };
    const dialogRef = this.dialog.open(JobDialogComponent, {
      width: '600px',
      disableClose: true,
      data
    });

    dialogRef.afterClosed().subscribe(saved => {
      if (saved === true) {
        this.reload();
      }
    });
  }

  private notify(message: string): void {
    this.snackBar.open(message, 'OK', { duration: 5000 });
  }

  private buildParams(): ManageJobParams {
    const params = new ManageJobParams();
    params.pageNumber = this.pageIndex() + 1;
    params.pageSize = this.pageSize();
    params.searchTerm = this.searchTerm();
    params.workTypes = this.workType() === null ? [] : [this.workType() as WorkType];
    params.state = this.state();
    params.companyId = this.companyFilter()?.id ?? null;
    params.orderBy = this.toOrderBy(this.sort());
    return params;
  }

  private toOrderBy({ active, direction }: Sort): JobOrderBy {
    const order = SORT_ORDER[active];
    return order && direction ? order[direction] : 'newest';
  }

  private onJobsLoaded(result: PaginatedResult<JobOffer> | null): void {
    if (result) {
      const { TotalItems, TotalPages } = result.pagination;

      // Strona poza zakresem (np. dane zmieniły się w międzyczasie) – przechodzimy na ostatnią istniejącą
      if (result.result.length === 0 && TotalItems > 0 && this.pageIndex() >= TotalPages) {
        this.pageIndex.set(TotalPages - 1);
        this.load$.next();
        return;
      }

      this.jobs.set(result.result);
      this.totalItems.set(TotalItems);
    }
    this.loading.set(false);
  }
}
