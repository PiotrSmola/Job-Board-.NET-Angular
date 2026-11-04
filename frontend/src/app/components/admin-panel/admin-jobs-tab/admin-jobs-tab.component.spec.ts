import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Observable, Subject, of, throwError } from 'rxjs';
import { AdminJobsTabComponent } from './admin-jobs-tab.component';
import { JobService } from '../../../services/job.service';
import { JobOffer, JobOfferStatus, WorkType } from '../../../models/job-offer.model';
import { ManageJobParams } from '../../../models/job-params.model';
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
    createdAt: new Date('2026-01-01'),
    status: JobOfferStatus.Active,
    expiresAt: null,
    isExpired: false
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
  let getManagedJobs: ReturnType<typeof vi.fn>;
  let deleteJob: ReturnType<typeof vi.fn>;
  let updateJobStatus: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;
  let alertSpy: ReturnType<typeof vi.spyOn>;
  let confirmSpy: ReturnType<typeof vi.spyOn>;

  function lastParams(): ManageJobParams {
    return getManagedJobs.mock.calls[getManagedJobs.mock.calls.length - 1][0] as ManageJobParams;
  }

  function create(initial: Observable<PaginatedResult<JobOffer>> = of(page(10, 35))): void {
    getManagedJobs.mockReturnValue(initial);
    fixture = TestBed.createComponent(AdminJobsTabComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    getManagedJobs = vi.fn();
    deleteJob = vi.fn().mockReturnValue(of(undefined));
    updateJobStatus = vi.fn().mockImplementation((id: number, status: JobOfferStatus) => of({ ...makeJob(id), status }));
    snackBarOpen = vi.fn();
    alertSpy = vi.spyOn(window, 'alert').mockImplementation(() => undefined);
    confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [AdminJobsTabComponent],
      providers: [
        { provide: JobService, useValue: { getManagedJobs, deleteJob, updateJobStatus } },
        { provide: MatDialog, useValue: { open: vi.fn() } },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } }
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

      expect(getManagedJobs).toHaveBeenCalledTimes(1);
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

      expect(getManagedJobs).toHaveBeenCalledTimes(2);
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
      const callsBefore = getManagedJobs.mock.calls.length;

      component.onSearchInput('ang');
      component.onSearchInput('angular  ');
      vi.advanceTimersByTime(399);
      expect(getManagedJobs.mock.calls.length).toBe(callsBefore);

      vi.advanceTimersByTime(1);

      expect(getManagedJobs.mock.calls.length).toBe(callsBefore + 1);
      expect(lastParams().searchTerm).toBe('angular');
      expect(lastParams().pageNumber).toBe(1);
      expect(component.hasActiveFilters()).toBe(true);
    });

    it('does not reload when the trimmed search term did not change', () => {
      vi.useFakeTimers();
      create();
      const callsBefore = getManagedJobs.mock.calls.length;

      component.onSearchInput('   ');
      vi.advanceTimersByTime(500);

      expect(getManagedJobs.mock.calls.length).toBe(callsBefore);
    });

    it('clearSearch immediately reloads without a search term and cancels a pending debounce', () => {
      vi.useFakeTimers();
      create();
      component.onSearchInput('abc');
      vi.advanceTimersByTime(400);
      expect(lastParams().searchTerm).toBe('abc');

      component.onSearchInput('abcd'); // czeka w debounce
      component.clearSearch();
      const callsAfterClear = getManagedJobs.mock.calls.length;
      expect(lastParams().searchTerm).toBe('');

      vi.advanceTimersByTime(500);

      expect(getManagedJobs.mock.calls.length).toBe(callsAfterClear);
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
      expect(getManagedJobs).toHaveBeenCalledTimes(1);
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
      getManagedJobs.mockReturnValue(of(page(1, 11, 2)));
      component.onPageChange({ pageIndex: 1, pageSize: 10, length: 11, previousPageIndex: 0 });
      expect(component.jobs().length).toBe(1);
      getManagedJobs.mockReturnValue(of(page(10, 10)));

      component.deleteJob(1);

      expect(lastParams().pageNumber).toBe(1);
      expect(component.pageIndex()).toBe(0);
      expect(component.jobs().length).toBe(10);
    });

    it('stays on the first page when its only item is deleted', () => {
      create(of(page(1, 1)));
      getManagedJobs.mockReturnValue(of(page(0, 0)));

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
      expect(getManagedJobs).toHaveBeenCalledTimes(1);
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
      getManagedJobs.mockReturnValue(of(page(2, 2)));

      component.reload();

      expect(component.jobs().length).toBe(2);
    });

    it('jumps to the last existing page when the requested page is out of range', () => {
      create(of(page(10, 35)));
      getManagedJobs.mockReset();
      getManagedJobs
        .mockReturnValueOnce(of(page(0, 11, 3)))   // strona 3 nie istnieje (są 2 strony)
        .mockReturnValueOnce(of(page(1, 11, 2)));
      component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });

      expect(getManagedJobs).toHaveBeenCalledTimes(2);
      expect((getManagedJobs.mock.calls[0][0] as ManageJobParams).pageNumber).toBe(3);
      expect(lastParams().pageNumber).toBe(2);
      expect(component.pageIndex()).toBe(1);
      expect(component.jobs().length).toBe(1);
      expect(component.loading()).toBe(false);
    });

    it('shows the empty state for zero results without looping', () => {
      create(of(page(0, 0)));

      expect(getManagedJobs).toHaveBeenCalledTimes(1);
      expect(component.jobs()).toEqual([]);
      expect(component.totalItems()).toBe(0);
    });

    it('ignores a stale response when a newer request was issued (switchMap)', () => {
      const first = new Subject<PaginatedResult<JobOffer>>();
      const second = new Subject<PaginatedResult<JobOffer>>();
      create(first);
      getManagedJobs.mockReturnValue(second);

      component.reload();
      first.next(page(5, 5)); // spóźniona odpowiedź na pierwsze żądanie
      expect(component.jobs().length).toBe(0);

      second.next(page(2, 2));
      expect(component.jobs().length).toBe(2);
    });
  });

  describe('offer lifecycle', () => {
    it('sends the selected state filter and resets to the first page', () => {
      create();
      component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });

      component.onStateChange('expired');

      expect(lastParams().state).toBe('expired');
      expect(lastParams().pageNumber).toBe(1);
      expect(component.hasActiveFilters()).toBe(true);
    });

    it('clearFilters resets the state filter', () => {
      create();
      component.onStateChange('draft');

      component.clearFilters();

      expect(lastParams().state).toBeNull();
    });

    it.each([
      [{ status: JobOfferStatus.Draft, isExpired: false }, JobOfferStatus.Active, 'Opublikuj', 'Szkic'],
      [{ status: JobOfferStatus.Active, isExpired: false }, JobOfferStatus.Closed, 'Zamknij', 'Aktywna'],
      [{ status: JobOfferStatus.Active, isExpired: true }, JobOfferStatus.Closed, 'Zamknij', 'Wygasła'],
      [{ status: JobOfferStatus.Closed, isExpired: false }, JobOfferStatus.Active, 'Wznów', 'Zamknięta']
    ])('offers the right quick action for %o', (state, targetStatus, label, stateLabel) => {
      create();
      const job = { ...makeJob(1), ...state };

      expect(component.quickActions(job)).toEqual([expect.objectContaining({ status: targetStatus, label })]);
      expect(component.statePresentation(job).label).toBe(stateLabel);
    });

    it('changes the status, notifies and reloads the list', () => {
      create();
      const callsBefore = getManagedJobs.mock.calls.length;

      component.changeStatus(makeJob(4), JobOfferStatus.Closed);

      expect(updateJobStatus).toHaveBeenCalledWith(4, JobOfferStatus.Closed);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Status oferty „Oferta 4”: Zamknięta');
      expect(getManagedJobs.mock.calls.length).toBe(callsBefore + 1);
      expect(component.statusChangingId()).toBeNull();
    });

    it('shows the translated API message when publishing an offer with a past expiration date', () => {
      create();
      updateJobStatus.mockReturnValue(throwError(() => new HttpErrorResponse({
        status: 400, error: { message: 'Expiration date must be in the future to publish' }
      })));

      component.changeStatus(makeJob(4), JobOfferStatus.Active);

      expect(snackBarOpen.mock.calls[0][0]).toBe('Aby opublikować ofertę, ustaw datę wygaśnięcia w przyszłości');
      expect(component.statusChangingId()).toBeNull();
    });

    it.each([401, 403])('does not duplicate the interceptor notice when changing status fails with %i', status => {
      create();
      updateJobStatus.mockReturnValue(throwError(() => new HttpErrorResponse({ status })));

      component.changeStatus(makeJob(4), JobOfferStatus.Closed);

      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(alertSpy).not.toHaveBeenCalled();
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
