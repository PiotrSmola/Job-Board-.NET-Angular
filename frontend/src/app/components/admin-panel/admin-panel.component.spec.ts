import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { Observable, of, throwError } from 'rxjs';
import { AdminPanelComponent, APPLICATIONS_TAB, CATEGORIES_TAB, COMPANIES_TAB, JOBS_TAB, STATS_TAB } from './admin-panel.component';
import { AdminService } from '../../services/admin.service';
import { AuthService } from '../../services/auth.service';
import { ConfirmService } from '../../services/confirm.service';
import { JobService } from '../../services/job.service';
import { JobApplicationService } from '../../services/job-application.service';
import { CompanyService } from '../../services/company.service';
import { StatsService } from '../../services/stats.service';
import { adminStatsFixture } from '../../../testing/stats-fixtures';
import { companyPage } from '../../../testing/company-fixtures';
import { provideRouter } from '@angular/router';
import { CategoryService } from '../../services/category.service';
import { CompanyStatus } from '../../models/company.model';

const EMPTY_PAGE = { result: [], pagination: { CurrentPage: 1, ItemsPerPage: 10, TotalItems: 0, TotalPages: 1 } };

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('AdminPanelComponent', () => {
  let fixture: ComponentFixture<AdminPanelComponent>;
  let component: AdminPanelComponent;
  let getSettings: ReturnType<typeof vi.fn>;
  let refreshSeed: ReturnType<typeof vi.fn>;
  let getCompanies: ReturnType<typeof vi.fn>;
  let getManagedJobs: ReturnType<typeof vi.fn>;
  let getAllApplications: ReturnType<typeof vi.fn>;
  let getAdminStats: ReturnType<typeof vi.fn>;
  let getCategories: ReturnType<typeof vi.fn>;
  let logout: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;
  let confirmSpy: ReturnType<typeof vi.fn>;

  async function create(settings: Observable<{ allowSeedRefresh: boolean }> = of({ allowSeedRefresh: true })): Promise<void> {
    getSettings.mockReturnValue(settings);
    fixture = TestBed.createComponent(AdminPanelComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  // Przełącza zakładkę przez kliknięcie nagłówka (tak jak użytkownik) i czeka na wyrenderowanie treści
  async function openTab(index: number): Promise<void> {
    root().querySelectorAll<HTMLElement>('.mdc-tab')[index].click();
    // zmiana zakładki i dołączenie jej treści następują po kilku cyklach (Promise + animacja)
    for (let i = 0; i < 4; i++) {
      fixture.detectChanges();
      await fixture.whenStable();
      await new Promise(resolve => setTimeout(resolve, 20));
    }
    fixture.detectChanges();
  }

  function refreshButton(): HTMLButtonElement | null {
    return root().querySelector('.refresh-seed-button');
  }

  beforeEach(async () => {
    getSettings = vi.fn();
    refreshSeed = vi.fn().mockReturnValue(of({ message: 'ok', timestamp: 'now' }));
    getCompanies = vi.fn().mockReturnValue(of(EMPTY_PAGE));
    getManagedJobs = vi.fn().mockReturnValue(of(EMPTY_PAGE));
    getAllApplications = vi.fn().mockReturnValue(of(EMPTY_PAGE));
    getAdminStats = vi.fn().mockReturnValue(of(adminStatsFixture()));
    getCategories = vi.fn().mockReturnValue(of([]));
    logout = vi.fn();
    snackBarOpen = vi.fn();
    confirmSpy = vi.fn().mockReturnValue(of(true));
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [AdminPanelComponent],
      providers: [
        provideRouter([]),
        { provide: StatsService, useValue: { getAdminStats } },
        { provide: AdminService, useValue: { getSettings, refreshSeed, getCompanies } },
        { provide: AuthService, useValue: { logout } },
        { provide: JobService, useValue: { getManagedJobs, deleteJob: vi.fn() } },
        { provide: JobApplicationService, useValue: { getAllApplications, deleteApplication: vi.fn() } },
        { provide: CompanyService, useValue: { getCompanies: vi.fn() } },
        { provide: CategoryService, useValue: { getCategories } },
        { provide: MatDialog, useValue: { open: vi.fn() } },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
        { provide: ConfirmService, useValue: { confirm: confirmSpy } }
      ]
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  it('has the "Panel admina" heading and tabs in the mockup order', async () => {
    await create();

    expect(root().querySelector('h1')?.textContent?.trim()).toBe('Panel admina');
    const labels = Array.from(root().querySelectorAll('.mdc-tab__text-label')).map(label => label.textContent?.trim());
    expect(labels).toEqual(['Statystyki', 'Oferty', 'Aplikacje', 'Firmy', 'Kategorie']);
    expect(root().querySelector('mat-tab-group')?.getAttribute('aria-label')).toBe('Sekcje panelu');
  });

  describe('statistics tab (default)', () => {
    it('opens on statistics: loads the dashboard (admin mode) and the companies waiting for verification', async () => {
      await create();

      expect(component.selectedTabIndex()).toBe(STATS_TAB);
      await vi.waitFor(() => {
        fixture.detectChanges();
        expect(root().querySelector('app-stats-dashboard')).not.toBeNull();
      });
      expect(getAdminStats).toHaveBeenCalledTimes(1);
      expect(getCompanies).toHaveBeenCalledWith(expect.objectContaining({ status: CompanyStatus.Pending, orderBy: 'oldest' }));
      expect(root().querySelector('app-admin-pending-companies')).not.toBeNull();
      expect(getManagedJobs).not.toHaveBeenCalled();
    });

    it('lists pending companies with labelled approve/reject buttons', async () => {
      getCompanies.mockReturnValue(of(companyPage(2, 2)));
      await create();

      const pending = root().querySelector('app-admin-pending-companies')!;
      expect(pending.textContent).toContain('Firmy do weryfikacji');
      expect(pending.querySelector('button[aria-label="Zatwierdź firmę Firma 1"]')).not.toBeNull();
      expect(pending.querySelector('button[aria-label="Odrzuć firmę Firma 2"]')).not.toBeNull();
    });

    it('reloads the pending companies when returning to the tab', async () => {
      await create();
      await openTab(JOBS_TAB);
      const calls = getCompanies.mock.calls.length;

      await openTab(STATS_TAB);

      expect(getCompanies.mock.calls.length).toBe(calls + 1);
    });
  });

  describe('seed refresh availability', () => {
    it('shows the refresh button on the offers tab when the server allows it', async () => {
      await create(of({ allowSeedRefresh: true }));
      await openTab(JOBS_TAB);

      expect(component.allowSeedRefresh()).toBe(true);
      expect(refreshButton()).not.toBeNull();
    });

    it('hides the refresh button when the server disallows it', async () => {
      await create(of({ allowSeedRefresh: false }));
      await openTab(JOBS_TAB);

      expect(component.allowSeedRefresh()).toBe(false);
      expect(refreshButton()).toBeNull();
    });

    it('hides the refresh button when settings cannot be loaded', async () => {
      await create(throwError(() => new HttpErrorResponse({ status: 500 })));
      await openTab(JOBS_TAB);

      expect(component.allowSeedRefresh()).toBe(false);
      expect(refreshButton()).toBeNull();
    });
  });

  describe('refreshSeedData', () => {
    it('asks for confirmation in a dialog and does nothing when declined', async () => {
      await create();
      confirmSpy.mockReturnValue(of(false));

      component.refreshSeedData();

      expect(confirmSpy).toHaveBeenCalledWith(expect.objectContaining({ destructive: true }));
      expect(refreshSeed).not.toHaveBeenCalled();
    });

    it('logs the admin out after success because the seed recreates accounts', async () => {
      await create();

      component.refreshSeedData();

      expect(refreshSeed).toHaveBeenCalledTimes(1);
      expect(snackBarOpen).toHaveBeenCalledWith(
        'Dane zostały przywrócone z seedera. Konta zostały odtworzone – zaloguj się ponownie.', 'OK',
        expect.objectContaining({ politeness: 'polite' }));
      expect(logout).toHaveBeenCalledTimes(1);
      expect(component.refreshingSeed()).toBe(false);
    });

    it('hides the button and informs the user on 403', async () => {
      await create();
      await openTab(JOBS_TAB);
      refreshSeed.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 403 })));

      component.refreshSeedData();
      fixture.detectChanges();

      expect(snackBarOpen).toHaveBeenCalledWith('Przywracanie danych z seedera jest wyłączone na tym serwerze.', 'OK', expect.anything());
      expect(component.allowSeedRefresh()).toBe(false);
      expect(refreshButton()).toBeNull();
      expect(component.refreshingSeed()).toBe(false);
    });

    it('shows a generic message on server errors (without leaking details)', async () => {
      await create();
      refreshSeed.mockReturnValue(
        throwError(() => new HttpErrorResponse({ status: 500, error: { message: 'Boom' } }))
      );

      component.refreshSeedData();

      expect(snackBarOpen).toHaveBeenCalledWith('Błąd podczas przywracania danych z seedera', 'OK', expect.anything());
      expect(component.allowSeedRefresh()).toBe(true);
    });

    it('stays silent on 401 (handled by the interceptor)', async () => {
      await create();
      refreshSeed.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 401 })));

      component.refreshSeedData();

      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(component.refreshingSeed()).toBe(false);
    });
  });

  describe('tab switching', () => {
    it('loads the jobs tab only when it is opened', async () => {
      await create();
      expect(getManagedJobs).not.toHaveBeenCalled();

      await openTab(JOBS_TAB);

      expect(component.selectedTabIndex()).toBe(JOBS_TAB);
      expect(getManagedJobs).toHaveBeenCalledTimes(1);
    });

    it('does not reload the jobs tab when entering the applications tab for the first time', async () => {
      await create();
      await openTab(JOBS_TAB);
      const jobCalls = getManagedJobs.mock.calls.length;

      await openTab(APPLICATIONS_TAB);

      expect(component.selectedTabIndex()).toBe(APPLICATIONS_TAB);
      expect(getManagedJobs.mock.calls.length).toBe(jobCalls);
    });

    it('reloads the jobs tab when returning to it', async () => {
      await create();
      await openTab(JOBS_TAB);
      await openTab(APPLICATIONS_TAB);
      const jobCalls = getManagedJobs.mock.calls.length;

      await openTab(JOBS_TAB);

      expect(getManagedJobs.mock.calls.length).toBe(jobCalls + 1);
    });

    it('does not load categories before the Kategorie tab is opened', async () => {
      await create();
      expect(getCategories).not.toHaveBeenCalled();

      await openTab(CATEGORIES_TAB);

      expect(component.selectedTabIndex()).toBe(CATEGORIES_TAB);
      expect(getCategories).toHaveBeenCalled();
    });

    it('opens the companies tab', async () => {
      await create();

      await openTab(COMPANIES_TAB);

      expect(root().querySelector('app-admin-companies-tab')).not.toBeNull();
    });
  });
});
