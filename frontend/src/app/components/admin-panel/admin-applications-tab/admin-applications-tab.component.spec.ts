import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, Subject, of, throwError } from 'rxjs';
import { AdminApplicationsTabComponent } from './admin-applications-tab.component';
import { JobApplicationService } from '../../../services/job-application.service';
import { JobApplication } from '../../../models/job-application.model';
import { ApplicationParams } from '../../../models/application-params.model';
import { PaginatedResult } from '../../../models/pagination.model';

function makeApplication(id: number): JobApplication {
  return {
    id,
    jobOfferId: 1,
    userId: 'u1',
    userEmail: 'jan@example.com',
    userFullName: 'Jan Kowalski',
    jobTitle: 'Programista',
    company: 'Acme',
    note: null,
    appliedAt: new Date('2026-01-01T10:00:00Z')
  };
}

function page(items: number, totalItems: number, currentPage = 1, pageSize = 10): PaginatedResult<JobApplication> {
  return {
    result: Array.from({ length: items }, (_, i) => makeApplication(i + 1)),
    pagination: {
      CurrentPage: currentPage,
      ItemsPerPage: pageSize,
      TotalItems: totalItems,
      TotalPages: Math.max(1, Math.ceil(totalItems / pageSize))
    }
  };
}

describe('AdminApplicationsTabComponent', () => {
  let fixture: ComponentFixture<AdminApplicationsTabComponent>;
  let component: AdminApplicationsTabComponent;
  let getAllApplications: ReturnType<typeof vi.fn>;
  let deleteApplication: ReturnType<typeof vi.fn>;
  let alertSpy: ReturnType<typeof vi.spyOn>;
  let confirmSpy: ReturnType<typeof vi.spyOn>;

  function lastParams(): ApplicationParams {
    return getAllApplications.mock.calls[getAllApplications.mock.calls.length - 1][0] as ApplicationParams;
  }

  function create(initial: Observable<PaginatedResult<JobApplication>> = of(page(10, 35))): void {
    getAllApplications.mockReturnValue(initial);
    fixture = TestBed.createComponent(AdminApplicationsTabComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    getAllApplications = vi.fn();
    deleteApplication = vi.fn().mockReturnValue(of(undefined));
    alertSpy = vi.spyOn(window, 'alert').mockImplementation(() => undefined);
    confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [AdminApplicationsTabComponent],
      providers: [{ provide: JobApplicationService, useValue: { getAllApplications, deleteApplication } }]
    }).compileComponents();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('requests the first page with default params and stores the result', () => {
    create();

    expect(getAllApplications).toHaveBeenCalledTimes(1);
    const params = lastParams();
    expect(params.pageNumber).toBe(1);
    expect(params.pageSize).toBe(10);
    expect(params.orderBy).toBe('newest');
    expect(params.searchTerm).toBe('');
    expect(params.jobOfferId).toBeNull();
    expect(component.applications().length).toBe(10);
    expect(component.totalItems()).toBe(35);
    expect(component.loading()).toBe(false);
  });

  describe('paging and sorting', () => {
    it('onPageChange sends pageNumber (1-based) and pageSize', () => {
      create();

      component.onPageChange({ pageIndex: 1, pageSize: 5, length: 35, previousPageIndex: 0 });

      expect(lastParams().pageNumber).toBe(2);
      expect(lastParams().pageSize).toBe(5);
    });

    it('clicking the paginator "next" button requests the next page', async () => {
      create();
      await fixture.whenStable();
      fixture.detectChanges();

      const next = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.mat-mdc-paginator-navigation-next');
      expect(next).not.toBeNull();
      next!.click();

      expect(lastParams().pageNumber).toBe(2);
    });

    it('sorting by appliedAt ascending sends orderBy=oldest and resets the page', () => {
      create();
      component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });

      component.onSortChange({ active: 'appliedAt', direction: 'asc' });

      expect(lastParams().orderBy).toBe('oldest');
      expect(lastParams().pageNumber).toBe(1);
    });

    it('sorting by appliedAt descending sends orderBy=newest', () => {
      create();
      component.onSortChange({ active: 'appliedAt', direction: 'asc' });

      component.onSortChange({ active: 'appliedAt', direction: 'desc' });

      expect(lastParams().orderBy).toBe('newest');
    });

    it('clicking the "Data aplikacji" header toggles the order', async () => {
      create();
      await fixture.whenStable();
      fixture.detectChanges();

      // Domyślnie malejąco (newest) – kliknięcie przełącza na rosnące
      const header = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('th.mat-sort-header'))
        .find(h => h.textContent?.includes('Data aplikacji'));
      expect(header).toBeDefined();
      header!.click();

      expect(lastParams().orderBy).toBe('oldest');
    });
  });

  describe('search', () => {
    it('is debounced, trimmed and resets the page', () => {
      vi.useFakeTimers();
      create();
      component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });
      const callsBefore = getAllApplications.mock.calls.length;

      component.onSearchInput('kow');
      component.onSearchInput(' kowalski ');
      vi.advanceTimersByTime(399);
      expect(getAllApplications.mock.calls.length).toBe(callsBefore);

      vi.advanceTimersByTime(1);

      expect(getAllApplications.mock.calls.length).toBe(callsBefore + 1);
      expect(lastParams().searchTerm).toBe('kowalski');
      expect(lastParams().pageNumber).toBe(1);
      expect(component.hasActiveFilters()).toBe(true);
    });

    it('clearSearch reloads immediately and cancels a pending debounce', () => {
      vi.useFakeTimers();
      create();
      component.onSearchInput('abc');
      vi.advanceTimersByTime(400);
      expect(lastParams().searchTerm).toBe('abc');

      component.onSearchInput('abcd');
      component.clearSearch();
      const callsAfterClear = getAllApplications.mock.calls.length;
      expect(lastParams().searchTerm).toBe('');

      vi.advanceTimersByTime(500);

      expect(getAllApplications.mock.calls.length).toBe(callsAfterClear);
    });

    it('clearFilters resets search, sort and page', () => {
      create();
      component.onSortChange({ active: 'appliedAt', direction: 'asc' });
      component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });

      component.clearFilters();

      expect(lastParams().orderBy).toBe('newest');
      expect(lastParams().searchTerm).toBe('');
      expect(lastParams().pageNumber).toBe(1);
      expect(component.hasActiveFilters()).toBe(false);
    });
  });

  describe('deleting', () => {
    it('does nothing when the user cancels the confirmation', () => {
      create();

      confirmSpy.mockReturnValue(false);
      component.deleteApplication(1);

      expect(deleteApplication).not.toHaveBeenCalled();
      expect(getAllApplications).toHaveBeenCalledTimes(1);
    });

    it('reloads the current page after deleting one of many items', () => {
      create();
      component.onPageChange({ pageIndex: 1, pageSize: 10, length: 35, previousPageIndex: 0 });

      component.deleteApplication(4);

      expect(deleteApplication).toHaveBeenCalledWith(4);
      expect(lastParams().pageNumber).toBe(2);
      expect(alertSpy).toHaveBeenCalledWith('Aplikacja została usunięta');
      expect(component.deletingId()).toBeNull();
    });

    it('steps back one page after deleting the last item on a page', () => {
      create(of(page(10, 11)));
      getAllApplications.mockReturnValue(of(page(1, 11, 2)));
      component.onPageChange({ pageIndex: 1, pageSize: 10, length: 11, previousPageIndex: 0 });
      expect(component.applications().length).toBe(1);
      getAllApplications.mockReturnValue(of(page(10, 10)));

      component.deleteApplication(1);

      expect(lastParams().pageNumber).toBe(1);
      expect(component.pageIndex()).toBe(0);
      expect(component.applications().length).toBe(10);
    });

    it('shows an error when deletion fails', () => {
      create();
      deleteApplication.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

      component.deleteApplication(1);

      expect(alertSpy).toHaveBeenCalledWith('Błąd podczas usuwania aplikacji');
      expect(component.deletingId()).toBeNull();
    });

    it.each([401, 403])('does not show its own alert when the interceptor handles %i', status => {
      create();
      deleteApplication.mockReturnValue(throwError(() => new HttpErrorResponse({ status })));

      component.deleteApplication(1);

      expect(alertSpy).not.toHaveBeenCalled();
    });
  });

  describe('load errors and out-of-range pages', () => {
    it('alerts on a load error and stops the loading indicator', () => {
      create(throwError(() => new HttpErrorResponse({ status: 500 })));

      expect(alertSpy).toHaveBeenCalledWith('Błąd podczas ładowania aplikacji');
      expect(component.loading()).toBe(false);
    });

    it('does not alert on 401 (handled by the interceptor)', () => {
      create(throwError(() => new HttpErrorResponse({ status: 401 })));

      expect(alertSpy).not.toHaveBeenCalled();
    });

    it('jumps to the last existing page when the requested page is out of range', () => {
      create();
      getAllApplications.mockReset();
      getAllApplications
        .mockReturnValueOnce(of(page(0, 11, 3)))
        .mockReturnValueOnce(of(page(1, 11, 2)));

      component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });

      expect(getAllApplications).toHaveBeenCalledTimes(2);
      expect(lastParams().pageNumber).toBe(2);
      expect(component.pageIndex()).toBe(1);
      expect(component.applications().length).toBe(1);
      expect(component.loading()).toBe(false);
    });

    it('ignores a stale response when a newer request was issued (switchMap)', () => {
      const first = new Subject<PaginatedResult<JobApplication>>();
      const second = new Subject<PaginatedResult<JobApplication>>();
      create(first);
      getAllApplications.mockReturnValue(second);

      component.reload();
      first.next(page(5, 5));
      expect(component.applications().length).toBe(0);

      second.next(page(2, 2));
      expect(component.applications().length).toBe(2);
    });
  });

  it('formatDate renders a Polish date', () => {
    create();
    const text = component.formatDate(new Date('2026-03-05T10:30:00Z'));
    expect(text).toContain('2026');
    expect(text.toLowerCase()).toContain('marca');
  });
});
