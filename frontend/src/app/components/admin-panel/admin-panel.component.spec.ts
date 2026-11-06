import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MatDialog } from '@angular/material/dialog';
import { Observable, of, throwError } from 'rxjs';
import { AdminPanelComponent } from './admin-panel.component';
import { AdminService } from '../../services/admin.service';
import { AuthService } from '../../services/auth.service';
import { JobService } from '../../services/job.service';
import { JobApplicationService } from '../../services/job-application.service';
import { CompanyService } from '../../services/company.service';
import { StatsService } from '../../services/stats.service';
import { adminStatsFixture } from '../../../testing/stats-fixtures';
import { provideRouter } from '@angular/router';

const EMPTY_PAGE = { result: [], pagination: { CurrentPage: 1, ItemsPerPage: 10, TotalItems: 0, TotalPages: 1 } };

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('AdminPanelComponent', () => {
  let fixture: ComponentFixture<AdminPanelComponent>;
  let component: AdminPanelComponent;
  let getSettings: ReturnType<typeof vi.fn>;
  let refreshSeed: ReturnType<typeof vi.fn>;
  let getManagedJobs: ReturnType<typeof vi.fn>;
  let getAllApplications: ReturnType<typeof vi.fn>;
  let getAdminStats: ReturnType<typeof vi.fn>;
  let logout: ReturnType<typeof vi.fn>;
  let alertSpy: ReturnType<typeof vi.spyOn>;
  let confirmSpy: ReturnType<typeof vi.spyOn>;

  async function create(settings: Observable<{ allowSeedRefresh: boolean }> = of({ allowSeedRefresh: true })): Promise<void> {
    getSettings.mockReturnValue(settings);
    fixture = TestBed.createComponent(AdminPanelComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  // Przełącza zakładkę przez kliknięcie nagłówka (tak jak użytkownik) i czeka na wyrenderowanie treści
  async function openTab(index: number): Promise<void> {
    const labels = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.mdc-tab');
    labels[index].click();
    for (let i = 0; i < 5; i++) {
      fixture.detectChanges();
      await fixture.whenStable();
    }
  }

  function refreshButton(): HTMLButtonElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector('.refresh-seed-button');
  }

  beforeEach(async () => {
    getSettings = vi.fn();
    refreshSeed = vi.fn().mockReturnValue(of({ message: 'ok', timestamp: 'now' }));
    getManagedJobs = vi.fn().mockReturnValue(of(EMPTY_PAGE));
    getAllApplications = vi.fn().mockReturnValue(of(EMPTY_PAGE));
    getAdminStats = vi.fn().mockReturnValue(of(adminStatsFixture()));
    logout = vi.fn();
    alertSpy = vi.spyOn(window, 'alert').mockImplementation(() => undefined);
    confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [AdminPanelComponent],
      providers: [
        provideRouter([]),
        { provide: StatsService, useValue: { getAdminStats } },
        { provide: AdminService, useValue: { getSettings, refreshSeed } },
        { provide: AuthService, useValue: { logout } },
        { provide: JobService, useValue: { getManagedJobs, deleteJob: vi.fn() } },
        { provide: JobApplicationService, useValue: { getAllApplications, deleteApplication: vi.fn() } },
        { provide: CompanyService, useValue: { getCompanies: vi.fn() } },
        { provide: MatDialog, useValue: { open: vi.fn() } }
      ]
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  describe('seed refresh availability', () => {
    it('shows the refresh button when the server allows it', async () => {
      await create(of({ allowSeedRefresh: true }));

      expect(component.allowSeedRefresh()).toBe(true);
      expect(refreshButton()).not.toBeNull();
    });

    it('hides the refresh button when the server disallows it', async () => {
      await create(of({ allowSeedRefresh: false }));

      expect(component.allowSeedRefresh()).toBe(false);
      expect(refreshButton()).toBeNull();
    });

    it('hides the refresh button when settings cannot be loaded', async () => {
      await create(throwError(() => new HttpErrorResponse({ status: 500 })));

      expect(component.allowSeedRefresh()).toBe(false);
      expect(refreshButton()).toBeNull();
    });
  });

  describe('refreshSeedData', () => {
    it('does nothing when the confirmation is declined', async () => {
      await create();
      confirmSpy.mockReturnValue(false);

      component.refreshSeedData();

      expect(refreshSeed).not.toHaveBeenCalled();
    });

    it('logs the admin out after success because the seed recreates accounts', async () => {
      await create();

      component.refreshSeedData();

      expect(refreshSeed).toHaveBeenCalledTimes(1);
      expect(alertSpy).toHaveBeenCalledWith(
        'Dane zostały pomyślnie przywrócone z seedera! Konta zostały odtworzone – zaloguj się ponownie.'
      );
      expect(logout).toHaveBeenCalledTimes(1);
      expect(component.refreshingSeed()).toBe(false);
    });

    it('hides the button and informs the user on 403', async () => {
      await create();
      refreshSeed.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 403 })));

      component.refreshSeedData();
      fixture.detectChanges();

      expect(alertSpy).toHaveBeenCalledWith('Przywracanie danych z seedera jest wyłączone na tym serwerze.');
      expect(component.allowSeedRefresh()).toBe(false);
      expect(refreshButton()).toBeNull();
      expect(component.refreshingSeed()).toBe(false);
    });

    it('shows the server message on other errors', async () => {
      await create();
      refreshSeed.mockReturnValue(
        throwError(() => new HttpErrorResponse({ status: 500, error: { message: 'Boom' } }))
      );

      component.refreshSeedData();

      expect(alertSpy).toHaveBeenCalledWith('Błąd podczas przywracania danych z seedera: Boom');
      expect(component.allowSeedRefresh()).toBe(true);
    });

    it('stays silent on 401 (handled by the interceptor)', async () => {
      await create();
      refreshSeed.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 401 })));

      component.refreshSeedData();

      expect(alertSpy).not.toHaveBeenCalled();
      expect(component.refreshingSeed()).toBe(false);
    });
  });

  describe('tab switching', () => {
    it('does not reload the jobs tab when entering the applications tab for the first time', async () => {
      await create();
      const jobCalls = getManagedJobs.mock.calls.length;

      await openTab(1);

      expect(component.selectedTabIndex()).toBe(1);
      expect(getManagedJobs.mock.calls.length).toBe(jobCalls);
    });

    it('reloads the jobs tab when returning to it', async () => {
      await create();
      await openTab(1);
      const jobCalls = getManagedJobs.mock.calls.length;

      await openTab(0);

      expect(component.selectedTabIndex()).toBe(0);
      expect(getManagedJobs.mock.calls.length).toBe(jobCalls + 1);
    });

    it('lists the statistics tab last', async () => {
      await create();

      const labels = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.mdc-tab__text-label'))
        .map(label => label.textContent?.replace(/\s+/g, ' ').trim());
      expect(labels).toEqual(['work Oferty Pracy', 'assignment Aplikacje', 'business Firmy', 'insights Statystyki']);
    });

    it('loads the statistics dashboard (lazily, in admin mode) only after visiting the tab', async () => {
      await create();
      expect((fixture.nativeElement as HTMLElement).querySelector('app-stats-dashboard')).toBeNull();
      expect(getAdminStats).not.toHaveBeenCalled();

      await openTab(3);
      await vi.waitFor(() => {
        fixture.detectChanges();
        expect((fixture.nativeElement as HTMLElement).querySelector('app-stats-dashboard')).not.toBeNull();
      });

      expect(getAdminStats).toHaveBeenCalledTimes(1);
    });
  });
});
