import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTabsModule } from '@angular/material/tabs';
import { Company, CompanyStatus } from '../../models/company.model';
import { CompanyService } from '../../services/company.service';
import { getApiErrorMessage, isHandledByInterceptor } from '../../utils/http-errors';
import { getCompanyStatusPresentation } from '../../utils/status-labels';
import { StatusChipComponent } from '../shared/status-chip.component';
import { AdminJobsTabComponent } from '../admin-panel/admin-jobs-tab/admin-jobs-tab.component';
import { AdminApplicationsTabComponent } from '../admin-panel/admin-applications-tab/admin-applications-tab.component';
import { EmployerCompanyTabComponent } from './employer-company-tab/employer-company-tab.component';

const JOBS_TAB = 0;
const APPLICATIONS_TAB = 1;
const COMPANY_TAB = 2;
const LOAD_ERROR = 'Nie udało się wczytać danych firmy';

/** Panel pracodawcy: oferty i aplikacje własnej firmy (zakładki admina w trybie employer) oraz profil firmy. */
@Component({
  selector: 'app-employer-panel',
  standalone: true,
  imports: [
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatTabsModule,
    StatusChipComponent,
    AdminJobsTabComponent,
    AdminApplicationsTabComponent,
    EmployerCompanyTabComponent
  ],
  templateUrl: './employer-panel.component.html',
  styleUrls: ['../admin-panel/admin-panel.component.scss', './employer-panel.component.scss']
})
export class EmployerPanelComponent {
  private companyService = inject(CompanyService);

  private readonly jobsTab = viewChild(AdminJobsTabComponent);
  private readonly applicationsTab = viewChild(AdminApplicationsTabComponent);

  // Zakładki tworzone są leniwie przy pierwszym wejściu; przy kolejnych – odświeżamy dane
  private readonly visitedTabs = new Set<number>([JOBS_TAB]);

  readonly companyTab = COMPANY_TAB;
  readonly selectedTabIndex = signal(JOBS_TAB);
  readonly company = signal<Company | null>(null);
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly isApproved = computed(() => this.company()?.status === CompanyStatus.Approved);
  readonly statusPresentation = computed(() => {
    const status = this.company()?.status;
    return status === null || status === undefined ? null : getCompanyStatusPresentation(status);
  });

  constructor() {
    this.loadCompany();
  }

  loadCompany(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.companyService.getMyCompany().subscribe({
      next: company => {
        this.company.set(company);
        this.loading.set(false);
      },
      error: error => {
        console.error('Error loading employer company:', error);
        this.loading.set(false);
        // bez danych firmy panel nie ma czego pokazać – zawsze stan błędu z ponowną próbą; powód 401/403
        // pokazał już interceptor, więc tu tylko ogólny tekst (bez dublowania komunikatu)
        this.loadError.set(isHandledByInterceptor(error)
          ? LOAD_ERROR
          : getApiErrorMessage(error, LOAD_ERROR));
      }
    });
  }

  onCompanyChange(company: Company): void {
    this.company.set(company);
  }

  showCompanyTab(): void {
    this.onTabChange(COMPANY_TAB);
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
    }
  }
}
