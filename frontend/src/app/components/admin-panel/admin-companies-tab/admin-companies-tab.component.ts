import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSortModule, Sort } from '@angular/material/sort';
import { Subject, catchError, debounceTime, filter, map, of, switchMap, tap } from 'rxjs';
import { AdminService } from '../../../services/admin.service';
import { Company, CompanyStatus } from '../../../models/company.model';
import { AdminCompanyOrderBy, AdminCompanyParams } from '../../../models/company-params.model';
import { PaginatedResult } from '../../../models/pagination.model';
import { getApiErrorMessage, isHandledByInterceptor } from '../../../utils/http-errors';
import { COMPANY_STATUS_FILTER_OPTIONS, getCompanyStatusPresentation } from '../../../utils/status-labels';
import { StatusChipComponent } from '../../shared/status-chip.component';
import { CompanyDialogComponent } from '../company-dialog/company-dialog.component';
import { RejectCompanyDialogComponent } from '../reject-company-dialog/reject-company-dialog.component';

// Kolumny sortowane po stronie serwera -> wartości orderBy z API
const SORT_ORDER: Record<string, { asc: AdminCompanyOrderBy; desc: AdminCompanyOrderBy }> = {
  name: { asc: 'nameAsc', desc: 'nameDesc' },
  createdAt: { asc: 'oldest', desc: 'newest' }
};

const DEFAULT_SORT: Sort = { active: 'createdAt', direction: 'desc' };

/** Moderacja firm w panelu admina: zatwierdzanie, odrzucanie, edycja, dodawanie i usuwanie. */
@Component({
  selector: 'app-admin-companies-tab',
  standalone: true,
  imports: [
    DatePipe,
    FormsModule,
    RouterLink,
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
    StatusChipComponent
  ],
  templateUrl: './admin-companies-tab.component.html',
  styleUrl: './admin-companies-tab.component.scss'
})
export class AdminCompaniesTabComponent {
  private adminService = inject(AdminService);
  private dialog = inject(MatDialog);
  private snackBar = inject(MatSnackBar);

  readonly displayedColumns = ['id', 'name', 'status', 'membersCount', 'activeJobsCount', 'createdAt', 'actions'];
  readonly pageSizeOptions = [5, 10, 25, 50];
  readonly statusOptions = COMPANY_STATUS_FILTER_OPTIONS;
  readonly CompanyStatus = CompanyStatus;

  readonly companies = signal<Company[]>([]);
  readonly totalItems = signal(0);
  readonly loading = signal(false);
  /** Id firmy, na której trwa akcja (zatwierdzanie / usuwanie). */
  readonly busyId = signal<number | null>(null);

  readonly pageIndex = signal(0);
  readonly pageSize = signal(10);
  readonly searchInput = signal('');
  readonly status = signal<CompanyStatus | null>(null);
  readonly sort = signal<Sort>(DEFAULT_SORT);

  // Fraza faktycznie wysłana do API (po debounce)
  private readonly searchTerm = signal('');
  readonly hasActiveFilters = computed(() => this.searchTerm() !== '' || this.status() !== null);

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
      switchMap(() => this.adminService.getCompanies(this.buildParams()).pipe(
        catchError(error => {
          console.error('Error loading companies:', error);
          if (!isHandledByInterceptor(error)) {
            this.notify(getApiErrorMessage(error, 'Błąd podczas ładowania firm'));
          }
          return of(null);
        })
      )),
      takeUntilDestroyed()
    ).subscribe(result => this.onCompaniesLoaded(result));

    this.reload();
  }

  /** Przeładowuje bieżącą stronę (np. po powrocie do zakładki). */
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

  onStatusChange(status: CompanyStatus | null): void {
    this.status.set(status);
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
    this.status.set(null);
    this.sort.set(DEFAULT_SORT);
    this.reload(true);
  }

  statusPresentation(status: CompanyStatus | null | undefined) {
    return getCompanyStatusPresentation(status ?? CompanyStatus.Pending);
  }

  approve(company: Company): void {
    this.busyId.set(company.id);
    this.adminService.approveCompany(company.id).subscribe({
      next: updated => {
        this.busyId.set(null);
        this.notify(`Firma „${updated.name}” została zatwierdzona`);
        // przy aktywnym filtrze statusu firma może zniknąć z bieżącej listy
        this.reload();
      },
      error: error => {
        console.error('Error approving company:', error);
        this.busyId.set(null);
        if (!isHandledByInterceptor(error)) {
          this.notify(getApiErrorMessage(error, 'Błąd podczas zatwierdzania firmy'));
        }
      }
    });
  }

  openRejectDialog(company: Company): void {
    const dialogRef = this.dialog.open(RejectCompanyDialogComponent, { width: '520px', data: company });
    dialogRef.afterClosed().subscribe(updated => {
      if (updated) {
        this.reload();
      }
    });
  }

  openAddDialog(): void {
    this.openCompanyDialog(null);
  }

  openEditDialog(company: Company): void {
    this.openCompanyDialog(company);
  }

  deleteCompany(company: Company): void {
    if (!confirm(`Czy na pewno chcesz usunąć firmę „${company.name}”?`)) {
      return;
    }

    this.busyId.set(company.id);
    this.adminService.deleteCompany(company.id).subscribe({
      next: () => {
        this.busyId.set(null);
        // Usunięto ostatnią firmę na stronie – cofamy się o stronę
        if (this.companies().length === 1 && this.pageIndex() > 0) {
          this.pageIndex.update(index => index - 1);
        }
        this.reload();
        this.notify('Firma została usunięta');
      },
      error: error => {
        console.error('Error deleting company:', error);
        this.busyId.set(null);
        if (!isHandledByInterceptor(error)) {
          this.notify(getApiErrorMessage(error, 'Błąd podczas usuwania firmy'));
        }
      }
    });
  }

  private openCompanyDialog(company: Company | null): void {
    const dialogRef = this.dialog.open(CompanyDialogComponent, { width: '600px', disableClose: true, data: company });
    dialogRef.afterClosed().subscribe(saved => {
      if (saved) {
        this.reload();
      }
    });
  }

  private applySearch(term: string): void {
    this.searchTerm.set(term);
    this.reload(true);
  }

  private notify(message: string): void {
    this.snackBar.open(message, 'OK', { duration: 5000 });
  }

  private buildParams(): AdminCompanyParams {
    const params = new AdminCompanyParams();
    params.pageNumber = this.pageIndex() + 1;
    params.pageSize = this.pageSize();
    params.searchTerm = this.searchTerm();
    params.status = this.status();
    params.orderBy = this.toOrderBy(this.sort());
    return params;
  }

  private toOrderBy({ active, direction }: Sort): AdminCompanyOrderBy {
    const order = SORT_ORDER[active];
    return order && direction ? order[direction] : 'newest';
  }

  private onCompaniesLoaded(result: PaginatedResult<Company> | null): void {
    if (result) {
      const { TotalItems, TotalPages } = result.pagination;

      // Strona poza zakresem (np. dane zmieniły się w międzyczasie) – przechodzimy na ostatnią istniejącą
      if (result.result.length === 0 && TotalItems > 0 && this.pageIndex() >= TotalPages) {
        this.pageIndex.set(TotalPages - 1);
        this.load$.next();
        return;
      }

      this.companies.set(result.result);
      this.totalItems.set(TotalItems);
    }
    this.loading.set(false);
  }
}
