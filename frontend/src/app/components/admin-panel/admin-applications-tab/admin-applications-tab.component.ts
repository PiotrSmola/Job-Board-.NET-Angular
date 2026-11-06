import { Component, computed, inject, input, signal, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatDialog } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSortModule, Sort } from '@angular/material/sort';
import { Subject, catchError, debounceTime, filter, map, of, switchMap, tap } from 'rxjs';
import { JobApplicationService } from '../../../services/job-application.service';
import { ApplicationStatus, JobApplication } from '../../../models/job-application.model';
import { ApplicationOrderBy, ApplicationParams } from '../../../models/application-params.model';
import { PaginatedResult } from '../../../models/pagination.model';
import { PanelMode } from '../../../models/panel-mode.model';
import { isHandledByInterceptor } from '../../../utils/http-errors';
import { APPLICATION_STATUS_FILTER_OPTIONS, getApplicationStatusPresentation } from '../../../utils/status-labels';
import { StatusChipComponent } from '../../shared/status-chip.component';
import { ApplicationStatusDialogComponent } from '../application-status-dialog/application-status-dialog.component';
import { ApplicationHistoryDialogComponent } from '../application-history-dialog/application-history-dialog.component';
import { AttachmentListComponent } from '../../shared/attachment-list.component';

// API sortuje aplikacje tylko po dacie złożenia
const DEFAULT_SORT: Sort = { active: 'appliedAt', direction: 'desc' };

const ALL_COLUMNS = ['id', 'jobTitle', 'company', 'user', 'status', 'appliedAt', 'note', 'attachments', 'actions'];

@Component({
  selector: 'app-admin-applications-tab',
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
    AttachmentListComponent
  ],
  templateUrl: './admin-applications-tab.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './admin-applications-tab.component.scss'
})
export class AdminApplicationsTabComponent {
  private applicationService = inject(JobApplicationService);
  private dialog = inject(MatDialog);

  /** admin – wszystkie aplikacje (z usuwaniem); employer – aplikacje do ofert własnej firmy, bez usuwania. */
  readonly mode = input<PanelMode>('admin');
  readonly isAdmin = computed(() => this.mode() === 'admin');
  readonly displayedColumns = computed(() => this.isAdmin() ? ALL_COLUMNS : ALL_COLUMNS.filter(c => c !== 'company'));
  readonly pageSizeOptions = [5, 10, 25, 50];
  readonly statusOptions = APPLICATION_STATUS_FILTER_OPTIONS;
  readonly ApplicationStatus = ApplicationStatus;

  readonly applications = signal<JobApplication[]>([]);
  readonly totalItems = signal(0);
  readonly loading = signal(false);
  readonly deletingId = signal<number | null>(null);

  readonly pageIndex = signal(0);
  readonly pageSize = signal(10);
  readonly searchInput = signal('');
  readonly status = signal<ApplicationStatus | null>(null);
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
      switchMap(() => this.applicationService.getAllApplications(this.buildParams()).pipe(
        catchError(error => {
          console.error('Error loading applications:', error);
          if (!isHandledByInterceptor(error)) {
            alert('Błąd podczas ładowania aplikacji');
          }
          return of(null);
        })
      )),
      takeUntilDestroyed()
    ).subscribe(result => this.onApplicationsLoaded(result));

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

  onStatusChange(status: ApplicationStatus | null): void {
    this.status.set(status);
    this.reload(true);
  }

  statusPresentation(status: ApplicationStatus) {
    return getApplicationStatusPresentation(status);
  }

  openStatusDialog(application: JobApplication): void {
    const dialogRef = this.dialog.open(ApplicationStatusDialogComponent, { width: '520px', ariaModal: true, data: application });

    dialogRef.afterClosed().subscribe(updated => {
      if (updated) {
        // przy aktywnym filtrze statusu aplikacja może zniknąć z bieżącej listy
        this.reload();
      }
    });
  }

  openHistoryDialog(application: JobApplication): void {
    this.dialog.open(ApplicationHistoryDialogComponent, { width: '560px', ariaModal: true, data: application });
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

  deleteApplication(id: number): void {
    // Usuwanie aplikacji – tylko admin
    if (!this.isAdmin() || !confirm('Czy na pewno chcesz usunąć tę aplikację?')) {
      return;
    }

    this.deletingId.set(id);
    this.applicationService.deleteApplication(id).subscribe({
      next: () => {
        this.deletingId.set(null);
        // Usunięto ostatnią aplikację na stronie – cofamy się o stronę
        if (this.applications().length === 1 && this.pageIndex() > 0) {
          this.pageIndex.update(index => index - 1);
        }
        this.reload();
        alert('Aplikacja została usunięta');
      },
      error: (error) => {
        console.error('Error deleting application:', error);
        this.deletingId.set(null);
        if (!isHandledByInterceptor(error)) {
          alert('Błąd podczas usuwania aplikacji');
        }
      }
    });
  }

  formatDate(date: Date): string {
    return new Date(date).toLocaleDateString('pl-PL', {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  }

  private applySearch(term: string): void {
    this.searchTerm.set(term);
    this.reload(true);
  }

  private buildParams(): ApplicationParams {
    const params = new ApplicationParams();
    params.pageNumber = this.pageIndex() + 1;
    params.pageSize = this.pageSize();
    params.searchTerm = this.searchTerm();
    params.status = this.status();
    params.orderBy = this.toOrderBy(this.sort());
    return params;
  }

  private toOrderBy({ active, direction }: Sort): ApplicationOrderBy {
    return active === 'appliedAt' && direction === 'asc' ? 'oldest' : 'newest';
  }

  private onApplicationsLoaded(result: PaginatedResult<JobApplication> | null): void {
    if (result) {
      const { TotalItems, TotalPages } = result.pagination;

      // Strona poza zakresem (np. dane zmieniły się w międzyczasie) – przechodzimy na ostatnią istniejącą
      if (result.result.length === 0 && TotalItems > 0 && this.pageIndex() >= TotalPages) {
        this.pageIndex.set(TotalPages - 1);
        this.load$.next();
        return;
      }

      this.applications.set(result.result);
      this.totalItems.set(TotalItems);
    }
    this.loading.set(false);
  }
}
