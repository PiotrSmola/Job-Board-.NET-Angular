import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { By } from '@angular/platform-browser';
import { Observable, Subject, of, throwError } from 'rxjs';
import { AdminJobsTabComponent } from './admin-jobs-tab.component';
import { JobService } from '../../../services/job.service';
import { CompanyService } from '../../../services/company.service';
import { AdminService } from '../../../services/admin.service';
import { ConfirmService } from '../../../services/confirm.service';
import { Currency, JobOffer, JobOfferStatus, SalaryPeriod, WorkType } from '../../../models/job-offer.model';
import { ManageJobParams } from '../../../models/job-params.model';
import { PaginatedResult } from '../../../models/pagination.model';
import { Company, CompanyStatus } from '../../../models/company.model';
import { PanelMode } from '../../../models/panel-mode.model';
import { CompanyAutocompleteComponent } from '../../shared/company-autocomplete.component';
import { JobDialogComponent } from '../job-dialog/job-dialog.component';
import { makeCompany } from '../../../../testing/company-fixtures';

function makeJob(id: number): JobOffer {
  return {
    id,
    title: `Oferta ${id}`,
    company: 'Acme',
    companyId: 1,
    description: 'Opis',
    salaryMin: 1000,
    salaryMax: 2000,
    location: 'Rzeszów',
    workType: WorkType.Remote,
    createdAt: new Date('2026-01-01'),
    status: JobOfferStatus.Active,
    expiresAt: null,
    isExpired: false,
    categoryId: null,
    categoryName: null,
    categorySlug: null,
    seniority: null,
    contractTypes: 0,
    salaryCurrency: Currency.PLN,
    salaryPeriod: SalaryPeriod.Month,
    skills: []
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

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('AdminJobsTabComponent', () => {
  let fixture: ComponentFixture<AdminJobsTabComponent>;
  let component: AdminJobsTabComponent;
  let getManagedJobs: ReturnType<typeof vi.fn>;
  let deleteJob: ReturnType<typeof vi.fn>;
  let updateJobStatus: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;
  let confirmSpy: ReturnType<typeof vi.fn>;

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
    confirmSpy = vi.fn().mockReturnValue(of(true));
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [AdminJobsTabComponent],
      providers: [
        { provide: JobService, useValue: { getManagedJobs, deleteJob, updateJobStatus } },
        { provide: CompanyService, useValue: { getCompanies: vi.fn() } },
        { provide: AdminService, useValue: { getCompanies: vi.fn() } },
        { provide: MatDialog, useValue: { open: vi.fn() } },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } },
        { provide: ConfirmService, useValue: { confirm: confirmSpy } }
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
      expect(params.workTypes).toEqual([]);
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

      expect(lastParams().workTypes).toEqual([WorkType.Remote]);
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
      expect(params.workTypes).toEqual([]);
      expect(params.searchTerm).toBe('');
      expect(params.orderBy).toBe('newest');
      expect(params.pageNumber).toBe(1);
      expect(component.hasActiveFilters()).toBe(false);
    });
  });

  describe('deleting', () => {
    it('does nothing when the user cancels the confirmation', () => {
      create();

      confirmSpy.mockReturnValue(of(false));
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
      expect(snackBarOpen).toHaveBeenCalledWith('Oferta została usunięta', 'OK', expect.anything());
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

      expect(snackBarOpen).toHaveBeenCalledWith('Błąd podczas usuwania oferty', 'OK', expect.anything());
      expect(component.deletingId()).toBeNull();
      expect(getManagedJobs).toHaveBeenCalledTimes(1);
    });

    it('admin: warns that applications are deleted together with the offer', () => {
      create();

      component.deleteJob(1);

      expect(confirmSpy).toHaveBeenCalledWith(expect.objectContaining({ message: 'Czy na pewno chcesz usunąć tę ofertę? Wszystkie aplikacje na to stanowisko również zostaną usunięte.' }));
    });

    it('employer: explains that an offer with applications can only be closed', () => {
      getManagedJobs.mockReturnValue(of(page(10, 35)));
      fixture = TestBed.createComponent(AdminJobsTabComponent);
      fixture.componentRef.setInput('mode', 'employer');
      fixture.componentRef.setInput('company', makeCompany(1, { status: CompanyStatus.Approved }));
      component = fixture.componentInstance;
      fixture.detectChanges();

      component.deleteJob(1);

      expect(confirmSpy).toHaveBeenCalledWith(expect.objectContaining({ message: 'Czy na pewno chcesz usunąć tę ofertę? Ofertę, na którą wpłynęły aplikacje, można tylko zamknąć.' }));
      expect(deleteJob).toHaveBeenCalledWith(1);
    });

    it('translates the "has applications" rejection and keeps the page', () => {
      create();
      deleteJob.mockReturnValue(throwError(() => new HttpErrorResponse({
        status: 400, error: { message: 'Job offer has applications - close it instead' }
      })));

      component.deleteJob(1);

      expect(snackBarOpen).toHaveBeenCalledWith('Oferta ma aplikacje – zamiast usuwać, zamknij ją.', 'OK', expect.anything());
      expect(component.deletingId()).toBeNull();
      expect(getManagedJobs).toHaveBeenCalledTimes(1);
    });

    it.each([401, 403])('does not show its own alert when the interceptor handles %i', status => {
      create();
      deleteJob.mockReturnValue(throwError(() => new HttpErrorResponse({ status })));

      component.deleteJob(1);

      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(component.deletingId()).toBeNull();
    });
  });

  describe('load errors and out-of-range pages', () => {
    it('shows a load error in the view and stops the loading indicator', () => {
      create(throwError(() => new HttpErrorResponse({ status: 500 })));

      expect(component.loadError()).toBe('Błąd podczas ładowania ofert');
      expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')?.textContent).toContain('Błąd podczas ładowania ofert');
      expect(component.loading()).toBe(false);
      expect(component.jobs()).toEqual([]);
    });

    it('does not show its own error on 401 (handled by the interceptor)', () => {
      create(throwError(() => new HttpErrorResponse({ status: 401 })));

      expect(component.loadError()).toBeNull();
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

    describe('rendered table', () => {
      const STATES: Partial<JobOffer>[] = [
        { status: JobOfferStatus.Draft },
        { status: JobOfferStatus.Active, expiresAt: '2026-12-31T22:59:59Z' },
        { status: JobOfferStatus.Active, isExpired: true },
        { status: JobOfferStatus.Closed }
      ];

      async function render(overrides: Partial<JobOffer>[] = STATES): Promise<HTMLElement> {
        create(of({
          result: overrides.map((o, i) => ({ ...makeJob(i + 1), ...o })),
          pagination: { CurrentPage: 1, ItemsPerPage: 10, TotalItems: overrides.length, TotalPages: 1 }
        }));
        await fixture.whenStable();
        fixture.detectChanges();
        return fixture.nativeElement as HTMLElement;
      }

      it('shows a state chip per row and the expiration date when present', async () => {
        const root = await render();

        const chips = Array.from(root.querySelectorAll('td app-status-chip')).map(c => c.textContent?.trim());
        expect(chips).toEqual(['Szkic', 'Aktywna', 'Wygasła', 'Zamknięta']);
        expect(root.querySelectorAll('.expires').length).toBe(1);
        expect(root.querySelector('.expires')?.textContent).toContain('do ');
      });

      it('renders Opublikuj / Zamknij / Zamknij / Wznów as the first action of each row', async () => {
        const root = await render();

        const labels = Array.from(root.querySelectorAll('tr.table-row')).map(row =>
          row.querySelector('.actions-cell button')?.getAttribute('aria-label')?.split(':')[0]);
        expect(labels).toEqual(['Opublikuj', 'Zamknij', 'Zamknij', 'Wznów']);
      });

      it('clicking "Opublikuj" sends the Active status for that offer and refreshes the list', async () => {
        const root = await render();
        const callsBefore = getManagedJobs.mock.calls.length;

        root.querySelectorAll('tr.table-row')[0].querySelector<HTMLButtonElement>('button[aria-label^="Opublikuj:"]')!.click();

        expect(updateJobStatus).toHaveBeenCalledWith(1, JobOfferStatus.Active);
        expect(getManagedJobs.mock.calls.length).toBe(callsBefore + 1);
      });

      it('clicking "Zamknij" sends the Closed status', async () => {
        const root = await render();

        root.querySelectorAll('tr.table-row')[1].querySelector<HTMLButtonElement>('button[aria-label^="Zamknij:"]')!.click();

        expect(updateJobStatus).toHaveBeenCalledWith(2, JobOfferStatus.Closed);
      });

      it('clicking "Wznów" sends the Active status', async () => {
        const root = await render();

        root.querySelectorAll('tr.table-row')[3].querySelector<HTMLButtonElement>('button[aria-label^="Wznów:"]')!.click();

        expect(updateJobStatus).toHaveBeenCalledWith(4, JobOfferStatus.Active);
      });

      it('disables the quick action of the row being changed', async () => {
        const pending = new Subject<JobOffer>();
        updateJobStatus.mockReturnValue(pending);
        const root = await render();

        root.querySelectorAll('tr.table-row')[0].querySelector<HTMLButtonElement>('button[aria-label^="Opublikuj:"]')!.click();
        fixture.detectChanges();

        expect(component.statusChangingId()).toBe(1);
        expect(root.querySelectorAll('tr.table-row')[0].querySelector<HTMLButtonElement>('button[aria-label^="Opublikuj:"]')!.disabled).toBe(true);
        expect(root.querySelectorAll('tr.table-row')[1].querySelector<HTMLButtonElement>('button[aria-label^="Zamknij:"]')!.disabled).toBe(false);
      });

      it('keeps the list unchanged and shows the API message when publishing with a past date fails', async () => {
        updateJobStatus.mockReturnValue(throwError(() => new HttpErrorResponse({
          status: 400, error: { message: 'Expiration date must be in the future to publish' }
        })));
        const root = await render();
        const callsBefore = getManagedJobs.mock.calls.length;

        root.querySelectorAll('tr.table-row')[0].querySelector<HTMLButtonElement>('button[aria-label^="Opublikuj:"]')!.click();

        expect(snackBarOpen.mock.calls[0][0]).toBe('Aby opublikować ofertę, ustaw datę wygaśnięcia w przyszłości');
        expect(getManagedJobs.mock.calls.length).toBe(callsBefore);
      });

      it('selecting a state in the dropdown requests /manage with that state', async () => {
        const root = await render();

        // pierwsza lista wyboru: „Pokaż” (stan oferty)
        const triggers = root.querySelectorAll<HTMLElement>('.toolbar .mat-mdc-select-trigger');
        triggers[0].click();
        fixture.detectChanges();
        await fixture.whenStable();
        const option = Array.from(document.querySelectorAll<HTMLElement>('mat-option')).find(o => o.textContent?.trim() === 'Zamknięte');
        expect(option).toBeDefined();
        option!.click();
        fixture.detectChanges();

        expect(lastParams().state).toBe('closed');
      });
    });

    it.each([401, 403])('does not duplicate the interceptor notice when changing status fails with %i', status => {
      create();
      updateJobStatus.mockReturnValue(throwError(() => new HttpErrorResponse({ status })));

      component.changeStatus(makeJob(4), JobOfferStatus.Closed);

      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(snackBarOpen).not.toHaveBeenCalled();
    });
  });

  describe('panel mode', () => {
    let dialogOpen: ReturnType<typeof vi.fn>;

    function createInMode(mode: PanelMode, company: Company | null = null, jobs = [makeJob(1)]): void {
      getManagedJobs.mockReturnValue(of({ ...page(0, jobs.length), result: jobs }));
      dialogOpen = TestBed.inject(MatDialog).open as ReturnType<typeof vi.fn>;
      dialogOpen.mockReturnValue({ afterClosed: () => of(undefined) });
      fixture = TestBed.createComponent(AdminJobsTabComponent);
      fixture.componentRef.setInput('mode', mode);
      fixture.componentRef.setInput('company', company);
      component = fixture.componentInstance;
      fixture.detectChanges();
    }

    function root(): HTMLElement {
      fixture.detectChanges();
      return fixture.nativeElement as HTMLElement;
    }

    function headers(): string[] {
      return Array.from(root().querySelectorAll('th')).map(h => h.textContent?.trim() ?? '');
    }

    function rowActions(index: number): (string | null)[] {
      return Array.from(root().querySelectorAll('tr.table-row')[index].querySelectorAll('.actions-cell button'))
        .map(b => b.getAttribute('aria-label')?.split(':')[0] ?? null);
    }

    const jobsInEveryState = (): JobOffer[] => [
      { ...makeJob(1), status: JobOfferStatus.Draft },
      { ...makeJob(2), status: JobOfferStatus.Active },
      { ...makeJob(3), status: JobOfferStatus.Closed }
    ];

    describe('admin', () => {
      it('is the default mode: shows the company column and the company filter', () => {
        create();

        expect(component.isAdmin()).toBe(true);
        expect(component.displayedColumns()).toContain('company');
        expect(root().querySelector('app-company-autocomplete')).not.toBeNull();
      });

      it('renders the "Firma" header and the company name in each row', () => {
        createInMode('admin', null, [{ ...makeJob(1), company: 'Alfa' }]);

        expect(headers()).toContain('Firma');
        expect(root().querySelector('tr.table-row .company-name')?.textContent).toBe('Alfa');
      });

      it('may publish and resume offers regardless of any company', () => {
        createInMode('admin', null, jobsInEveryState());

        expect(component.canPublish()).toBe(true);
        expect(rowActions(0)).toContain('Opublikuj');
        expect(rowActions(2)).toContain('Wznów');
      });

      it('filtering by company sends companyId, resets the page and counts as an active filter', () => {
        createInMode('admin');
        component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });

        component.onCompanyFilterChange({ id: 7, name: 'Alfa' });

        expect(lastParams().companyId).toBe(7);
        expect(lastParams().pageNumber).toBe(1);
        expect(component.hasActiveFilters()).toBe(true);
      });

      it('does not reload when the same company is selected again', () => {
        createInMode('admin');
        component.onCompanyFilterChange({ id: 7, name: 'Alfa' });
        const callsBefore = getManagedJobs.mock.calls.length;

        component.onCompanyFilterChange({ id: 7, name: 'Alfa' });

        expect(getManagedJobs.mock.calls.length).toBe(callsBefore);
      });

      it('removing the company filter (null) reloads without companyId', () => {
        createInMode('admin');
        component.onCompanyFilterChange({ id: 7, name: 'Alfa' });

        component.onCompanyFilterChange(null);

        expect(lastParams().companyId).toBeNull();
        expect(component.hasActiveFilters()).toBe(false);
      });

      it('the autocomplete in the template drives the company filter', () => {
        createInMode('admin');
        const autocomplete = fixture.debugElement.query(By.directive(CompanyAutocompleteComponent));

        autocomplete.componentInstance.companyChange.emit({ id: 3, name: 'Gamma' });

        expect(lastParams().companyId).toBe(3);
      });

      it('clearFilters removes the company filter', () => {
        createInMode('admin');
        component.onCompanyFilterChange({ id: 7, name: 'Alfa' });

        component.clearFilters();

        expect(lastParams().companyId).toBeNull();
        expect(component.companyFilter()).toBeNull();
      });

      it('the "Firma" header sorts by company', async () => {
        createInMode('admin');
        await fixture.whenStable();
        fixture.detectChanges();

        const header = Array.from(root().querySelectorAll<HTMLElement>('th.mat-sort-header')).find(h => h.textContent?.includes('Firma'))!;
        header.click();

        expect(lastParams().orderBy).toBe('companyAsc');
      });

      it('opens the job dialog in admin mode without a company', () => {
        createInMode('admin');

        component.openAddDialog();

        expect(dialogOpen).toHaveBeenCalledWith(JobDialogComponent, expect.objectContaining({
          data: { job: null, mode: 'admin', company: null }
        }));
      });
    });

    describe('employer', () => {
      it('hides the company column and the company filter', () => {
        createInMode('employer', makeCompany(1, { status: CompanyStatus.Approved }));

        expect(component.isAdmin()).toBe(false);
        expect(component.displayedColumns()).not.toContain('company');
        expect(headers()).not.toContain('Firma');
        expect(root().querySelector('app-company-autocomplete')).toBeNull();
        expect(root().querySelector('.company-name')).toBeNull();
      });

      it('keeps all other columns', () => {
        createInMode('employer', makeCompany(1, { status: CompanyStatus.Approved }));

        expect(component.displayedColumns()).toEqual(['title', 'location', 'status', 'salary', 'createdAt', 'actions']);
      });

      it('never sends a company filter', () => {
        createInMode('employer', makeCompany(1, { status: CompanyStatus.Approved }));

        component.onSortChange({ active: 'title', direction: 'asc' });

        expect(lastParams().companyId).toBeNull();
      });

      it('approved company: offers Opublikuj / Zamknij / Wznów as an admin does', () => {
        createInMode('employer', makeCompany(1, { status: CompanyStatus.Approved }), jobsInEveryState());

        expect(component.canPublish()).toBe(true);
        expect(rowActions(0)[0]).toBe('Opublikuj');
        expect(rowActions(1)[0]).toBe('Zamknij');
        expect(rowActions(2)[0]).toBe('Wznów');
      });

      it.each([
        [CompanyStatus.Pending],
        [CompanyStatus.Rejected]
      ])('company with status %i: hides Opublikuj and Wznów, keeps Zamknij', status => {
        createInMode('employer', makeCompany(1, { status }), jobsInEveryState());

        expect(component.canPublish()).toBe(false);
        expect(rowActions(0)).not.toContain('Opublikuj');
        expect(rowActions(1)).toContain('Zamknij');
        expect(rowActions(2)).not.toContain('Wznów');
      });

      it('a company with an unknown status, or no company at all, cannot publish', () => {
        createInMode('employer', makeCompany(1, { status: null }), jobsInEveryState());
        expect(component.canPublish()).toBe(false);

        createInMode('employer', null, jobsInEveryState());
        expect(component.canPublish()).toBe(false);
        expect(rowActions(0)).not.toContain('Opublikuj');
      });

      it('still offers edit and delete for offers of a not approved company', () => {
        createInMode('employer', makeCompany(1, { status: CompanyStatus.Pending }), [{ ...makeJob(1), status: JobOfferStatus.Draft }]);

        expect(root().querySelectorAll('tr.table-row .actions-cell button').length).toBe(2);
      });

      it('quickActions filters out only the Active-targeting actions', () => {
        createInMode('employer', makeCompany(1, { status: CompanyStatus.Pending }));

        expect(component.quickActions({ ...makeJob(1), status: JobOfferStatus.Draft })).toEqual([]);
        expect(component.quickActions({ ...makeJob(2), status: JobOfferStatus.Closed })).toEqual([]);
        expect(component.quickActions({ ...makeJob(3), status: JobOfferStatus.Active }).map(a => a.label)).toEqual(['Zamknij']);
        expect(component.quickActions({ ...makeJob(4), status: JobOfferStatus.Active, isExpired: true }).map(a => a.label)).toEqual(['Zamknij']);
      });

      it('opens the job dialog in employer mode with the employer company', () => {
        const company = makeCompany(4, { status: CompanyStatus.Pending });
        createInMode('employer', company);

        component.openAddDialog();

        expect(dialogOpen).toHaveBeenCalledWith(JobDialogComponent, expect.objectContaining({
          data: { job: null, mode: 'employer', company }
        }));
      });

      it('passes the edited offer to the dialog and refreshes after saving', () => {
        const company = makeCompany(4, { status: CompanyStatus.Approved });
        createInMode('employer', company);
        const job = makeJob(9);
        dialogOpen.mockReturnValue({ afterClosed: () => of(true) });
        const callsBefore = getManagedJobs.mock.calls.length;

        component.openEditDialog(job);

        expect(dialogOpen).toHaveBeenCalledWith(JobDialogComponent, expect.objectContaining({ data: { job, mode: 'employer', company } }));
        expect(getManagedJobs.mock.calls.length).toBe(callsBefore + 1);
      });

      it('reacts to the company becoming approved (publish actions appear)', () => {
        createInMode('employer', makeCompany(1, { status: CompanyStatus.Pending }), [{ ...makeJob(1), status: JobOfferStatus.Draft }]);
        expect(rowActions(0)).not.toContain('Opublikuj');

        fixture.componentRef.setInput('company', makeCompany(1, { status: CompanyStatus.Approved }));

        expect(rowActions(0)).toContain('Opublikuj');
      });
    });
  });

  describe('formatting helpers', () => {
    beforeEach(() => create());

    it('salary uses the offer currency and period', () => {
      expect(component.salary(makeJob(1))).toContain('PLN');
    });

    it('metaLine joins seniority, contract types and work type', () => {
      expect(component.metaLine(makeJob(1))).toBe('Zdalna');
    });

    it('names the offer in the action labels and the table', () => {
      const root = fixture.nativeElement as HTMLElement;
      fixture.detectChanges();
      const labels = Array.from(root.querySelectorAll('tr.table-row')[0].querySelectorAll('.actions-cell button')).map(b => b.getAttribute('aria-label'));
      expect(labels).toEqual(['Zamknij: Oferta 1', 'Edytuj ofertę: Oferta 1', 'Usuń ofertę: Oferta 1']);
      expect(root.querySelector('table')?.getAttribute('aria-label')).toBe('Oferty pracy');
    });

    it('getWorkTypeLabel returns Polish labels', () => {
      expect(component.getWorkTypeLabel(WorkType.Remote)).toBe('Zdalna');
      expect(component.getWorkTypeLabel(WorkType.Hybrid)).toBe('Hybrydowa');
      expect(component.getWorkTypeLabel(WorkType.Onsite)).toBe('Stacjonarna');
      expect(component.getWorkTypeLabel(99 as WorkType)).toBe('Nieznany');
    });
  });
});
