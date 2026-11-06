import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component, forwardRef, input, output } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { By } from '@angular/platform-browser';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { EmployerPanelComponent } from './employer-panel.component';
import { EmployerCompanyTabComponent } from './employer-company-tab/employer-company-tab.component';
import { AdminJobsTabComponent } from '../admin-panel/admin-jobs-tab/admin-jobs-tab.component';
import { AdminApplicationsTabComponent } from '../admin-panel/admin-applications-tab/admin-applications-tab.component';
import { CompanyService } from '../../services/company.service';
import { StatsService } from '../../services/stats.service';
import { employerStatsFixture } from '../../../testing/stats-fixtures';
import { Company, CompanyStatus } from '../../models/company.model';
import { PanelMode } from '../../models/panel-mode.model';
import { makeCompany } from '../../../testing/company-fixtures';

// Zakładki zastąpione atrapami (z tymi samymi tokenami, które odpytuje viewChild panelu)
@Component({
  selector: 'app-admin-jobs-tab',
  template: '<div class="stub-jobs">{{ mode() }}</div>',
  providers: [{ provide: AdminJobsTabComponent, useExisting: forwardRef(() => StubJobsTabComponent) }]
})
class StubJobsTabComponent {
  readonly mode = input<PanelMode>('admin');
  readonly company = input<Company | null>(null);
  readonly reload = vi.fn();
}

@Component({
  selector: 'app-admin-applications-tab',
  template: '<div class="stub-applications">{{ mode() }}</div>',
  providers: [{ provide: AdminApplicationsTabComponent, useExisting: forwardRef(() => StubApplicationsTabComponent) }]
})
class StubApplicationsTabComponent {
  readonly mode = input<PanelMode>('admin');
  readonly reload = vi.fn();
}

@Component({
  selector: 'app-employer-company-tab',
  template: '<div class="stub-company">{{ company().name }}</div>'
})
class StubCompanyTabComponent {
  readonly company = input.required<Company>();
  readonly companyChange = output<Company>();
}

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('EmployerPanelComponent', () => {
  let fixture: ComponentFixture<EmployerPanelComponent>;
  let component: EmployerPanelComponent;
  let getMyCompany: ReturnType<typeof vi.fn>;
  let getEmployerStats: ReturnType<typeof vi.fn>;

  async function create(company$ = of(makeCompany(3, { name: 'Acme', status: CompanyStatus.Approved }))): Promise<void> {
    getMyCompany.mockReturnValue(company$);
    fixture = TestBed.createComponent(EmployerPanelComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function tabLabels(): string[] {
    return Array.from(root().querySelectorAll('.mdc-tab__text-label')).map(l => l.textContent?.replace(/\s+/g, ' ').trim() ?? '');
  }

  async function selectTab(index: number): Promise<void> {
    root().querySelectorAll<HTMLElement>('.mdc-tab')[index].click();
    // zmiana zakładki i dołączenie jej treści następują po kilku cyklach (Promise + animacja)
    for (let i = 0; i < 3; i++) {
      fixture.detectChanges();
      await fixture.whenStable();
      await new Promise(resolve => setTimeout(resolve, 20));
    }
    fixture.detectChanges();
  }

  function notice(): HTMLElement | null {
    return root().querySelector<HTMLElement>('.approval-notice');
  }

  beforeEach(async () => {
    getMyCompany = vi.fn();
    getEmployerStats = vi.fn().mockReturnValue(of(employerStatsFixture()));
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [EmployerPanelComponent],
      providers: [
        provideRouter([]),
        { provide: CompanyService, useValue: { getMyCompany } },
        { provide: StatsService, useValue: { getEmployerStats } },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } }
      ]
    })
      .overrideComponent(EmployerPanelComponent, {
        remove: { imports: [AdminJobsTabComponent, AdminApplicationsTabComponent, EmployerCompanyTabComponent] },
        add: { imports: [StubJobsTabComponent, StubApplicationsTabComponent, StubCompanyTabComponent] }
      })
      .compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  describe('loading the company', () => {
    it('shows a spinner while the company is loading', async () => {
      await create(new Subject<Company>() as never);

      expect(component.loading()).toBe(true);
      expect(root().querySelector('mat-spinner')).not.toBeNull();
      expect(root().querySelector('mat-tab-group')).toBeNull();
    });

    it('shows the company name and status chip in the header', async () => {
      await create(of(makeCompany(3, { name: 'Acme', status: CompanyStatus.Pending })));

      expect(root().querySelector('h1')?.textContent).toContain('Panel pracodawcy');
      const subtitle = root().querySelector('.company-subtitle')!;
      expect(subtitle.textContent).toContain('Acme');
      expect(subtitle.querySelector('app-status-chip')?.textContent?.trim()).toBe('Oczekuje');
    });

    it('shows an error with a retry button when loading fails, and retries', async () => {
      await create(throwError(() => new HttpErrorResponse({ status: 500 })));

      expect(root().textContent).toContain('Nie udało się wczytać danych firmy');
      expect(root().querySelector('mat-tab-group')).toBeNull();

      getMyCompany.mockReturnValue(of(makeCompany(3, { name: 'Acme' })));
      const retry = Array.from(root().querySelectorAll<HTMLButtonElement>('button')).find(b => b.textContent?.includes('Spróbuj ponownie'))!;
      retry.click();

      expect(getMyCompany).toHaveBeenCalledTimes(2);
      expect(component.loadError()).toBeNull();
      expect(root().querySelector('mat-tab-group')).not.toBeNull();
    });

    it.each([401, 403])('shows the error state with a retry button but not the reason handled by the interceptor (%i)', async status => {
      await create(throwError(() => new HttpErrorResponse({
        status, error: { message: 'Company must be approved before publishing offers' }
      })));

      expect(component.loadError()).toBe('Nie udało się wczytać danych firmy');
      expect(root().textContent).not.toContain('Firma musi zostać zatwierdzona');
      expect(root().querySelector('mat-tab-group')).toBeNull();
      const buttons = Array.from(root().querySelectorAll<HTMLButtonElement>('button'));
      expect(buttons.some(b => b.textContent?.includes('Spróbuj ponownie'))).toBe(true);
    });

    it('shows the translated API reason for other errors (e.g. 404 without a company)', async () => {
      await create(throwError(() => new HttpErrorResponse({ status: 404, error: { message: 'Company not found' } })));

      expect(root().textContent).toContain('Firma nie została znaleziona');
      expect(root().textContent).toContain('Spróbuj ponownie');
    });

    it('keeps the error state when the retry fails again', async () => {
      await create(throwError(() => new HttpErrorResponse({ status: 500 })));

      component.loadCompany();

      expect(getMyCompany).toHaveBeenCalledTimes(2);
      expect(component.loading()).toBe(false);
      expect(root().textContent).toContain('Nie udało się wczytać danych firmy');
      expect(root().querySelector('mat-spinner')).toBeNull();
    });
  });

  describe('tabs', () => {
    it('renders the tabs: Oferty, Aplikacje, Profil firmy, Statystyki', async () => {
      await create();

      expect(tabLabels()).toEqual(['work Oferty', 'assignment Aplikacje', 'business Profil firmy', 'insights Statystyki']);
    });

    it('loads the statistics dashboard (lazily, in employer mode) only after visiting the tab', async () => {
      await create();
      expect(root().querySelector('app-stats-dashboard')).toBeNull();
      expect(getEmployerStats).not.toHaveBeenCalled();

      await selectTab(3);
      await vi.waitFor(() => {
        fixture.detectChanges();
        expect(root().querySelector('app-stats-dashboard')).not.toBeNull();
      });

      expect(getEmployerStats).toHaveBeenCalledTimes(1);
      expect(root().querySelector('app-stats-jobs-table h3')?.textContent).toContain('Oferty firmy');
    });

    it('does not reload anything when switching to the statistics tab', async () => {
      await create();
      const jobs = fixture.debugElement.query(By.directive(StubJobsTabComponent)).componentInstance as StubJobsTabComponent;

      await selectTab(3);

      expect(jobs.reload).not.toHaveBeenCalled();
    });

    it('opens on the offers tab in employer mode with the employer company', async () => {
      const company = makeCompany(3, { name: 'Acme', status: CompanyStatus.Approved });
      await create(of(company));

      const jobs = fixture.debugElement.query(By.directive(StubJobsTabComponent));
      expect(jobs).not.toBeNull();
      expect(jobs.componentInstance.mode()).toBe('employer');
      expect(jobs.componentInstance.company()).toEqual(company);
      expect(component.selectedTabIndex()).toBe(0);
    });

    it('creates the applications tab (employer mode) only after visiting it', async () => {
      await create();
      expect(fixture.debugElement.query(By.directive(StubApplicationsTabComponent))).toBeNull();

      await selectTab(1);

      const apps = fixture.debugElement.query(By.directive(StubApplicationsTabComponent));
      expect(apps).not.toBeNull();
      expect(apps.componentInstance.mode()).toBe('employer');
      expect(component.selectedTabIndex()).toBe(1);
    });

    it('passes the company to the profile tab', async () => {
      await create(of(makeCompany(3, { name: 'Acme' })));

      await selectTab(2);

      expect(root().querySelector('.stub-company')?.textContent).toBe('Acme');
    });

    it('reloads the offers tab on return and does not reload on the first visit', async () => {
      await create();
      const jobs = fixture.debugElement.query(By.directive(StubJobsTabComponent)).componentInstance as StubJobsTabComponent;
      expect(jobs.reload).not.toHaveBeenCalled();

      await selectTab(2);
      expect(jobs.reload).not.toHaveBeenCalled();
      await selectTab(0);

      expect(jobs.reload).toHaveBeenCalledTimes(1);
    });

    it('reloads the applications tab only on a return visit', async () => {
      await create();
      await selectTab(1);
      const apps = fixture.debugElement.query(By.directive(StubApplicationsTabComponent)).componentInstance as StubApplicationsTabComponent;
      expect(apps.reload).not.toHaveBeenCalled();

      await selectTab(0);
      await selectTab(1);

      expect(apps.reload).toHaveBeenCalledTimes(1);
    });

    it('does not reload anything when switching to the profile tab', async () => {
      await create();
      const jobs = fixture.debugElement.query(By.directive(StubJobsTabComponent)).componentInstance as StubJobsTabComponent;

      await selectTab(2);
      await selectTab(0);
      await selectTab(2);

      expect(jobs.reload).toHaveBeenCalledTimes(1); // tylko powrót na Oferty
    });

    it('updates the header when the profile tab reports a changed company', async () => {
      await create(of(makeCompany(3, { name: 'Acme', status: CompanyStatus.Rejected })));
      await selectTab(2);
      const profile = fixture.debugElement.query(By.directive(StubCompanyTabComponent));

      profile.componentInstance.companyChange.emit(makeCompany(3, { name: 'Acme 2', status: CompanyStatus.Pending }));

      const subtitle = root().querySelector('.company-subtitle')!;
      expect(subtitle.textContent).toContain('Acme 2');
      expect(subtitle.querySelector('app-status-chip')?.textContent?.trim()).toBe('Oczekuje');
    });
  });

  describe('not-approved company notice', () => {
    it.each([
      [CompanyStatus.Pending],
      [CompanyStatus.Rejected]
    ])('is shown on the offers tab for status %i', async status => {
      await create(of(makeCompany(3, { status })));

      expect(component.isApproved()).toBe(false);
      expect(notice()).not.toBeNull();
      expect(notice()!.textContent).toContain('Firma nie jest jeszcze zatwierdzona');
      expect(notice()!.textContent).toContain('szkice');
    });

    it('is not shown for an approved company', async () => {
      await create(of(makeCompany(3, { status: CompanyStatus.Approved })));

      expect(component.isApproved()).toBe(true);
      expect(notice()).toBeNull();
    });

    it('is shown on the applications tab too', async () => {
      await create(of(makeCompany(3, { status: CompanyStatus.Pending })));

      await selectTab(1);

      expect(notice()).not.toBeNull();
    });

    it('is hidden on the profile tab (which has its own banner)', async () => {
      await create(of(makeCompany(3, { status: CompanyStatus.Pending })));

      await selectTab(2);

      expect(notice()).toBeNull();
    });

    it('its "Profil firmy" button switches to the profile tab', async () => {
      await create(of(makeCompany(3, { status: CompanyStatus.Pending })));

      notice()!.querySelector<HTMLButtonElement>('button')!.click();
      fixture.detectChanges();
      await fixture.whenStable();

      expect(component.selectedTabIndex()).toBe(component.companyTab);
      expect(notice()).toBeNull();
    });

    it('disappears once the company becomes approved', async () => {
      await create(of(makeCompany(3, { status: CompanyStatus.Pending })));
      expect(notice()).not.toBeNull();

      component.onCompanyChange(makeCompany(3, { status: CompanyStatus.Approved }));

      expect(notice()).toBeNull();
    });
  });
});
