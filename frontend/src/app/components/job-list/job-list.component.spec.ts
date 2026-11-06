import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, ParamMap, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { JobListComponent } from './job-list.component';
import { ContractTypes, Currency, JobOffer, SalaryPeriod, SeniorityLevel, WorkType } from '../../models/job-offer.model';
import { Category, JobFacets } from '../../models/taxonomy.model';
import { makeJob } from '../../../testing/job-fixtures';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

const CATEGORIES: Category[] = [
  { id: 1, name: 'Programowanie', slug: 'programowanie', sortOrder: 1, activeJobsCount: 20 },
  { id: 2, name: 'DevOps i chmura', slug: 'devops-i-chmura', sortOrder: 2, activeJobsCount: 8 }
];

const FACETS: JobFacets = {
  total: 35,
  categories: [{ id: 1, slug: 'programowanie', name: 'Programowanie', count: 20 }],
  seniority: [{ value: SeniorityLevel.Junior, count: 12 }, { value: SeniorityLevel.Mid, count: 1 }],
  contractTypes: [{ value: ContractTypes.B2B, count: 7 }],
  workTypes: [{ value: WorkType.Remote, count: 22 }],
  skills: [{ name: 'Angular', count: 9 }, { name: 'Docker', count: 4 }]
};

function pagination(total: number, page = 1, size = 10): string {
  return JSON.stringify({ CurrentPage: page, ItemsPerPage: size, TotalItems: total, TotalPages: Math.max(1, Math.ceil(total / size)) });
}

function jobs(count: number): JobOffer[] {
  return Array.from({ length: count }, (_, i) => makeJob(i + 1));
}

describe('JobListComponent', () => {
  let fixture: ComponentFixture<JobListComponent>;
  let component: JobListComponent;
  let http: HttpTestingController;
  let router: Router;
  let query$: BehaviorSubject<ParamMap>;
  let navigate: ReturnType<typeof vi.spyOn>;

  const root = () => fixture.nativeElement as HTMLElement;
  const jobRequests = () => http.match(r => r.url === '/api/jobs');
  const facetRequests = () => http.match(r => r.url === '/api/jobs/facets');
  const lastNavigation = () => {
    const call = navigate.mock.calls[navigate.mock.calls.length - 1];
    return { query: (call[1] as { queryParams: Record<string, string> }).queryParams, replaceUrl: (call[1] as { replaceUrl: boolean }).replaceUrl };
  };

  function respond(request: TestRequest, items: JobOffer[], total: number): void {
    request.flush(items, { headers: { Pagination: pagination(total) } });
    fixture.detectChanges();
  }

  /** Odpowiada na wszystkie oczekujące żądania (kategorie, oferty, facety). */
  function flushAll(items: JobOffer[] = jobs(10), total = 35): void {
    http.match('/api/categories').forEach(r => r.flush(CATEGORIES));
    // anulowane żądania (poprzednie filtry) zostają bez odpowiedzi
    facetRequests().filter(r => !r.cancelled).forEach(r => r.flush(FACETS));
    jobRequests().filter(r => !r.cancelled).forEach(r => r.flush(items, { headers: { Pagination: pagination(total) } }));
    fixture.detectChanges();
  }

  function setUrl(search: string): void {
    query$.next(convertToParamMap(Object.fromEntries(new URLSearchParams(search))));
  }

  function create(search = ''): void {
    setUrl(search);
    fixture = TestBed.createComponent(JobListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function clickByLabel(label: string): void {
    root().querySelector<HTMLInputElement>(`input[aria-label="${label}"]`)!.click();
    fixture.detectChanges();
  }

  beforeEach(() => {
    vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    TestBed.configureTestingModule({
      imports: [JobListComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);

    // Adres URL symulujemy strumieniem query params; nawigacja komponentu „zapisuje” nowy adres w tym strumieniu
    query$ = new BehaviorSubject<ParamMap>(convertToParamMap({}));
    Object.defineProperty(TestBed.inject(ActivatedRoute), 'queryParamMap', { value: query$ });
    navigate = vi.spyOn(router, 'navigate').mockImplementation(async (_commands, extras) => {
      query$.next(convertToParamMap((extras?.queryParams ?? {}) as Record<string, string>));
      return true;
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    fixture?.destroy();
    http.verify({ ignoreCancelled: true });
    vi.restoreAllMocks();
  });

  describe('loading', () => {
    it('requests the first page, the facets and the categories on init', () => {
      create();

      const [jobsReq] = jobRequests();
      expect(jobsReq.request.params.get('pageNumber')).toBe('1');
      expect(jobsReq.request.params.get('pageSize')).toBe('10');
      expect(facetRequests().length).toBe(1);
      expect(http.match('/api/categories').length).toBe(1);
    });

    it('shows skeleton cards with reserved height until the first response arrives (no layout shift)', () => {
      create();

      expect(root().querySelectorAll('.job-skeleton').length).toBe(6);
      expect(root().querySelector('.results-info')).not.toBeNull();
      expect(root().querySelector('.jobs-grid')?.getAttribute('aria-busy')).toBe('true');
      expect(root().querySelector('.job-card')).toBeNull();

      flushAll();

      expect(root().querySelectorAll('.job-skeleton').length).toBe(0);
      expect(root().querySelectorAll('.job-card').length).toBe(10);
      expect(root().querySelector('.jobs-grid')?.getAttribute('aria-busy')).toBe('false');
    });

    it('keeps the current cards (dimmed, aria-busy) while the next results load instead of replacing them with skeletons', () => {
      create();
      flushAll();

      setUrl('seniority=1');
      fixture.detectChanges();

      expect(root().querySelectorAll('.job-skeleton').length).toBe(0);
      expect(root().querySelectorAll('.job-card').length).toBe(10);
      const grid = root().querySelector('.jobs-grid')!;
      expect(grid.classList.contains('stale')).toBe(true);
      expect(grid.getAttribute('aria-busy')).toBe('true');
      flushAll();
    });

    it('shows a message when there are no results', () => {
      create();
      flushAll([], 0);

      expect(root().querySelector('.no-results h3')?.textContent).toContain('Brak ofert');
      expect(root().querySelector('mat-paginator')).toBeNull();
    });
  });

  describe('accessibility of the results', () => {
    it('announces the number of results in a polite live region', () => {
      create();
      const info = root().querySelector('.results-info')!;
      expect(info.getAttribute('role')).toBe('status');
      expect(info.getAttribute('aria-live')).toBe('polite');
      expect(info.textContent?.trim()).toBe('');

      flushAll(jobs(10), 35);

      expect(info.textContent?.replace(/\s+/g, ' ').trim()).toBe('Znaleziono 35 ofert');
    });

    it.each([[1, 'Znaleziono 1 oferta'], [3, 'Znaleziono 3 oferty'], [12, 'Znaleziono 12 ofert']])(
      'uses the correct Polish plural for %s results', (total, expected) => {
        create();
        flushAll(jobs(1), total);

        expect(root().querySelector('.results-info')?.textContent?.replace(/\s+/g, ' ').trim()).toBe(expected);
      });

    it('does not clear the announcement while the next results are loading', () => {
      create();
      flushAll();

      setUrl('skills=Angular');
      fixture.detectChanges();

      expect(root().querySelector('.results-info')?.textContent).toContain('Znaleziono');
      flushAll();
    });

    it('does not show a count when loading failed', () => {
      create();
      http.match('/api/categories').forEach(r => r.flush(CATEGORIES));
      jobRequests().forEach(r => r.flush(null, { status: 500, statusText: 'Server Error' }));
      fixture.detectChanges();

      expect(root().querySelector('.results-info')?.textContent?.trim()).toBe('');
    });
  });

  describe('filters from the URL', () => {
    it('reads all filters from the query params on entry and sends them to the API', () => {
      create('category=programowanie&seniority=1,2&contractTypes=3&workType=0&skills=Angular,Docker&currency=1&minSalary=5000&page=2&pageSize=20&orderBy=oldest');

      const [req] = jobRequests();
      const params = req.request.params;
      expect(params.get('category')).toBe('programowanie');
      expect(params.get('seniority')).toBe('1,2');
      expect(params.get('contractTypes')).toBe('3');
      expect(params.get('workType')).toBe('0');
      expect(params.get('skills')).toBe('Angular,Docker');
      expect(params.get('currency')).toBe('1');
      expect(params.get('minSalary')).toBe('5000');
      expect(params.get('pageNumber')).toBe('2');
      expect(params.get('pageSize')).toBe('20');
      expect(params.get('orderBy')).toBe('oldest');
      flushAll();
    });

    it('reflects the URL filters in the panel and in the active filter chips', () => {
      create('seniority=1&skills=Angular&category=devops-i-chmura');
      flushAll();

      expect(root().querySelector<HTMLInputElement>('input[aria-label^="Junior"]')!.checked).toBe(true);
      expect(root().querySelector<HTMLInputElement>('input[aria-label^="Mid"]')!.checked).toBe(false);
      const chips = Array.from(root().querySelectorAll('app-removable-chips .chip span')).map(s => s.textContent);
      expect(chips).toContain('Kategoria: DevOps i chmura');
      expect(chips).toContain('Junior');
      expect(chips).toContain('Angular');
    });

    it('ignores invalid values from a hand-edited URL', () => {
      create('seniority=abc,99&workType=9&page=-1&orderBy=hax');

      const [req] = jobRequests();
      expect(req.request.params.has('seniority')).toBe(false);
      expect(req.request.params.has('workType')).toBe(false);
      expect(req.request.params.get('pageNumber')).toBe('1');
      expect(req.request.params.get('orderBy')).toBe('newest');
      flushAll();
    });
  });

  describe('URL synchronization', () => {
    beforeEach(() => {
      create();
      flushAll();
    });

    it('ticking a checkbox writes the filter to the URL as a new history entry and resets the page', () => {
      setUrl('page=3');
      flushAll();

      clickByLabel('Junior, 12 ofert');

      expect(lastNavigation().query).toEqual({ seniority: '1' });
      expect(lastNavigation().replaceUrl).toBe(false);
      expect(navigate.mock.calls[navigate.mock.calls.length - 1][1]).toMatchObject({ relativeTo: expect.anything() });
      flushAll();
    });

    it('the whole cycle URL -> request works for several filters ticked one after another', () => {
      clickByLabel('Junior, 12 ofert');
      flushAll();
      clickByLabel('B2B, 7 ofert');
      flushAll();

      expect(lastNavigation().query).toEqual({ seniority: '1', contractTypes: '2' });
      flushAll();
    });

    it('typing in the search box is debounced by 500 ms and replaces the history entry', () => {
      vi.useFakeTimers();
      const input = root().querySelector<HTMLInputElement>('input[type=search]')!;
      const before = navigate.mock.calls.length;

      input.value = 'ang';
      input.dispatchEvent(new Event('input'));
      input.value = 'angular';
      input.dispatchEvent(new Event('input'));
      vi.advanceTimersByTime(499);
      expect(navigate.mock.calls.length).toBe(before);

      vi.advanceTimersByTime(1);

      expect(navigate.mock.calls.length).toBe(before + 1);
      expect(lastNavigation().query).toEqual({ searchTerm: 'angular' });
      expect(lastNavigation().replaceUrl).toBe(true);
      flushAll();
    });

    it('Enter in the search box applies the phrase immediately', () => {
      const input = root().querySelector<HTMLInputElement>('input[type=search]')!;
      input.value = 'rust';
      input.dispatchEvent(new Event('input'));

      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));

      expect(lastNavigation().query).toEqual({ searchTerm: 'rust' });
      expect(lastNavigation().replaceUrl).toBe(false);
      flushAll();
    });

    it('salary typing is debounced by 800 ms and replaces the history entry', () => {
      vi.useFakeTimers();
      const [min] = Array.from(root().querySelectorAll<HTMLInputElement>('input[type=number]'));
      const before = navigate.mock.calls.length;

      min.value = '8000';
      min.dispatchEvent(new Event('input'));
      vi.advanceTimersByTime(799);
      expect(navigate.mock.calls.length).toBe(before);
      vi.advanceTimersByTime(1);

      expect(lastNavigation().query).toEqual({ minSalary: '8000' });
      expect(lastNavigation().replaceUrl).toBe(true);
      flushAll();
    });

    it('does not send salary ranges where "od" exceeds "do" and shows an inline alert instead', () => {
      vi.useFakeTimers();
      const [min, max] = Array.from(root().querySelectorAll<HTMLInputElement>('input[type=number]'));
      const before = navigate.mock.calls.length;

      min.value = '9000';
      min.dispatchEvent(new Event('input'));
      max.value = '5000';
      max.dispatchEvent(new Event('input'));
      vi.advanceTimersByTime(1000);
      fixture.detectChanges();

      expect(navigate.mock.calls.length).toBe(before);
      const alert = root().querySelector('#salary-error')!;
      expect(alert.getAttribute('role')).toBe('alert');
      expect(alert.textContent).toContain('nie może być wyższe');
    });

    it('the browser Back/Forward (URL change) reloads results and restores the filters in the panel', () => {
      clickByLabel('Junior, 12 ofert');
      flushAll();
      expect(root().querySelector<HTMLInputElement>('input[aria-label^="Junior"]')!.checked).toBe(true);

      setUrl(''); // wstecz
      fixture.detectChanges();
      const [backReq] = jobRequests();
      expect(backReq.request.params.has('seniority')).toBe(false);
      respond(backReq, jobs(10), 35);
      facetRequests().forEach(r => r.flush(FACETS));
      fixture.detectChanges();
      expect(root().querySelector<HTMLInputElement>('input[aria-label^="Junior"]')!.checked).toBe(false);

      setUrl('seniority=1'); // naprzód
      fixture.detectChanges();
      const [forwardReq] = jobRequests();
      expect(forwardReq.request.params.get('seniority')).toBe('1');
      respond(forwardReq, jobs(3), 3);
      facetRequests().forEach(r => r.flush(FACETS));
      fixture.detectChanges();
      expect(root().querySelector<HTMLInputElement>('input[aria-label^="Junior"]')!.checked).toBe(true);
      expect(root().querySelectorAll('.job-card').length).toBe(3);
    });

    it('overwrites the typed search text with the value from the URL after Back', async () => {
      setUrl('searchTerm=angular');
      fixture.detectChanges();
      flushAll();
      await fixture.whenStable();
      const input = root().querySelector<HTMLInputElement>('input[type=search]')!;
      expect(input.value).toBe('angular');

      setUrl('');
      fixture.detectChanges();
      flushAll();
      await fixture.whenStable();

      expect(input.value).toBe('');
    });

    it('clearing the filters navigates to a URL without any params', () => {
      setUrl('seniority=1&skills=Angular&page=2');
      flushAll();

      component.clearFilters();

      expect(lastNavigation().query).toEqual({});
      expect(lastNavigation().replaceUrl).toBe(false);
      flushAll();
    });

    it('the "Wyczyść filtry" button is disabled without active filters', () => {
      const button = root().querySelector<HTMLButtonElement>('button.clear')!;
      expect(button.disabled).toBe(true);

      setUrl('seniority=1');
      flushAll();

      expect(root().querySelector<HTMLButtonElement>('button.clear')!.disabled).toBe(false);
    });
  });

  describe('active filter chips', () => {
    it('removing a chip drops only that filter value from the URL', () => {
      create('seniority=1,2&skills=Angular');
      flushAll();

      root().querySelector<HTMLButtonElement>('button[aria-label="Usuń filtr: Junior"]')!.click();

      expect(lastNavigation().query).toEqual({ seniority: '2', skills: 'Angular' });
      flushAll();
    });

    it('moves focus to a neighbouring chip when one is removed', () => {
      create('seniority=1,2');
      flushAll();
      const buttons = root().querySelectorAll<HTMLButtonElement>('app-removable-chips button');

      buttons[0].click();

      expect(document.activeElement).toBe(buttons[1]);
      flushAll();
    });

    it('moves focus to the results region when the last chip is removed', () => {
      create('seniority=1');
      flushAll();

      root().querySelector<HTMLButtonElement>('app-removable-chips button')!.click();

      expect(document.activeElement).toBe(root().querySelector('.results-info'));
      flushAll();
    });

    it('shows no chip list without filters', () => {
      create();
      flushAll();

      expect(root().querySelector('app-removable-chips ul')).toBeNull();
    });
  });

  describe('filter panel (WCAG)', () => {
    beforeEach(() => {
      create();
      flushAll();
    });

    it('groups options in fieldsets with legends', () => {
      const legends = Array.from(root().querySelectorAll('fieldset > legend')).map(l => l.textContent?.trim());
      expect(legends).toEqual(['Kategoria', 'Poziom', 'Typ umowy', 'Tryb pracy', 'Umiejętności', 'Wynagrodzenie']);
    });

    it('puts the facet count in the accessible name of an option', () => {
      expect(root().querySelector('input[aria-label="Junior, 12 ofert"]')).not.toBeNull();
      expect(root().querySelector('input[aria-label="Mid, 1 oferta"]')).not.toBeNull();
      expect(root().querySelector('input[aria-label="B2B, 7 ofert"]')).not.toBeNull();
      expect(root().querySelector('input[aria-label="Zdalna, 22 oferty"]')).not.toBeNull();
      // brak w odpowiedzi facet = 0 ofert
      expect(root().querySelector('input[aria-label="Senior, 0 ofert"]')).not.toBeNull();
      expect(root().querySelector('input[aria-label="Programowanie, 20 ofert"]')).not.toBeNull();
    });

    it('does not repeat the visible count for assistive technology', () => {
      const counts = root().querySelectorAll('.count');
      expect(counts.length).toBeGreaterThan(0);
      expect(Array.from(counts).every(c => c.getAttribute('aria-hidden') === 'true')).toBe(true);
    });

    it('collapses on narrow screens behind a toggle button that exposes its state', () => {
      const toggle = root().querySelector<HTMLButtonElement>('button.toggle')!;
      expect(toggle.getAttribute('aria-expanded')).toBe('false');
      expect(toggle.getAttribute('aria-controls')).toBe('filters-body');

      toggle.click();
      fixture.detectChanges();

      expect(toggle.getAttribute('aria-expanded')).toBe('true');
      expect(root().querySelector('#filters-body')?.classList.contains('open')).toBe(true);
    });

    it('offers popular skills from the facets and adds them to the filter', () => {
      clickByLabel('Angular, 9 ofert');

      expect(lastNavigation().query).toEqual({ skills: 'Angular' });
      flushAll();
    });
  });

  describe('facets', () => {
    it('labels options without a count until the facets are loaded', () => {
      create();
      http.match('/api/categories').forEach(r => r.flush(CATEGORIES));
      fixture.detectChanges();

      expect(root().querySelector('input[aria-label="Junior"]')).not.toBeNull();
      expect(root().querySelector('.count')).toBeNull();
      flushAll();
    });

    it('are reloaded when a filter changes but not when only the page changes', () => {
      create();
      flushAll();

      component.onPageChange({ pageIndex: 1, pageSize: 10, length: 35, previousPageIndex: 0 });
      flushAll();
      expect(facetRequests().length).toBe(0);

      clickByLabel('Junior, 12 ofert');
      const [facetReq] = facetRequests();
      expect(facetReq.request.params.get('seniority')).toBe('1');
      expect(facetReq.request.params.has('pageNumber')).toBe(false);
      facetReq.flush(FACETS);
      flushAll();
    });

    it('failure keeps the page working without counts being replaced', () => {
      create();
      http.match('/api/categories').forEach(r => r.flush(CATEGORIES));
      facetRequests().forEach(r => r.flush(null, { status: 500, statusText: 'Server Error' }));
      jobRequests().forEach(r => r.flush(jobs(10), { headers: { Pagination: pagination(35) } }));
      fixture.detectChanges();

      expect(root().querySelectorAll('.job-card').length).toBe(10);
      expect(root().querySelector('.load-error')).toBeNull();
      expect(root().querySelector('input[aria-label="Junior"]')).not.toBeNull();
    });
  });

  describe('request cancellation', () => {
    it('cancels the previous request when the filters change and ignores its late response', () => {
      create();
      flushAll();

      setUrl('seniority=1');
      const [first] = jobRequests();
      setUrl('seniority=2');
      const [second] = jobRequests();

      expect(first.cancelled).toBe(true);
      facetRequests().filter(r => !r.cancelled).forEach(r => r.flush(FACETS));
      respond(second, [makeJob(99, { title: 'Nowsza' })], 1);

      expect(root().querySelectorAll('.job-card').length).toBe(1);
      expect(root().querySelector('mat-card-title')?.textContent).toContain('Nowsza');
    });

    it('cancels pending facet requests when the filters change', () => {
      create();
      const [firstFacets] = facetRequests();
      setUrl('seniority=2');

      expect(firstFacets.cancelled).toBe(true);
      flushAll();
    });

    it('cancels in-flight requests when the component is destroyed', () => {
      create();
      const requests: TestRequest[] = [...jobRequests(), ...facetRequests(), ...http.match('/api/categories')];

      fixture.destroy();

      expect(requests.length).toBe(3);
      expect(requests.every(r => r.cancelled)).toBe(true);
    });
  });

  describe('load error state', () => {
    function failJobs(status: number): void {
      http.match('/api/categories').forEach(r => r.flush(CATEGORIES));
      facetRequests().forEach(r => r.flush(FACETS));
      jobRequests().forEach(r => r.flush(null, { status, statusText: 'Error' }));
      fixture.detectChanges();
    }

    it('shows an error message with a retry button instead of the list', () => {
      create();
      failJobs(500);

      expect(root().querySelector('.load-error h3')?.textContent).toContain('Nie udało się wczytać ofert');
      expect(root().querySelector('.load-error button')?.textContent).toContain('Spróbuj ponownie');
      expect(root().querySelector('.load-error')?.getAttribute('role')).toBe('alert');
      expect(root().querySelector('.no-results')).toBeNull();
      expect(root().querySelector('mat-paginator')).toBeNull();
    });

    it('shows a connection message when the server is unreachable', () => {
      create();
      failJobs(0);

      expect(root().querySelector('.load-error h3')?.textContent).toContain('Brak połączenia z serwerem');
    });

    it('retry reloads the same params and clears the error on success', () => {
      create('seniority=1');
      failJobs(500);

      root().querySelector<HTMLButtonElement>('.load-error button')!.click();
      const [retryReq] = jobRequests();
      expect(retryReq.request.params.get('seniority')).toBe('1');
      retryReq.flush(jobs(10), { headers: { Pagination: pagination(35) } });
      fixture.detectChanges();

      expect(root().querySelector('.load-error')).toBeNull();
      expect(root().querySelectorAll('.job-card').length).toBe(10);
    });
  });

  describe('paging', () => {
    beforeEach(() => {
      create();
      flushAll();
    });

    it('onPageChange writes a 1-based page and page size to the URL as a new history entry', () => {
      component.onPageChange({ pageIndex: 2, pageSize: 25, length: 35, previousPageIndex: 0 });

      expect(lastNavigation().query).toEqual({ page: '3', pageSize: '25' });
      expect(lastNavigation().replaceUrl).toBe(false);
      flushAll();
    });

    it('a filter change resets the page to the first one', () => {
      setUrl('page=3');
      flushAll();

      component.onFilterChange({ patch: { seniority: [SeniorityLevel.Mid] }, replace: false });

      expect(lastNavigation().query).toEqual({ seniority: '2' });
      flushAll();
    });

    it('redirects an out-of-range page to the last one without adding a history entry', () => {
      setUrl('page=9');
      fixture.detectChanges();
      jobRequests().forEach(r => r.flush([], { headers: { Pagination: pagination(35, 9) } }));

      expect(lastNavigation().query).toEqual({ page: '4' });
      expect(lastNavigation().replaceUrl).toBe(true);
      flushAll();
    });
  });

  describe('cards', () => {
    it('show category, level, contract types, salary with currency and period, and up to 5 skills with "+N"', () => {
      create();
      flushAll([
        makeJob(1, {
          categoryId: 1, categoryName: 'Programowanie', categorySlug: 'programowanie',
          seniority: SeniorityLevel.Senior, contractTypes: ContractTypes.EmploymentContract | ContractTypes.B2B,
          salaryMin: 15000, salaryMax: 20000, salaryCurrency: Currency.PLN, salaryPeriod: SalaryPeriod.Month,
          skills: ['Angular', 'TypeScript', 'RxJS', 'Jest', 'Docker', 'Git', 'SQL']
        }),
        makeJob(2, { salaryMin: 120, salaryMax: 150, salaryPeriod: SalaryPeriod.Hour })
      ], 2);

      const [first, second] = Array.from(root().querySelectorAll('.job-card'));
      const tags = Array.from(first.querySelectorAll('.tags li')).map(li => li.textContent?.trim());
      expect(tags).toEqual(['Programowanie', 'Senior', 'Umowa o pracę', 'B2B']);
      expect(first.querySelector('.salary-badge')?.textContent?.replace(/[\s  ]+/g, ' ').trim()).toBe('payments 15 000–20 000 PLN / mies.');
      const skills = Array.from(first.querySelectorAll('.skills li')).map(li => li.textContent?.trim());
      expect(skills).toEqual(['Angular', 'TypeScript', 'RxJS', 'Jest', 'Docker', '+2']);
      expect(second.querySelector('.salary-badge')?.textContent?.replace(/[\s  ]+/g, ' ')).toContain('120–150 PLN / h');
      expect(second.querySelector('.tags')).toBeNull();
      expect(second.querySelector('.skills')).toBeNull();
    });

    it('viewJobDetails navigates to the job page', () => {
      create();
      flushAll();
      navigate.mockResolvedValue(true);

      component.viewJobDetails(7);

      expect(navigate).toHaveBeenLastCalledWith(['/jobs', 7]);
    });
  });

  describe('expiration hint', () => {
    // Stała "teraz": środa 4 listopada 2026, 12:00 czasu lokalnego (tylko Date jest fałszowane)
    const NOW = new Date(2026, 10, 4, 12, 0, 0);
    const at = (days: number, hours = 18) => new Date(2026, 10, 4 + days, hours, 0, 0).toISOString();

    function createWithExpirations(expirations: (string | null)[]): void {
      vi.useFakeTimers({ toFake: ['Date'] });
      vi.setSystemTime(NOW);
      create();
      flushAll(expirations.map((expiresAt, i) => makeJob(i + 1, { expiresAt })), expirations.length);
    }

    function hints(): (string | undefined)[] {
      return Array.from(root().querySelectorAll('.job-card')).map(card => card.querySelector('.expires-soon')?.textContent?.trim());
    }

    it.each([
      [at(0), 'Wygasa dziś'],
      [at(1), 'Wygasa jutro'],
      [at(2), 'Wygasa za 2 dni'],
      [at(7), 'Wygasa za 7 dni']
    ])('shows "%s" -> %s', (expiresAt, expected) => {
      createWithExpirations([expiresAt]);

      expect(component.expiresSoon(component.jobs()[0])).toBe(expected);
      expect(hints()).toEqual([expected]);
    });

    it('shows the hint only on the cards that expire soon', () => {
      createWithExpirations([null, at(3), at(20), at(1)]);

      expect(hints()).toEqual([undefined, 'Wygasa za 3 dni', undefined, 'Wygasa jutro']);
    });

    it('shows nothing when the expiration moment has already passed today', () => {
      createWithExpirations([new Date(2026, 10, 4, 9, 0, 0).toISOString()]);

      expect(hints()).toEqual([undefined]);
    });
  });

  describe('company link', () => {
    beforeEach(() => {
      create();
      flushAll([makeJob(1, { company: 'Alfa', companyId: 7 }), makeJob(2, { company: 'Beta', companyId: 9 })], 2);
    });

    it('shows the company name on each card as a link to the company profile', () => {
      const links = Array.from(root().querySelectorAll<HTMLAnchorElement>('mat-card-subtitle a.company-link'));
      expect(links.map(l => l.textContent?.trim())).toEqual(['Alfa', 'Beta']);
      expect(links.map(l => l.getAttribute('href'))).toEqual(['/companies/7', '/companies/9']);
    });

    it('clicking the company link does not open the offer details', () => {
      const viewDetails = vi.spyOn(component, 'viewJobDetails');

      root().querySelector<HTMLAnchorElement>('a.company-link')!.click();

      expect(viewDetails).not.toHaveBeenCalled();
    });

    it('clicking elsewhere on the card still opens the offer details', () => {
      const viewDetails = vi.spyOn(component, 'viewJobDetails').mockImplementation(() => undefined);

      root().querySelector<HTMLElement>('mat-card-title')!.click();

      expect(viewDetails).toHaveBeenCalledWith(1);
    });
  });
});
