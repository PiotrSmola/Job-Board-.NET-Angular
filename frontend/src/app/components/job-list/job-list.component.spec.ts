import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, ParamMap, Router, UrlTree, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, of } from 'rxjs';
import { JobListComponent } from './job-list.component';
import { ContractTypes, Currency, JobOffer, SalaryPeriod, SeniorityLevel, WorkType } from '../../models/job-offer.model';
import { Category, JobFacets } from '../../models/taxonomy.model';
import { makeJob } from '../../../testing/job-fixtures';
import { SavedJobsService } from '../../services/saved-jobs.service';

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

const squash = (text: string | null | undefined) => (text ?? '').replace(/[\s  ]+/g, ' ').trim();

describe('JobListComponent', () => {
  let fixture: ComponentFixture<JobListComponent>;
  let component: JobListComponent;
  let http: HttpTestingController;
  let router: Router;
  let query$: BehaviorSubject<ParamMap>;
  let navigate: ReturnType<typeof vi.spyOn>;
  let savedJobsEnabled: boolean;
  let toggle: ReturnType<typeof vi.fn>;

  const root = () => fixture.nativeElement as HTMLElement;
  const jobRequests = () => http.match(r => r.url === '/api/jobs');
  const facetRequests = () => http.match(r => r.url === '/api/jobs/facets');
  const cards = () => Array.from(root().querySelectorAll<HTMLElement>('article.job-card'));
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

  function type(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  beforeEach(() => {
    savedJobsEnabled = false;
    toggle = vi.fn().mockReturnValue(of(true));
    vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    TestBed.configureTestingModule({
      imports: [JobListComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: SavedJobsService,
          useValue: { enabled: () => savedJobsEnabled, isSaved: (id: number) => id === 2, isPending: () => false, ensureLoaded: vi.fn(), toggle }
        }
      ]
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
      expect(root().querySelector('.jobs')?.getAttribute('aria-busy')).toBe('true');
      expect(cards().length).toBe(0);

      flushAll();

      expect(root().querySelectorAll('.job-skeleton').length).toBe(0);
      expect(cards().length).toBe(10);
      expect(root().querySelector('.jobs')?.getAttribute('aria-busy')).toBe('false');
    });

    it('keeps the current cards (dimmed, aria-busy) while the next results load instead of replacing them with skeletons', () => {
      create();
      flushAll();

      setUrl('seniority=1');
      fixture.detectChanges();

      expect(root().querySelectorAll('.job-skeleton').length).toBe(0);
      expect(cards().length).toBe(10);
      const list = root().querySelector('.jobs')!;
      expect(list.classList.contains('stale')).toBe(true);
      expect(list.getAttribute('aria-busy')).toBe('true');
      flushAll();
    });

    it('shows a message when there are no results', () => {
      create();
      flushAll([], 0);

      expect(root().querySelector('.no-results')?.textContent).toContain('Brak ofert');
      expect(root().querySelector('mat-paginator')).toBeNull();
    });
  });

  describe('headings and the result count', () => {
    it('has one h1, an h2 with the number of results and h3 offer titles', () => {
      create();
      flushAll(jobs(3), 35);

      expect(root().querySelectorAll('h1').length).toBe(1);
      expect(root().querySelector('h1')?.textContent).toBe('Oferty pracy');
      expect(squash(root().querySelector('#results-title')?.textContent)).toBe('35 ofert');
      expect(root().querySelectorAll('article.job-card h3').length).toBe(3);
    });

    it('announces the number of results in a polite live region', () => {
      create();
      const info = root().querySelector('.results-info')!;
      expect(info.getAttribute('role')).toBe('status');
      expect(info.getAttribute('aria-live')).toBe('polite');
      expect(info.textContent?.trim()).toBe('');

      flushAll(jobs(10), 35);

      expect(squash(info.textContent)).toBe('Znaleziono 35 ofert');
    });

    it.each([[1, 'Znaleziono 1 oferta'], [3, 'Znaleziono 3 oferty'], [12, 'Znaleziono 12 ofert']])(
      'uses the correct Polish plural for %s results', (total, expected) => {
        create();
        flushAll(jobs(1), total);

        expect(squash(root().querySelector('.results-info')?.textContent)).toBe(expected);
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

    it('reflects the URL filters in the panel and lists the active filters as plain text', () => {
      create('seniority=1&skills=Angular&category=devops-i-chmura');
      flushAll();

      expect(root().querySelector<HTMLInputElement>('input[aria-label^="Junior"]')!.checked).toBe(true);
      expect(root().querySelector<HTMLInputElement>('input[aria-label^="Mid"]')!.checked).toBe(false);
      expect(root().querySelector<HTMLInputElement>('input[type=radio][value="devops-i-chmura"]')!.checked).toBe(true);
      expect(squash(root().querySelector('.active-filters')?.textContent))
        .toBe('Aktywne filtry: Kategoria: DevOps i chmura, Junior, Angular');
      expect(root().querySelector('.results app-removable-chips')).toBeNull();
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

    it('choosing a category radio writes its slug, "Wszystkie" removes it', () => {
      clickByLabel('Programowanie, 20 ofert');
      expect(lastNavigation().query).toEqual({ category: 'programowanie' });
      flushAll();

      root().querySelector<HTMLInputElement>('input[type=radio][value=""]')!.click();
      expect(lastNavigation().query).toEqual({});
      flushAll();
    });

    it('typing in the search box is debounced by 500 ms and replaces the history entry', () => {
      vi.useFakeTimers();
      const input = root().querySelector<HTMLInputElement>('input[type=search]')!;
      const before = navigate.mock.calls.length;

      type(input, 'ang');
      type(input, 'angular');
      vi.advanceTimersByTime(499);
      expect(navigate.mock.calls.length).toBe(before);

      vi.advanceTimersByTime(1);

      expect(navigate.mock.calls.length).toBe(before + 1);
      expect(lastNavigation().query).toEqual({ searchTerm: 'angular' });
      expect(lastNavigation().replaceUrl).toBe(true);
      flushAll();
    });

    it('"Szukaj" applies the phrase and the location immediately as a new history entry', () => {
      vi.useFakeTimers();
      const [phrase, place] = Array.from(root().querySelectorAll<HTMLInputElement>('form[role=search] input'));
      type(phrase, 'rust');
      type(place, 'Kraków');

      root().querySelector<HTMLButtonElement>('form[role=search] button[type=submit]')!.click();

      expect(lastNavigation().query).toEqual({ searchTerm: 'rust', location: 'Kraków' });
      expect(lastNavigation().replaceUrl).toBe(false);
      fixture.detectChanges();
      const after = navigate.mock.calls.length;
      // wartości już zastosowane – debounce nie nawiguje drugi raz
      vi.advanceTimersByTime(600);
      expect(navigate.mock.calls.length).toBe(after);
      flushAll();
    });

    it('the search row is a labelled search landmark with labelled inputs', () => {
      const form = root().querySelector('form[role=search]')!;
      expect(form.getAttribute('aria-label')).toBe('Szukaj ofert');
      const labels = Array.from(form.querySelectorAll('input')).map(i => i.getAttribute('aria-label'));
      expect(labels).toEqual(['Stanowisko, firma lub technologia', 'Lokalizacja']);
    });

    it('salary typing is debounced by 800 ms and replaces the history entry', () => {
      vi.useFakeTimers();
      const [min] = Array.from(root().querySelectorAll<HTMLInputElement>('input[type=number]'));
      const before = navigate.mock.calls.length;

      type(min, '8000');
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

      type(min, '9000');
      type(max, '5000');
      vi.advanceTimersByTime(1000);
      fixture.detectChanges();

      expect(navigate.mock.calls.length).toBe(before);
      const alert = root().querySelector('#salary-error')!;
      expect(alert.getAttribute('role')).toBe('alert');
      expect(alert.textContent).toContain('nie może być wyższa');
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
      expect(cards().length).toBe(3);
    });

    it('overwrites the typed search text with the value from the URL after Back', () => {
      setUrl('searchTerm=angular');
      fixture.detectChanges();
      flushAll();
      const input = root().querySelector<HTMLInputElement>('input[type=search]')!;
      expect(input.value).toBe('angular');

      setUrl('');
      fixture.detectChanges();
      flushAll();

      expect(input.value).toBe('');
    });

    it('"Wyczyść filtry" drops the filters but keeps the sorting and moves focus to the results heading', () => {
      setUrl('seniority=1&skills=Angular&page=2&orderBy=oldest');
      flushAll();

      root().querySelector<HTMLButtonElement>('button.clear')!.click();

      expect(lastNavigation().query).toEqual({ orderBy: 'oldest' });
      expect(lastNavigation().replaceUrl).toBe(false);
      expect(document.activeElement).toBe(root().querySelector('#results-title'));
      flushAll();
    });

    it('shows "Wyczyść filtry" and the active filters only when a filter is set', () => {
      expect(root().querySelector('button.clear')).toBeNull();
      expect(root().querySelector('.active-filters')).toBeNull();

      setUrl('seniority=1');
      flushAll();

      expect(root().querySelector('button.clear')).not.toBeNull();
    });
  });

  describe('sorting', () => {
    it('is labelled next to the result count and writes the order to the URL', () => {
      create('seniority=1');
      flushAll();
      const select = root().querySelector('.results-header mat-select')!;
      expect(select.getAttribute('aria-labelledby')).toContain('sort-label');

      component.onSort('salaryHigh');

      expect(lastNavigation().query).toEqual({ seniority: '1', orderBy: 'salaryHigh' });
      flushAll();
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

    it('wraps every option in its label (the whole row is clickable)', () => {
      const inputs = Array.from(root().querySelectorAll<HTMLInputElement>('app-job-filters input[type=checkbox], app-job-filters input[type=radio]'));
      expect(inputs.length).toBeGreaterThan(0);
      expect(inputs.every(i => i.closest('label.check') !== null)).toBe(true);
    });

    it('collapses on narrow screens behind a toggle button that exposes its state', () => {
      const toggleButton = root().querySelector<HTMLButtonElement>('button.toggle')!;
      expect(toggleButton.getAttribute('aria-expanded')).toBe('false');
      expect(toggleButton.getAttribute('aria-controls')).toBe('filters-body');

      toggleButton.click();
      fixture.detectChanges();

      expect(toggleButton.getAttribute('aria-expanded')).toBe('true');
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

      expect(cards().length).toBe(10);
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

      expect(cards().length).toBe(1);
      expect(root().querySelector('.job-link')?.textContent).toContain('Nowsza');
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

      expect(root().querySelector('.load-error')?.textContent).toContain('Nie udało się wczytać ofert');
      expect(root().querySelector('.load-error button')?.textContent).toContain('Spróbuj ponownie');
      expect(root().querySelector('.load-error')?.getAttribute('role')).toBe('alert');
      expect(root().querySelector('.no-results')).toBeNull();
      expect(root().querySelector('mat-paginator')).toBeNull();
    });

    it('shows a connection message when the server is unreachable', () => {
      create();
      failJobs(0);

      expect(root().querySelector('.load-error')?.textContent).toContain('Brak połączenia z serwerem');
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
      expect(cards().length).toBe(10);
    });
  });

  describe('paging', () => {
    beforeEach(() => {
      create();
      flushAll();
    });

    it('onPageChange writes a 1-based page and page size to the URL and moves focus to the results heading', () => {
      component.onPageChange({ pageIndex: 2, pageSize: 20, length: 35, previousPageIndex: 0 });

      expect(lastNavigation().query).toEqual({ page: '3', pageSize: '20' });
      expect(lastNavigation().replaceUrl).toBe(false);
      expect(document.activeElement).toBe(root().querySelector('#results-title'));
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
    it('show company · city, the meta line, skills as text, the salary and the posting date', () => {
      create();
      flushAll([
        makeJob(1, {
          company: 'Alfa', location: 'Kraków',
          seniority: SeniorityLevel.Senior, contractTypes: ContractTypes.B2B, workType: WorkType.Remote,
          salaryMin: 15000, salaryMax: 20000, salaryCurrency: Currency.PLN, salaryPeriod: SalaryPeriod.Month,
          skills: ['Angular', 'TypeScript', 'RxJS', 'Jest', 'Docker', 'Git', 'SQL'], createdAt: new Date(2020, 0, 2)
        }),
        makeJob(2, { salaryMin: 120, salaryMax: 150, salaryPeriod: SalaryPeriod.Hour })
      ], 2);

      const [first, second] = cards();
      expect(squash(first.querySelector('.company')?.textContent)).toBe('Alfa · Kraków');
      expect(first.querySelector('.meta')?.textContent?.trim()).toBe('Senior · B2B · Zdalnie');
      expect(squash(first.querySelector('.skills')?.textContent)).toBe('Umiejętności: Angular, TypeScript, RxJS, Jest, Docker i 2 więcej');
      expect(squash(first.querySelector('.salary')?.textContent)).toBe('15 000–20 000 PLN / mies.');
      expect(squash(first.querySelector('.posted')?.textContent)).toBe('Dodano 2 stycznia 2020');
      expect(first.querySelector('time')?.getAttribute('datetime')).toBe(new Date(2020, 0, 2).toISOString());
      expect(squash(second.querySelector('.salary')?.textContent)).toContain('120–150 PLN / h');
      expect(second.querySelector('.skills')).toBeNull();
      expect(first.querySelector('ul')).toBeNull();
    });

    it('link the title to the offer and pass the list filters for "Wróć do wyników"', () => {
      create('seniority=1');
      flushAll(jobs(1), 1);
      const navigateByUrl = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

      const link = root().querySelector<HTMLAnchorElement>('h3 a.job-link')!;
      expect(link.getAttribute('href')).toBe('/jobs/1');
      link.click();

      expect(navigateByUrl).toHaveBeenCalledTimes(1);
      expect(navigateByUrl.mock.calls[0][1]).toMatchObject({ state: { jobsQuery: { seniority: '1' } } });
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
      return cards().map(card => card.querySelector('.expires-soon')?.textContent?.trim());
    }

    it.each([
      [at(0), 'Wygasa dziś'],
      [at(1), 'Wygasa jutro'],
      [at(2), 'Wygasa za 2 dni'],
      [at(7), 'Wygasa za 7 dni']
    ])('shows "%s" -> %s', (expiresAt, expected) => {
      createWithExpirations([expiresAt]);

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
      const links = Array.from(root().querySelectorAll<HTMLAnchorElement>('a.company-link'));
      expect(links.map(l => l.textContent?.trim())).toEqual(['Alfa', 'Beta']);
      expect(links.map(l => l.getAttribute('href'))).toEqual(['/companies/7', '/companies/9']);
    });

    it('clicking the company link opens only the company profile', () => {
      const navigateByUrl = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

      root().querySelector<HTMLAnchorElement>('a.company-link')!.click();

      expect(navigateByUrl).toHaveBeenCalledTimes(1);
      expect(router.serializeUrl(navigateByUrl.mock.calls[0][0] as UrlTree)).toBe('/companies/7');
    });
  });

  describe('saving offers', () => {
    it('shows a save toggle per card for a candidate, with the offer title in the label and the saved state', () => {
      savedJobsEnabled = true;
      create();
      flushAll(jobs(2), 2);

      const buttons = Array.from(root().querySelectorAll('app-save-job-button button'));
      expect(buttons.map(b => b.getAttribute('aria-label'))).toEqual(['Zapisz ofertę: Oferta 1', 'Usuń z zapisanych: Oferta 2']);
      expect(buttons.map(b => b.getAttribute('aria-pressed'))).toEqual(['false', 'true']);
    });

    it('toggling does not open the offer details', () => {
      savedJobsEnabled = true;
      create();
      flushAll(jobs(1), 1);
      const navigateByUrl = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

      root().querySelector<HTMLButtonElement>('app-save-job-button button')!.click();

      expect(toggle).toHaveBeenCalledWith(1);
      expect(navigateByUrl).not.toHaveBeenCalled();
    });

    it('shows no save toggles for visitors who cannot save offers', () => {
      create();
      flushAll(jobs(2), 2);
      expect(root().querySelector('app-save-job-button button')).toBeNull();
    });
  });
});
