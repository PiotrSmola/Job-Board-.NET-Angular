import { Component, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTabsModule } from '@angular/material/tabs';
import { AdminService } from '../../services/admin.service';
import { AuthService } from '../../services/auth.service';
import { AdminJobsTabComponent } from './admin-jobs-tab/admin-jobs-tab.component';
import { AdminApplicationsTabComponent } from './admin-applications-tab/admin-applications-tab.component';
import { AdminCompaniesTabComponent } from './admin-companies-tab/admin-companies-tab.component';
import { StatsDashboardComponent } from '../stats/stats-dashboard.component';

const JOBS_TAB = 0;
const APPLICATIONS_TAB = 1;
const COMPANIES_TAB = 2;

@Component({
  selector: 'app-admin-panel',
  standalone: true,
  imports: [
    MatButtonModule,
    MatIconModule,
    MatTabsModule,
    AdminJobsTabComponent,
    AdminApplicationsTabComponent,
    AdminCompaniesTabComponent,
    // używany wyłącznie w @defer – Angular wydziela go do osobnego chunka
    StatsDashboardComponent
  ],
  templateUrl: './admin-panel.component.html',
  styleUrl: './admin-panel.component.scss'
})
export class AdminPanelComponent {
  private adminService = inject(AdminService);
  private authService = inject(AuthService);

  private readonly jobsTab = viewChild(AdminJobsTabComponent);
  private readonly applicationsTab = viewChild(AdminApplicationsTabComponent);
  private readonly companiesTab = viewChild(AdminCompaniesTabComponent);

  // Zakładki tworzone są leniwie przy pierwszym wejściu; przy kolejnych – odświeżamy dane
  private readonly visitedTabs = new Set<number>([JOBS_TAB]);

  readonly selectedTabIndex = signal(JOBS_TAB);
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

    if (index === JOBS_TAB) {
      this.jobsTab()?.reload();
    } else if (index === APPLICATIONS_TAB) {
      this.applicationsTab()?.reload();
    } else if (index === COMPANIES_TAB) {
      this.companiesTab()?.reload();
    }
  }

  refreshSeedData(): void {
    if (!confirm('Czy na pewno chcesz przywrócić dane z seedera? Wszystkie oferty pracy, aplikacje i użytkownicy zostaną usunięte i zastąpione danymi początkowymi.')) {
      return;
    }

    this.refreshingSeed.set(true);
    this.adminService.refreshSeed().subscribe({
      next: () => {
        this.refreshingSeed.set(false);
        // Seed odtwarza konta z nowymi Id, więc obecny token przestaje być ważny
        alert('Dane zostały pomyślnie przywrócone z seedera! Konta zostały odtworzone – zaloguj się ponownie.');
        this.authService.logout();
      },
      error: (error: HttpErrorResponse) => {
        console.error('Error refreshing seed data:', error);
        this.refreshingSeed.set(false);

        if (error.status === 403) {
          // Funkcja wyłączona na serwerze (np. zmieniono konfigurację) – chowamy przycisk
          this.allowSeedRefresh.set(false);
          alert('Przywracanie danych z seedera jest wyłączone na tym serwerze.');
        } else if (error.status !== 401) {
          alert('Błąd podczas przywracania danych z seedera: ' + (error.error?.message || error.message));
        }
      }
    });
  }
}
