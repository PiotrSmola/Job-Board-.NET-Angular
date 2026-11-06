import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { CompanyDetailComponent } from './company-detail.component';
import { CompanyService } from '../../services/company.service';
import { JobService } from '../../services/job.service';
import { Company, CompanyStatus } from '../../models/company.model';
import { JobOffer, WorkType } from '../../models/job-offer.model';
import { JobParams } from '../../models/job-params.model';
import { PaginatedResult } from '../../models/pagination.model';
import { makeCompany } from '../../../testing/company-fixtures';
import { jobPage, makeJob } from '../../../testing/job-fixtures';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('CompanyDetailComponent', () => {
  let harness: RouterTestingHarness;
  let component: CompanyDetailComponent;
  let getCompany: ReturnType<typeof vi.fn>;
  let getJobs: ReturnType<typeof vi.fn>;

  async function open(id: number | string = 5): Promise<void> {
    component = await harness.navigateByUrl(`/companies/${id}`, CompanyDetailComponent);
    harness.detectChanges();
  }

  function root(): HTMLElement {
    harness.detectChanges();
    return harness.routeNativeElement as HTMLElement;
  }

  function lastJobParams(): JobParams {
    return getJobs.mock.calls[getJobs.mock.calls.length - 1][0] as JobParams;
  }

  function httpError(status: number, error: unknown = null): HttpErrorResponse {
    return new HttpErrorResponse({ status, statusText: 'Error', error });
  }

  function withJobs(jobs: JobOffer[], total = jobs.length): Observable<PaginatedResult<JobOffer>> {
    return of({ ...jobPage(0, total), result: jobs });
  }

  beforeEach(async () => {
    getCompany = vi.fn().mockReturnValue(of(makeCompany(5, { name: 'Acme' })));
    getJobs = vi.fn().mockReturnValue(withJobs([]));
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [CompanyDetailComponent],
      providers: [
        provideRouter([{ path: 'companies/:id', component: CompanyDetailComponent }]),
        { provide: CompanyService, useValue: { getCompany } },
        { provide: JobService, useValue: { getJobs } }
      ]
    }).compileComponents();
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => vi.restoreAllMocks());

  describe('profile', () => {
    it('loads the company by the id from the route', async () => {
      await open(5);

      expect(getCompany).toHaveBeenCalledWith(5);
      expect(component.company()?.name).toBe('Acme');
    });

    it('shows name, location, website, active offers count, description and founding date', async () => {
      getCompany.mockReturnValue(of(makeCompany(5, {
        name: 'Acme', location: 'Rzeszów', website: 'https://acme.example.com', description: 'Robimy rzeczy',
        activeJobsCount: 4, createdAt: '2026-03-05T10:00:00Z'
      })));

      await open(5);

      const text = root().textContent ?? '';
      expect(text).toContain('Acme');
      expect(text).toContain('Rzeszów');
      expect(text).toContain('https://acme.example.com');
      expect(text).toContain('Aktywne oferty: 4');
      expect(text).toContain('Robimy rzeczy');
      expect(text).toContain('05.03.2026');
    });

    it('omits location, website and description when they are missing', async () => {
      getCompany.mockReturnValue(of(makeCompany(5, { location: null, website: null, description: null })));

      await open(5);

      expect(root().querySelector('.meta mat-icon')?.textContent).not.toBe('location_on');
      expect(root().querySelector('a.meta-item')).toBeNull();
      expect(root().querySelector('.description')).toBeNull();
    });

    it('has a link back to the companies list', async () => {
      await open(5);

      expect(root().querySelector('.back a')?.getAttribute('href')).toBe('/companies');
    });

    it('shows a spinner while the profile is loading', async () => {
      getCompany.mockReturnValue(new Subject<Company>());

      await open(5);

      expect(component.loading()).toBe(true);
      expect(root().querySelector('mat-spinner')).not.toBeNull();
    });
  });

  describe('website link', () => {
    it.each([
      ['https://acme.example.com'],
      ['http://acme.example.com/kariera'],
      ['  https://acme.example.com  ']
    ])('renders "%s" as a link opening in a new tab with noopener', async website => {
      getCompany.mockReturnValue(of(makeCompany(5, { website })));

      await open(5);

      const link = root().querySelector<HTMLAnchorElement>('a.meta-item')!;
      expect(link).not.toBeNull();
      expect(link.getAttribute('href')).toBe(website.trim());
      expect(link.target).toBe('_blank');
      expect(link.rel).toContain('noopener');
      expect(link.rel).toContain('noreferrer');
    });

    it.each([
      ['javascript:alert(document.cookie)'],
      ['JaVaScRiPt:alert(1)'],
      ['data:text/html,<script>alert(1)</script>'],
      ['vbscript:msgbox(1)'],
      ['ftp://acme.example.com'],
      ['//evil.example.com'],
      ['acme.example.com']
    ])('does not render "%s" as a link', async website => {
      getCompany.mockReturnValue(of(makeCompany(5, { website })));

      await open(5);

      expect(component.websiteUrl()).toBeNull();
      const html = root().innerHTML.toLowerCase();
      expect(root().querySelector('a.meta-item')).toBeNull();
      expect(root().querySelector('a[href^="javascript" i]')).toBeNull();
      expect(html).not.toContain('href="javascript');
      expect(html).not.toContain('href="data:');
      expect(html).not.toContain('href="vbscript');
    });
  });

  describe('status chip (admin / member view)', () => {
    it.each([
      [CompanyStatus.Pending, 'Oczekuje'],
      [CompanyStatus.Rejected, 'Odrzucona']
    ])('shows the chip for status %i', async (status, label) => {
      getCompany.mockReturnValue(of(makeCompany(5, { status })));

      await open(5);

      expect(root().querySelector('mat-card-title app-status-chip')?.textContent?.trim()).toBe(label);
    });

    it('shows no chip for an approved company or when the API omits the status', async () => {
      getCompany.mockReturnValue(of(makeCompany(5, { status: CompanyStatus.Approved })));
      await open(5);
      expect(root().querySelector('app-status-chip')).toBeNull();

      getCompany.mockReturnValue(of(makeCompany(6, { status: null })));
      await open(6);
      expect(root().querySelector('app-status-chip')).toBeNull();
    });
  });

  describe('offers', () => {
    it('requests the offers filtered by the company id (page 1, 10 per page)', async () => {
      await open(5);

      expect(getJobs).toHaveBeenCalledTimes(1);
      const params = lastJobParams();
      expect(params.companyId).toBe(5);
      expect(params.pageNumber).toBe(1);
      expect(params.pageSize).toBe(10);
    });

    it('does not request offers before the company is loaded', async () => {
      getCompany.mockReturnValue(new Subject<Company>());

      await open(5);

      expect(getJobs).not.toHaveBeenCalled();
    });

    it('renders each offer as a link to its details with location, work type and salary', async () => {
      getJobs.mockReturnValue(withJobs([
        makeJob(11, { title: 'Dev Angular', location: 'Kraków', workType: WorkType.Hybrid, salaryMin: 8000, salaryMax: 12000 }),
        makeJob(12, { title: 'Tester', workType: WorkType.Onsite, salaryMin: null, salaryMax: null })
      ]));

      await open(5);

      const rows = root().querySelectorAll<HTMLAnchorElement>('a.job-row');
      expect(rows.length).toBe(2);
      expect(rows[0].getAttribute('href')).toBe('/jobs/11');
      expect(rows[0].textContent).toContain('Dev Angular');
      expect(rows[0].textContent).toContain('Kraków · Hybrydowa · 8000 - 12000 PLN');
      expect(rows[1].textContent).toContain('Stacjonarna · Nieokreślona');
    });

    it.each([
      [5000, null, 'Od 5000 PLN'],
      [null, 9000, 'Do 9000 PLN']
    ])('formats a one-sided salary (%s, %s) as "%s"', async (salaryMin, salaryMax, expected) => {
      await open(5);

      expect(component.formatSalary(makeJob(1, { salaryMin, salaryMax }))).toBe(expected);
    });

    it('shows an empty state when the company has no active offers', async () => {
      await open(5);

      expect(root().textContent).toContain('Ta firma nie ma obecnie aktywnych ofert');
      expect(root().querySelector('mat-paginator')).toBeNull();
    });

    it('shows no paginator for up to 10 offers and a paginator above', async () => {
      getJobs.mockReturnValue(withJobs([makeJob(1)], 10));
      await open(5);
      expect(root().querySelector('mat-paginator')).toBeNull();

      getJobs.mockReturnValue(withJobs([makeJob(1)], 25));
      await open(6);
      expect(root().querySelector('mat-paginator')).not.toBeNull();
    });

    it('requests the next page of offers from the paginator', async () => {
      getJobs.mockReturnValue(withJobs([makeJob(1)], 25));
      await open(5);

      root().querySelector<HTMLButtonElement>('.mat-mdc-paginator-navigation-next')!.click();

      expect(lastJobParams().pageNumber).toBe(2);
      expect(lastJobParams().companyId).toBe(5);
      expect(component.jobsPageIndex()).toBe(1);
    });

    it('shows the empty offers list (not an error) when loading offers fails', async () => {
      getJobs.mockReturnValue(throwError(() => httpError(500)));

      await open(5);

      expect(component.jobs()).toEqual([]);
      expect(component.jobsLoading()).toBe(false);
      expect(root().textContent).toContain('Ta firma nie ma obecnie aktywnych ofert');
      expect(root().textContent).toContain('Acme');
    });
  });

  describe('errors', () => {
    it.each([404, 400])('shows the "not found" state for %i and does not request offers', async status => {
      getCompany.mockReturnValue(throwError(() => httpError(status)));

      await open(999);

      expect(component.notFound()).toBe(true);
      expect(root().textContent).toContain('Nie znaleziono firmy');
      expect(root().querySelector('.company-card')).toBeNull();
      expect(getJobs).not.toHaveBeenCalled();
    });

    it('treats a non-numeric id as not found', async () => {
      getCompany.mockReturnValue(throwError(() => httpError(400)));

      await open('abc');

      expect(getCompany).toHaveBeenCalledWith(NaN);
      expect(component.notFound()).toBe(true);
    });

    it('shows a retry state on a server error, and retry loads the profile', async () => {
      getCompany.mockReturnValue(throwError(() => httpError(500)));

      await open(5);

      expect(component.notFound()).toBe(false);
      expect(root().textContent).toContain('Nie udało się wczytać profilu firmy');

      getCompany.mockReturnValue(of(makeCompany(5, { name: 'Acme' })));
      Array.from(root().querySelectorAll<HTMLButtonElement>('button'))
        .find(b => b.textContent?.includes('Spróbuj ponownie'))!.click();

      expect(component.loadError()).toBeNull();
      expect(root().textContent).toContain('Acme');
      expect(getJobs).toHaveBeenCalledTimes(1);
    });

    it('shows a connection message when the server is unreachable', async () => {
      getCompany.mockReturnValue(throwError(() => httpError(0)));

      await open(5);

      expect(component.loadError()).toBe('Brak połączenia z serwerem');
    });
  });

  describe('route changes', () => {
    // switchMap na kolejnych wczytaniach: spóźniona odpowiedź dla poprzedniego :id jest anulowana
    it('ignores a late response for the previous company when the id changes quickly', async () => {
      const first = new Subject<Company>();
      getCompany.mockReturnValueOnce(first);
      await open(5);
      getCompany.mockReturnValue(of(makeCompany(6, { name: 'Beta' })));
      await open(6);
      expect(component.company()?.name).toBe('Beta');
      const jobsCalls = getJobs.mock.calls.length;

      first.next(makeCompany(5, { name: 'Acme' })); // spóźniona odpowiedź dla poprzedniego id

      expect(component.company()?.name).toBe('Beta');
      expect(first.observed).toBe(false);
      expect(getJobs.mock.calls.length).toBe(jobsCalls);
      expect(lastJobParams().companyId).toBe(6);
    });

    it('ignores late offers of the previous company when the id changes', async () => {
      const firstJobs = new Subject<PaginatedResult<JobOffer>>();
      getJobs.mockReturnValueOnce(firstJobs);
      await open(5);
      getCompany.mockReturnValue(of(makeCompany(6, { name: 'Beta' })));
      getJobs.mockReturnValue(withJobs([makeJob(60)]));
      await open(6);

      firstJobs.next({ ...jobPage(0, 1), result: [makeJob(50)] });

      expect(component.jobs().map(j => j.id)).toEqual([60]);
      expect(component.jobsLoading()).toBe(false);
    });

    it('a retry cancels the previous pending request', async () => {
      const pending = new Subject<Company>();
      getCompany.mockReturnValueOnce(pending);
      await open(5);
      getCompany.mockReturnValue(of(makeCompany(5, { name: 'Ponownie' })));

      component.loadCompany();
      pending.next(makeCompany(5, { name: 'Stara' }));

      expect(component.company()?.name).toBe('Ponownie');
      expect(component.loading()).toBe(false);
    });

    it('reloads the profile and the offers (from page 1) when the id changes', async () => {
      getJobs.mockReturnValue(withJobs([makeJob(1)], 25));
      await open(5);
      component.onJobsPageChange({ pageIndex: 1, pageSize: 10, length: 25, previousPageIndex: 0 });
      expect(component.jobsPageIndex()).toBe(1);
      getCompany.mockReturnValue(of(makeCompany(6, { name: 'Beta' })));

      await open(6);

      expect(getCompany).toHaveBeenLastCalledWith(6);
      expect(component.company()?.name).toBe('Beta');
      expect(component.jobsPageIndex()).toBe(0);
      expect(lastJobParams().companyId).toBe(6);
      expect(lastJobParams().pageNumber).toBe(1);
    });
  });
});
