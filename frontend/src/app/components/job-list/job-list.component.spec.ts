import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter, Router } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { JobListComponent } from './job-list.component';
import { JobService } from '../../services/job.service';
import { JobOffer, JobOfferStatus, WorkType } from '../../models/job-offer.model';
import { JobParams } from '../../models/job-params.model';
import { PaginatedResult } from '../../models/pagination.model';

function makeJob(id: number): JobOffer {
  return {
    id, title: `Oferta ${id}`, company: 'Acme', companyId: 1, description: 'Opis', salaryMin: 1000, salaryMax: 2000,
    location: 'Rzeszów', workType: WorkType.Hybrid, createdAt: new Date('2026-01-01'),
    status: JobOfferStatus.Active, expiresAt: null, isExpired: false
  };
}

function page(items: number, totalItems: number): PaginatedResult<JobOffer> {
  return {
    result: Array.from({ length: items }, (_, i) => makeJob(i + 1)),
    pagination: { CurrentPage: 1, ItemsPerPage: 10, TotalItems: totalItems, TotalPages: Math.max(1, Math.ceil(totalItems / 10)) }
  };
}

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('JobListComponent', () => {
  let fixture: ComponentFixture<JobListComponent>;
  let component: JobListComponent;
  let getJobs: ReturnType<typeof vi.fn>;
  let alertSpy: ReturnType<typeof vi.spyOn>;
  let consoleLog: ReturnType<typeof vi.spyOn>;

  // JobParams jest mutowany przez komponent, więc zapamiętujemy kopię w chwili wywołania
  let calls: JobParams[];

  function lastParams(): JobParams {
    return calls[calls.length - 1];
  }

  function create(result: Observable<PaginatedResult<JobOffer>> = of(page(10, 35))): void {
    getJobs.mockImplementation((params: JobParams) => {
      calls.push(Object.assign(new JobParams(), params));
      return result;
    });
    fixture = TestBed.createComponent(JobListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    calls = [];
    getJobs = vi.fn();
    alertSpy = vi.spyOn(window, 'alert').mockImplementation(() => undefined);
    vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);
    consoleLog = vi.spyOn(console, 'log').mockImplementation(() => undefined);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [JobListComponent],
      providers: [provideRouter([]), { provide: JobService, useValue: { getJobs } }]
    }).compileComponents();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('loads the first page on init', () => {
    create();

    expect(calls.length).toBe(1);
    expect(lastParams().pageNumber).toBe(1);
    expect(lastParams().pageSize).toBe(10);
    expect(component.jobs.length).toBe(10);
    expect(component.totalItems).toBe(35);
    expect(component.loading).toBe(false);
  });

  it('does not write debug output to the console', () => {
    create();

    expect(consoleLog).not.toHaveBeenCalled();
  });

  it('stops loading and keeps working after a load error', () => {
    create(throwError(() => new Error('fail')));

    expect(component.loading).toBe(false);
    expect(component.jobs).toEqual([]);
  });

  describe('load error state', () => {
    function root(): HTMLElement {
      fixture.detectChanges();
      return fixture.nativeElement as HTMLElement;
    }

    it('shows an error message with a retry button instead of the empty list', () => {
      create(throwError(() => new HttpErrorResponse({ status: 500, url: '/api/jobs' })));

      expect(component.loadError).toBe('Nie udało się wczytać ofert');
      const el = root();
      expect(el.querySelector('.load-error h3')?.textContent).toContain('Nie udało się wczytać ofert');
      expect(el.querySelector('.load-error button')?.textContent).toContain('Spróbuj ponownie');
      expect(el.querySelector('.no-results')).toBeNull();
      expect(el.querySelector('.results-info')).toBeNull();
      expect(el.querySelector('mat-paginator')).toBeNull();
    });

    it('shows a connection message when the server is unreachable', () => {
      create(throwError(() => new HttpErrorResponse({ status: 0, url: '/api/jobs' })));

      expect(root().querySelector('.load-error h3')?.textContent).toContain('Brak połączenia z serwerem');
    });

    it('retry reloads the same params and clears the error on success', () => {
      create(throwError(() => new HttpErrorResponse({ status: 500, url: '/api/jobs' })));
      const before = calls.length;
      getJobs.mockImplementation((params: JobParams) => {
        calls.push(Object.assign(new JobParams(), params));
        return of(page(10, 35));
      });

      root().querySelector<HTMLButtonElement>('.load-error button')!.click();

      expect(calls.length).toBe(before + 1);
      expect(component.loadError).toBeNull();
      const el = root();
      expect(el.querySelector('.load-error')).toBeNull();
      expect(el.querySelectorAll('.job-card').length).toBe(10);
    });

    it('does not show the error block when loading succeeds', () => {
      create();

      expect(component.loadError).toBeNull();
      expect(root().querySelector('.load-error')).toBeNull();
    });
  });

  it('onPageChange sends a 1-based page number and page size', () => {
    create();

    component.onPageChange({ pageIndex: 2, pageSize: 25, length: 35, previousPageIndex: 0 });

    expect(lastParams().pageNumber).toBe(3);
    expect(lastParams().pageSize).toBe(25);
  });

  it('onFilterChange resets the page', () => {
    create();
    component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });

    component.jobParams.workType = WorkType.Remote;
    component.onFilterChange();

    expect(lastParams().pageNumber).toBe(1);
    expect(lastParams().workType).toBe(WorkType.Remote);
  });

  describe('search', () => {
    it('is debounced by 500 ms and resets the page', () => {
      vi.useFakeTimers();
      create();
      component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });
      const before = calls.length;

      component.onSearchTermChange('ang');
      component.onSearchTermChange('angular');
      vi.advanceTimersByTime(499);
      expect(calls.length).toBe(before);

      vi.advanceTimersByTime(1);

      expect(calls.length).toBe(before + 1);
      expect(lastParams().searchTerm).toBe('angular');
      expect(lastParams().pageNumber).toBe(1);
    });

    it('clearSearch reloads without a search term', () => {
      vi.useFakeTimers();
      create();
      const before = calls.length;
      component.jobParams.searchTerm = 'angular';

      component.clearSearch();
      vi.advanceTimersByTime(500);

      expect(calls.length).toBe(before + 1);
      expect(lastParams().searchTerm).toBe('');
      expect(lastParams().pageNumber).toBe(1);
    });

    it('typing in the search box and clicking the clear button sends both requests', () => {
      vi.useFakeTimers();
      create();
      const root = fixture.nativeElement as HTMLElement;
      const before = calls.length;

      const input = root.querySelector('input')!;
      input.value = 'angular';
      input.dispatchEvent(new Event('input'));
      vi.advanceTimersByTime(500);
      fixture.detectChanges();
      expect(lastParams().searchTerm).toBe('angular');

      root.querySelector<HTMLButtonElement>('.search-field button')!.click();
      fixture.detectChanges();
      vi.advanceTimersByTime(500);
      fixture.detectChanges();

      expect(calls.length).toBe(before + 2);
      expect(lastParams().searchTerm).toBe('');
    });

    it('applies the same search term again after clearFilters', () => {
      vi.useFakeTimers();
      create();
      component.onSearchTermChange('angular');
      vi.advanceTimersByTime(500);
      component.clearFilters();
      const before = calls.length;

      component.onSearchTermChange('angular');
      vi.advanceTimersByTime(500);

      expect(calls.length).toBe(before + 1);
      expect(lastParams().searchTerm).toBe('angular');
    });

    it('clearFilters drops a search term still waiting in the debounce', () => {
      vi.useFakeTimers();
      create();
      component.onSearchTermChange('angular');
      vi.advanceTimersByTime(200);

      component.clearFilters();
      const afterClear = calls.length;
      vi.advanceTimersByTime(1000);

      expect(calls.length).toBe(afterClear);
      expect(component.jobParams.searchTerm).toBe('');
    });

    it('clearSearch reloads immediately and ignores a term still waiting in the debounce', () => {
      vi.useFakeTimers();
      create();
      component.onSearchTermChange('angular');
      vi.advanceTimersByTime(500);
      component.onSearchTermChange('angularx');
      vi.advanceTimersByTime(200);
      const before = calls.length;

      component.clearSearch();
      expect(calls.length).toBe(before + 1);
      expect(lastParams().searchTerm).toBe('');

      vi.advanceTimersByTime(1000);
      expect(calls.length).toBe(before + 1);
    });

    it('does not reload when the debounced term equals the applied one', () => {
      vi.useFakeTimers();
      create();
      component.onSearchTermChange('angular');
      vi.advanceTimersByTime(500);
      const before = calls.length;

      component.onSearchTermChange('angularx');
      component.onSearchTermChange('angular');
      vi.advanceTimersByTime(500);

      expect(calls.length).toBe(before);
    });
  });

  describe('salary filter', () => {
    it('is debounced by 800 ms and applies min/max', () => {
      vi.useFakeTimers();
      create();
      const before = calls.length;

      component.jobParams.minSalary = 5000;
      component.jobParams.maxSalary = 9000;
      component.onSalaryChange();
      vi.advanceTimersByTime(799);
      expect(calls.length).toBe(before);

      vi.advanceTimersByTime(1);

      expect(calls.length).toBe(before + 1);
      expect(lastParams().minSalary).toBe(5000);
      expect(lastParams().maxSalary).toBe(9000);
      expect(lastParams().pageNumber).toBe(1);
    });

    it('rejects min greater than max with an alert and no request', () => {
      vi.useFakeTimers();
      create();
      const before = calls.length;

      component.jobParams.minSalary = 9000;
      component.jobParams.maxSalary = 5000;
      component.onSalaryChange();
      vi.advanceTimersByTime(800);

      expect(alertSpy).toHaveBeenCalledWith('Wynagrodzenie "Od" nie może być wyższe niż "Do"');
      expect(calls.length).toBe(before);
    });
  });

  it('clearFilters restores default params and reloads', () => {
    create();
    component.jobParams.workType = WorkType.Onsite;
    component.jobParams.location = 'Kraków';
    component.jobParams.pageNumber = 3;

    component.clearFilters();

    expect(lastParams().workType).toBeNull();
    expect(lastParams().location).toBe('');
    expect(lastParams().pageNumber).toBe(1);
  });

  it('viewJobDetails navigates to the job page', () => {
    create();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    component.viewJobDetails(7);

    expect(navigate).toHaveBeenCalledWith(['/jobs', 7]);
  });

  describe('expiration hint', () => {
    // Stała "teraz": środa 4 listopada 2026, 12:00 czasu lokalnego (tylko Date jest fałszowane)
    const NOW = new Date(2026, 10, 4, 12, 0, 0);
    const at = (days: number, hours = 18) => new Date(2026, 10, 4 + days, hours, 0, 0).toISOString();

    function createWithExpirations(expirations: (string | null)[]): void {
      vi.useFakeTimers({ toFake: ['Date'] });
      vi.setSystemTime(NOW);
      const result = page(expirations.length, expirations.length);
      result.result.forEach((job, i) => (job.expiresAt = expirations[i]));
      create(of(result));
    }

    function hints(): (string | undefined)[] {
      fixture.detectChanges();
      return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.job-card')).map(card =>
        card.querySelector('.expires-soon')?.textContent?.trim());
    }

    it.each([
      [at(0), 'Wygasa dziś'],
      [at(1), 'Wygasa jutro'],
      [at(2), 'Wygasa za 2 dni'],
      [at(7), 'Wygasa za 7 dni']
    ])('shows "%s" -> %s', (expiresAt, expected) => {
      createWithExpirations([expiresAt]);

      expect(component.expiresSoon(component.jobs[0])).toBe(expected);
      expect(hints()).toEqual([expected]);
    });

    it('shows nothing for open-ended offers and offers expiring in more than 7 days', () => {
      createWithExpirations([null, at(8), at(30)]);

      expect(hints()).toEqual([undefined, undefined, undefined]);
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

  it('formats salary and work type labels', () => {
    create();

    expect(component.formatSalary(null, null)).toBe('Nieokreślona');
    expect(component.formatSalary(1000, 2000)).toBe('1000 - 2000 PLN');
    expect(component.formatSalary(1000, null)).toBe('Od 1000 PLN');
    expect(component.formatSalary(null, 2000)).toBe('Do 2000 PLN');
    expect(component.getWorkTypeLabel(WorkType.Remote)).toBe('Zdalna');
    expect(component.getWorkTypeLabel(WorkType.Onsite)).toBe('Stacjonarna');
  });
});
