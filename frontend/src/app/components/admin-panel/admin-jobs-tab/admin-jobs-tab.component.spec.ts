import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MatDialog } from '@angular/material/dialog';
import { Observable, Subject, of, throwError } from 'rxjs';
import { AdminJobsTabComponent } from './admin-jobs-tab.component';
import { JobService } from '../../../services/job.service';
import { JobOffer, WorkType } from '../../../models/job-offer.model';
import { JobParams } from '../../../models/job-params.model';
import { PaginatedResult } from '../../../models/pagination.model';

function makeJob(id: number): JobOffer {
  return {
    id,
    title: `Oferta ${id}`,
    company: 'Acme',
    description: 'Opis',
    salaryMin: 1000,
    salaryMax: 2000,
    location: 'Rzeszów',
    workType: WorkType.Remote,
    createdAt: new Date('2026-01-01')
  };
}

function page(items: number, totalItems: number, currentPage = 1, pageSize = 10): PaginatedResult<JobOffer> {
  return {
    result: Array.from({ length: items }, (_, i) => makeJob(i + 1)),
    pagination: {
      CurrentPage: currentPage,
      ItemsPerPage: pageSize,
      TotalItems: totalItems,
      TotalPages: Math.max(1, Math.ceil(totalItems / pageSize))
    }
  };
}

describe('AdminJobsTabComponent', () => {
  let fixture: ComponentFixture<AdminJobsTabComponent>;
  let component: AdminJobsTabComponent;
  let getJobs: ReturnType<typeof vi.fn>;
  let deleteJob: ReturnType<typeof vi.fn>;
  let alertSpy: ReturnType<typeof vi.spyOn>;
  let confirmSpy: ReturnType<typeof vi.spyOn>;

  function lastParams(): JobParams {
    return getJobs.mock.calls[getJobs.mock.calls.length - 1][0] as JobParams;
  }

  function create(initial: Observable<PaginatedResult<JobOffer>> = of(page(10, 35))): void {
    getJobs.mockReturnValue(initial);
    fixture = TestBed.createComponent(AdminJobsTabComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    getJobs = vi.fn();
    deleteJob = vi.fn().mockReturnValue(of(undefined));
    alertSpy = vi.spyOn(window, 'alert').mockImplementation(() => undefined);
    confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [AdminJobsTabComponent],
      providers: [
        { provide: JobService, useValue: { getJobs, deleteJob } },
        { provide: MatDialog, useValue: { open: vi.fn() } }
      ]
    }).compileComponents();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  describe('initial load', () => {
    it('requests the first page with default params and stores the result', () => {
      create(of(page(10, 35)));

      expect(getJobs).toHaveBeenCalledTimes(1);
      const params = lastParams();
      expect(params.pageNumber).toBe(1);
      expect(params.pageSize).toBe(10);
      expect(params.orderBy).toBe('newest');
      expect(params.searchTerm).toBe('');
      expect(params.workType).toBeNull();
      expect(component.jobs().length).toBe(10);
      expect(component.totalItems()).toBe(35);
      expect(component.loading()).toBe(false);
    });

    it('keeps loading=true until the response arrives', () => {
      const pending = new Subject<PaginatedResult<JobOffer>>();
      create(pending);
      expect(component.loading()).toBe(true);

      pending.next(page(3, 3));

      expect(component.loading()).toBe(false);
      expect(component.jobs().length).toBe(3);
    });
  });

  describe('paging', () => {
    it('onPageChange sends pageNumber (1-based) and pageSize', () => {
      create();

      component.onPageChange({ pageIndex: 2, pageSize: 25, length: 35, previousPageIndex: 0 });

      expect(getJobs).toHaveBeenCalledTimes(2);
      expect(lastParams().pageNumber).toBe(3);
      expect(lastParams().pageSize).toBe(25);
    });

    it('clicking the paginator "next" button requests the next page', async () => {
      create();
      await fixture.whenStable();
      fixture.detectChanges();

      const next = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.mat-mdc-paginator-navigation-next');
      expect(next).not.toBeNull();
      next!.click();

      expect(lastParams().pageNumber).toBe(2);
      expect(component.pageIndex()).toBe(1);
    });
  });

  describe('sorting', () => {
    it.each([
      ['title', 'asc', 'titleAsc'],
      ['title', 'desc', 'titleDesc'],
      ['company', 'asc', 'companyAsc'],
      ['company', 'desc', 'companyDesc'],
      ['location', 'asc', 'locationAsc'],
      ['location', 'desc', 'locationDesc'],
      ['salary', 'asc', 'salaryLow'],
      ['salary', 'desc', 'salaryHigh'],
      ['createdAt', 'asc', 'oldest'],
      ['createdAt', 'desc', 'newest']
    ] as const)('sorting by %s %s sends orderBy=%s', (active, direction, expected) => {
      create();

      component.onSortChange({ active, direction });

      expect(lastParams().orderBy).toBe(expected);
    });

    it('falls back to "newest" for an empty direction or an unknown column', () => {
      create();

      component.onSortChange({ active: 'title', direction: '' });
      expect(lastParams().orderBy).toBe('newest');

      component.onSortChange({ active: 'unknown', direction: 'asc' });
      expect(lastParams().orderBy).toBe('newest');
    });

    it('resets to the first page when the sort changes', () => {
      create();
      component.onPageChange({ pageIndex: 3, pageSize: 10, length: 35, previousPageIndex: 0 });
      expect(lastParams().pageNumber).toBe(4);

      component.onSortChange({ active: 'title', direction: 'asc' });

      expect(lastParams().pageNumber).toBe(1);
      expect(component.pageIndex()).toBe(0);
    });

    it('clicking a sortable header sends the matching orderBy', async () => {
      create();
      await fixture.whenStable();
      fixture.detectChanges();

      const headers = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('th.mat-sort-header'));
      const title = headers.find(h => h.textContent?.includes('Stanowisko'));
      expect(title).toBeDefined();
      title!.click();

      expect(lastParams().orderBy).toBe('titleAsc');
      expect(lastParams().pageNumber).toBe(1);
    });
  });

  describe('filters', () => {
    it('work type filter sends workType (including 0) and resets the page', () => {
      create();
      component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });

      component.onWorkTypeChange(WorkType.Remote);

      expect(lastParams().workType).toBe(WorkType.Remote);
      expect(lastParams().pageNumber).toBe(1);
      expect(component.hasActiveFilters()).toBe(true);
    });

    it('search is debounced, trimmed and resets the page', () => {
      vi.useFakeTimers();
      create();
      component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });
      const callsBefore = getJobs.mock.calls.length;

      component.onSearchInput('ang');
      component.onSearchInput('angular  ');
      vi.advanceTimersByTime(399);
      expect(getJobs.mock.calls.length).toBe(callsBefore);

      vi.advanceTimersByTime(1);

      expect(getJobs.mock.calls.length).toBe(callsBefore + 1);
      expect(lastParams().searchTerm).toBe('angular');
      expect(lastParams().pageNumber).toBe(1);
      expect(component.hasActiveFilters()).toBe(true);
    });

    it('does not reload when the trimmed search term did not change', () => {
      vi.useFakeTimers();
      create();
      const callsBefore = getJobs.mock.calls.length;

      component.onSearchInput('   ');
      vi.advanceTimersByTime(500);

      expect(getJobs.mock.calls.length).toBe(callsBefore);
    });

    it('clearSearch immediately reloads without a search term and cancels a pending debounce', () => {
      vi.useFakeTimers();
      create();
      component.onSearchInput('abc');
      vi.advanceTimersByTime(400);
      expect(lastParams().searchTerm).toBe('abc');

      component.onSearchInput('abcd'); // czeka w debounce
      component.clearSearch();
      const callsAfterClear = getJobs.mock.calls.length;
      expect(lastParams().searchTerm).toBe('');

      vi.advanceTimersByTime(500);

      expect(getJobs.mock.calls.length).toBe(callsAfterClear);
      expect(component.searchInput()).toBe('');
    });

    it('clearFilters resets search, work type, sort and page', () => {
      create();
      component.onWorkTypeChange(WorkType.Hybrid);
      component.onSortChange({ active: 'salary', direction: 'desc' });
      component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });

      component.clearFilters();

      const params = lastParams();
      expect(params.workType).toBeNull();
      expect(params.searchTerm).toBe('');
      expect(params.orderBy).toBe('newest');
      expect(params.pageNumber).toBe(1);
      expect(component.hasActiveFilters()).toBe(false);
    });
  });

  describe('deleting', () => {
    it('does nothing when the user cancels the confirmation', () => {
      create();

      confirmSpy.mockReturnValue(false);
      component.deleteJob(1);

      expect(deleteJob).not.toHaveBeenCalled();
      expect(getJobs).toHaveBeenCalledTimes(1);
    });

    it('reloads the current page after deleting one of many items', () => {
      create(of(page(10, 35)));
      component.onPageChange({ pageIndex: 1, pageSize: 10, length: 35, previousPageIndex: 0 });

      component.deleteJob(3);

      expect(deleteJob).toHaveBeenCalledWith(3);
      expect(lastParams().pageNumber).toBe(2);
      expect(component.pageIndex()).toBe(1);
      expect(alertSpy).toHaveBeenCalledWith('Oferta została usunięta');
      expect(component.deletingId()).toBeNull();
    });

    it('steps back one page after deleting the last item on a page', () => {
      create(of(page(10, 11)));
      getJobs.mockReturnValue(of(page(1, 11, 2)));
      component.onPageChange({ pageIndex: 1, pageSize: 10, length: 11, previousPageIndex: 0 });
      expect(component.jobs().length).toBe(1);
      getJobs.mockReturnValue(of(page(10, 10)));

      component.deleteJob(1);

      expect(lastParams().pageNumber).toBe(1);
      expect(component.pageIndex()).toBe(0);
      expect(component.jobs().length).toBe(10);
    });

    it('stays on the first page when its only item is deleted', () => {
      create(of(page(1, 1)));
      getJobs.mockReturnValue(of(page(0, 0)));

      component.deleteJob(1);

      expect(lastParams().pageNumber).toBe(1);
      expect(component.pageIndex()).toBe(0);
      expect(component.jobs().length).toBe(0);
    });

    it('shows an error and keeps the page when deletion fails', () => {
      create();
      deleteJob.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

      component.deleteJob(1);

      expect(alertSpy).toHaveBeenCalledWith('Błąd podczas usuwania oferty');
      expect(component.deletingId()).toBeNull();
      expect(getJobs).toHaveBeenCalledTimes(1);
    });

    it.each([401, 403])('does not show its own alert when the interceptor handles %i', status => {
      create();
      deleteJob.mockReturnValue(throwError(() => new HttpErrorResponse({ status })));

      component.deleteJob(1);

      expect(alertSpy).not.toHaveBeenCalled();
      expect(component.deletingId()).toBeNull();
    });
  });

  describe('load errors and out-of-range pages', () => {
    it('alerts on a load error and stops the loading indicator', () => {
      create(throwError(() => new HttpErrorResponse({ status: 500 })));

      expect(alertSpy).toHaveBeenCalledWith('Błąd podczas ładowania ofert');
      expect(component.loading()).toBe(false);
      expect(component.jobs()).toEqual([]);
    });

    it('does not alert on 401 (handled by the interceptor)', () => {
      create(throwError(() => new HttpErrorResponse({ status: 401 })));

      expect(alertSpy).not.toHaveBeenCalled();
      expect(component.loading()).toBe(false);
    });

    it('keeps the loop alive after a failed request', () => {
      create(throwError(() => new HttpErrorResponse({ status: 500 })));
      getJobs.mockReturnValue(of(page(2, 2)));

      component.reload();

      expect(component.jobs().length).toBe(2);
    });

    it('jumps to the last existing page when the requested page is out of range', () => {
      create(of(page(10, 35)));
      getJobs.mockReset();
      getJobs
        .mockReturnValueOnce(of(page(0, 11, 3)))   // strona 3 nie istnieje (są 2 strony)
        .mockReturnValueOnce(of(page(1, 11, 2)));
      component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });

      expect(getJobs).toHaveBeenCalledTimes(2);
      expect((getJobs.mock.calls[0][0] as JobParams).pageNumber).toBe(3);
      expect(lastParams().pageNumber).toBe(2);
      expect(component.pageIndex()).toBe(1);
      expect(component.jobs().length).toBe(1);
      expect(component.loading()).toBe(false);
    });

    it('shows the empty state for zero results without looping', () => {
      create(of(page(0, 0)));

      expect(getJobs).toHaveBeenCalledTimes(1);
      expect(component.jobs()).toEqual([]);
      expect(component.totalItems()).toBe(0);
    });

    it('ignores a stale response when a newer request was issued (switchMap)', () => {
      const first = new Subject<PaginatedResult<JobOffer>>();
      const second = new Subject<PaginatedResult<JobOffer>>();
      create(first);
      getJobs.mockReturnValue(second);

      component.reload();
      first.next(page(5, 5)); // spóźniona odpowiedź na pierwsze żądanie
      expect(component.jobs().length).toBe(0);

      second.next(page(2, 2));
      expect(component.jobs().length).toBe(2);
    });
  });

  describe('formatting helpers', () => {
    beforeEach(() => create());

    it('formatSalary covers all combinations', () => {
      expect(component.formatSalary(null, null)).toBe('Nieokreślona');
      expect(component.formatSalary(1000, 2000)).toBe('1000 - 2000 PLN');
      expect(component.formatSalary(1000, null)).toBe('Od 1000 PLN');
      expect(component.formatSalary(null, 2000)).toBe('Do 2000 PLN');
    });

    it('getWorkTypeLabel returns Polish labels', () => {
      expect(component.getWorkTypeLabel(WorkType.Remote)).toBe('Zdalna');
      expect(component.getWorkTypeLabel(WorkType.Hybrid)).toBe('Hybrydowa');
      expect(component.getWorkTypeLabel(WorkType.Onsite)).toBe('Stacjonarna');
      expect(component.getWorkTypeLabel(99 as WorkType)).toBe('Nieznany');
    });
  });
});
