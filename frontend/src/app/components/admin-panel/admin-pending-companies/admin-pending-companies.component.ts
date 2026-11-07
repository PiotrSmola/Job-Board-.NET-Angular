import { ChangeDetectionStrategy, Component, DestroyRef, inject, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { Subject, catchError, of, switchMap, tap } from 'rxjs';
import { Company, CompanyStatus } from '../../../models/company.model';
import { AdminCompanyParams } from '../../../models/company-params.model';
import { AdminService } from '../../../services/admin.service';
import { safeWebsiteUrl } from '../../../utils/company-form';
import { getApiErrorMessage, isHandledByInterceptor } from '../../../utils/http-errors';
import { RejectCompanyDialogComponent } from '../reject-company-dialog/reject-company-dialog.component';

const PAGE_SIZE = 10;

/** „Firmy do weryfikacji” pod statystykami admina: najstarsze oczekujące zgłoszenia z akcjami Zatwierdź / Odrzuć. */
@Component({
  selector: 'app-admin-pending-companies',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, MatButtonModule, MatProgressSpinnerModule, MatTableModule],
  template: `
    <section aria-labelledby="pending-heading">
      <h2 id="pending-heading" class="section-title">
        Firmy do weryfikacji
        @if (total() > 0) {
          <span class="tertiary num count">({{ total() }})</span>
        }
      </h2>

      @if (loading() && !companies().length) {
        <div class="state"><mat-spinner diameter="32" aria-label="Wczytywanie firm do weryfikacji"></mat-spinner></div>
      } @else if (loadError(); as message) {
        <div class="state" role="alert">
          <p class="danger-text">{{ message }}</p>
          <button mat-stroked-button type="button" (click)="reload()">Spróbuj ponownie</button>
        </div>
      } @else if (companies().length) {
        <div class="table-card">
          <div class="table-scroll">
            <table mat-table [dataSource]="companies()" aria-label="Firmy do weryfikacji">
              <ng-container matColumnDef="name">
                <th mat-header-cell *matHeaderCellDef>Firma</th>
                <td mat-cell *matCellDef="let company">
                  <a class="primary-text" [routerLink]="['/companies', company.id]">{{ company.name }}</a>
                  @if (company.location) {
                    <span class="secondary-text">{{ company.location }}</span>
                  }
                </td>
              </ng-container>
              <ng-container matColumnDef="createdAt">
                <th mat-header-cell *matHeaderCellDef>Zgłoszona</th>
                <td mat-cell *matCellDef="let company" class="muted nowrap">{{ company.createdAt | date: 'dd.MM.yyyy' }}</td>
              </ng-container>
              <ng-container matColumnDef="website">
                <th mat-header-cell *matHeaderCellDef>Strona</th>
                <td mat-cell *matCellDef="let company">
                  @if (website(company); as url) {
                    <a class="text-link" [href]="url" target="_blank" rel="noopener noreferrer">{{ url }}</a>
                  } @else {
                    <span class="tertiary">brak</span>
                  }
                </td>
              </ng-container>
              <ng-container matColumnDef="actions">
                <th mat-header-cell *matHeaderCellDef class="actions-header"><span class="visually-hidden">Akcje</span></th>
                <td mat-cell *matCellDef="let company">
                  <div class="actions-cell">
                    <button mat-button color="warn" type="button" [disabled]="busyId() === company.id"
                      (click)="reject(company)" [attr.aria-label]="'Odrzuć firmę ' + company.name">Odrzuć</button>
                    <button mat-stroked-button type="button" [disabled]="busyId() === company.id"
                      (click)="approve(company)" [attr.aria-label]="'Zatwierdź firmę ' + company.name">Zatwierdź</button>
                  </div>
                </td>
              </ng-container>
              <tr mat-header-row *matHeaderRowDef="columns"></tr>
              <tr mat-row *matRowDef="let row; columns: columns;" class="table-row"></tr>
            </table>
          </div>
        </div>
        @if (total() > companies().length) {
          <p class="muted more">Pokazano {{ companies().length }} najstarszych – pozostałe w zakładce Firmy.</p>
        }
      } @else {
        <p class="muted empty">Brak firm oczekujących na weryfikację.</p>
      }
    </section>
  `,
  styleUrl: './admin-pending-companies.component.scss'
})
export class AdminPendingCompaniesComponent {
  private adminService = inject(AdminService);
  private dialog = inject(MatDialog);
  private snackBar = inject(MatSnackBar);
  private destroyRef = inject(DestroyRef);

  /** Zatwierdzono lub odrzucono firmę – rodzic może odświeżyć inne widoki. */
  readonly changed = output<void>();

  readonly columns = ['name', 'createdAt', 'website', 'actions'];
  readonly companies = signal<Company[]>([]);
  readonly total = signal(0);
  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly busyId = signal<number | null>(null);

  private readonly load$ = new Subject<void>();

  constructor() {
    this.load$.pipe(
      tap(() => {
        this.loading.set(true);
        this.loadError.set(null);
      }),
      switchMap(() => {
        const params = new AdminCompanyParams();
        params.pageSize = PAGE_SIZE;
        params.status = CompanyStatus.Pending;
        params.orderBy = 'oldest';
        return this.adminService.getCompanies(params).pipe(
          catchError(error => {
            console.error('Error loading pending companies:', error);
            this.loadError.set(getApiErrorMessage(error, 'Nie udało się wczytać firm do weryfikacji'));
            return of(null);
          })
        );
      }),
      takeUntilDestroyed()
    ).subscribe(page => {
      if (page) {
        this.companies.set(page.result);
        this.total.set(page.pagination.TotalItems);
      }
      this.loading.set(false);
    });

    this.reload();
  }

  reload(): void {
    this.load$.next();
  }

  website(company: Company): string | null {
    return safeWebsiteUrl(company.website);
  }

  approve(company: Company): void {
    this.busyId.set(company.id);
    this.adminService.approveCompany(company.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: updated => {
        this.busyId.set(null);
        this.snackBar.open(`Firma „${updated.name}” została zatwierdzona`, 'OK', { duration: 5000, politeness: 'polite' });
        this.afterChange();
      },
      error: error => {
        console.error('Error approving company:', error);
        this.busyId.set(null);
        if (!isHandledByInterceptor(error)) {
          this.snackBar.open(getApiErrorMessage(error, 'Błąd podczas zatwierdzania firmy'), 'OK', { duration: 5000 });
        }
      }
    });
  }

  reject(company: Company): void {
    this.dialog.open(RejectCompanyDialogComponent, { width: '520px', maxWidth: '95vw', ariaModal: true, data: company })
      .afterClosed().pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(updated => {
        if (updated) this.afterChange();
      });
  }

  private afterChange(): void {
    this.reload();
    this.changed.emit();
  }
}
