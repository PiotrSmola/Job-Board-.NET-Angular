import { Component, DestroyRef, inject, signal, viewChild, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { filter } from 'rxjs';
import { AdminService } from '../../services/admin.service';
import { AuthService } from '../../services/auth.service';
import { ConfirmService } from '../../services/confirm.service';
import { getApiErrorMessage } from '../../utils/http-errors';
import { AdminJobsTabComponent } from './admin-jobs-tab/admin-jobs-tab.component';
import { AdminApplicationsTabComponent } from './admin-applications-tab/admin-applications-tab.component';
import { AdminCompaniesTabComponent } from './admin-companies-tab/admin-companies-tab.component';
import { AdminCategoriesTabComponent } from './admin-categories-tab/admin-categories-tab.component';
import { AdminPendingCompaniesComponent } from './admin-pending-companies/admin-pending-companies.component';
import { StatsDashboardComponent } from '../stats/stats-dashboard.component';

export const STATS_TAB = 0;
export const JOBS_TAB = 1;
export const APPLICATIONS_TAB = 2;
export const COMPANIES_TAB = 3;
export const CATEGORIES_TAB = 4;

/** Panel admina: statystyki z firmami do weryfikacji, oferty, aplikacje, firmy i kategorie. */
@Component({
  selector: 'app-admin-panel',
  standalone: true,
  imports: [
    MatButtonModule,
    MatTabsModule,
    AdminJobsTabComponent,
    AdminApplicationsTabComponent,
    AdminCompaniesTabComponent,
    AdminCategoriesTabComponent,
    AdminPendingCompaniesComponent,
    // używany wyłącznie w @defer – Angular wydziela go do osobnego chunka
    StatsDashboardComponent
  ],
  templateUrl: './admin-panel.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './admin-panel.component.scss'
})
export class AdminPanelComponent {
  private adminService = inject(AdminService);
  private authService = inject(AuthService);
  private confirm = inject(ConfirmService);
  private snackBar = inject(MatSnackBar);
  private destroyRef = inject(DestroyRef);

  private readonly jobsTab = viewChild(AdminJobsTabComponent);
  private readonly applicationsTab = viewChild(AdminApplicationsTabComponent);
  private readonly companiesTab = viewChild(AdminCompaniesTabComponent);
  private readonly categoriesTab = viewChild(AdminCategoriesTabComponent);
  private readonly pendingCompanies = viewChild(AdminPendingCompaniesComponent);

  // Zakładki tworzone są leniwie przy pierwszym wejściu; przy kolejnych – odświeżamy dane
  private readonly visitedTabs = new Set<number>([STATS_TAB]);

  readonly selectedTabIndex = signal(STATS_TAB);
  readonly allowSeedRefresh = signal(false);
  readonly refreshingSeed = signal(false);

  constructor() {
    this.adminService.getSettings().pipe(takeUntilDestroyed()).subscribe({
      next: settings => this.allowSeedRefresh.set(settings.allowSeedRefresh === true),
      error: error => {
        console.error('Error loading admin settings:', error);
        this.allowSeedRefresh.set(false);
      }
    });
  }

  onTabChange(index: number): void {
    this.selectedTabIndex.set(index);

    if (!this.visitedTabs.has(index)) {
      this.visitedTabs.add(index);
      return;
    }

    if (index === STATS_TAB) {
      this.pendingCompanies()?.reload();
    } else if (index === JOBS_TAB) {
      this.jobsTab()?.reload();
    } else if (index === APPLICATIONS_TAB) {
      this.applicationsTab()?.reload();
    } else if (index === COMPANIES_TAB) {
      this.companiesTab()?.reload();
    } else if (index === CATEGORIES_TAB) {
      this.categoriesTab()?.reload();
    }
  }

  /** Weryfikacja firmy pod statystykami – lista firm (jeśli już otwarta) pokaże nowy status. */
  onPendingChanged(): void {
    this.companiesTab()?.reload();
  }

  refreshSeedData(): void {
    this.confirm.confirm({
      title: 'Przywrócić dane z seedera?',
      message: 'Wszystkie oferty pracy, aplikacje i użytkownicy zostaną usunięci i zastąpieni danymi początkowymi.',
      confirmLabel: 'Przywróć dane',
      destructive: true
    }).pipe(filter(Boolean), takeUntilDestroyed(this.destroyRef)).subscribe(() => this.runSeedRefresh());
  }

  private runSeedRefresh(): void {
    this.refreshingSeed.set(true);
    this.adminService.refreshSeed().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.refreshingSeed.set(false);
        // Seed odtwarza konta z nowymi Id, więc obecny token przestaje być ważny
        this.notify('Dane zostały przywrócone z seedera. Konta zostały odtworzone – zaloguj się ponownie.', 'polite');
        this.authService.logout();
      },
      error: (error: HttpErrorResponse) => {
        console.error('Error refreshing seed data:', error);
        this.refreshingSeed.set(false);

        if (error.status === 403) {
          // Funkcja wyłączona na serwerze (np. zmieniono konfigurację) – chowamy przycisk
          this.allowSeedRefresh.set(false);
          this.notify('Przywracanie danych z seedera jest wyłączone na tym serwerze.');
        } else if (error.status !== 401) {
          this.notify(getApiErrorMessage(error, 'Błąd podczas przywracania danych z seedera'));
        }
      }
    });
  }

  private notify(message: string, politeness: 'polite' | 'assertive' = 'assertive'): void {
    this.snackBar.open(message, 'OK', { duration: 8000, politeness });
  }
}
