import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Observable, Subject, of, throwError } from 'rxjs';
import { AdminCompaniesTabComponent } from './admin-companies-tab.component';
import { AdminService } from '../../../services/admin.service';
import { Company, CompanyStatus } from '../../../models/company.model';
import { AdminCompanyParams } from '../../../models/company-params.model';
import { PaginatedResult } from '../../../models/pagination.model';
import { CompanyDialogComponent } from '../company-dialog/company-dialog.component';
import { RejectCompanyDialogComponent } from '../reject-company-dialog/reject-company-dialog.component';
import { companyPage, makeCompany } from '../../../../testing/company-fixtures';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('AdminCompaniesTabComponent', () => {
  let fixture: ComponentFixture<AdminCompaniesTabComponent>;
  let component: AdminCompaniesTabComponent;
  let getCompanies: ReturnType<typeof vi.fn>;
  let approveCompany: ReturnType<typeof vi.fn>;
  let deleteCompany: ReturnType<typeof vi.fn>;
  let dialogOpen: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;
  let confirmSpy: ReturnType<typeof vi.spyOn>;

  function lastParams(): AdminCompanyParams {
    return getCompanies.mock.calls[getCompanies.mock.calls.length - 1][0] as AdminCompanyParams;
  }

  function create(initial: Observable<PaginatedResult<Company>> = of(companyPage(10, 35))): void {
    getCompanies.mockReturnValue(initial);
    fixture = TestBed.createComponent(AdminCompaniesTabComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function createWith(companies: Company[], totalItems = companies.length): void {
    create(of({ ...companyPage(0, totalItems), result: companies }));
  }

  function root(): HTMLElement {
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function httpError(status: number, error: unknown = null): HttpErrorResponse {
    return new HttpErrorResponse({ status, statusText: 'Error', error });
  }

  function row(index: number): HTMLElement {
    return root().querySelectorAll<HTMLElement>('tr.table-row')[index];
  }

  function actionButton(index: number, label: string): HTMLButtonElement | null {
    return row(index).querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`);
  }

  beforeEach(async () => {
    getCompanies = vi.fn();
    approveCompany = vi.fn().mockImplementation((id: number) => of(makeCompany(id, { status: CompanyStatus.Approved })));
    deleteCompany = vi.fn().mockReturnValue(of(undefined));
    dialogOpen = vi.fn().mockReturnValue({ afterClosed: () => of(undefined) });
    snackBarOpen = vi.fn();
    confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [AdminCompaniesTabComponent],
      providers: [
        provideRouter([]),
        { provide: AdminService, useValue: { getCompanies, approveCompany, deleteCompany } },
        { provide: MatDialog, useValue: { open: dialogOpen } },
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
      create(of(companyPage(10, 35)));

      expect(getCompanies).toHaveBeenCalledTimes(1);
      const params = lastParams();
      expect(params.pageNumber).toBe(1);
      expect(params.pageSize).toBe(10);
      expect(params.orderBy).toBe('newest');
      expect(params.searchTerm).toBe('');
      expect(params.status).toBeNull();
      expect(component.companies().length).toBe(10);
      expect(component.totalItems()).toBe(35);
      expect(component.loading()).toBe(false);
    });

    it('keeps loading=true until the response arrives', () => {
      const pending = new Subject<PaginatedResult<Company>>();
      create(pending);
      expect(component.loading()).toBe(true);
      expect(root().querySelector('mat-spinner')).not.toBeNull();

      pending.next(companyPage(3, 3));

      expect(component.loading()).toBe(false);
      expect(component.companies().length).toBe(3);
    });

    it('renders a row per company with name link, status chip, team size and active offers', () => {
      createWith([
        makeCompany(7, { name: 'Alfa', status: CompanyStatus.Pending, membersCount: 2, activeJobsCount: 5 }),
        makeCompany(8, { name: 'Beta', status: CompanyStatus.Approved })
      ]);

      expect(root().querySelectorAll('tr.table-row').length).toBe(2);
      const link = row(0).querySelector<HTMLAnchorElement>('a.company-name')!;
      expect(link.textContent).toContain('Alfa');
      expect(link.getAttribute('href')).toBe('/companies/7');
      expect(row(0).querySelector('app-status-chip')?.textContent?.trim()).toBe('Oczekuje');
      expect(row(1).querySelector('app-status-chip')?.textContent?.trim()).toBe('Zatwierdzona');
      expect(row(0).textContent).toContain('#7');
    });

    it('shows the rejection reason only for rejected companies', () => {
      createWith([
        makeCompany(1, { status: CompanyStatus.Rejected, rejectionReason: 'Niepełne dane' }),
        makeCompany(2, { status: CompanyStatus.Pending, rejectionReason: 'Stary powód' })
      ]);

      expect(row(0).querySelector('.rejection-reason')?.textContent).toContain('Niepełne dane');
      expect(row(1).querySelector('.rejection-reason')).toBeNull();
    });

    it('treats a missing status as pending', () => {
      create(of(companyPage(0, 0)));

      expect(component.statusPresentation(undefined).label).toBe('Oczekuje');
      expect(component.statusPresentation(null).label).toBe('Oczekuje');
    });

    it('shows the "no companies" empty state when nothing exists', () => {
      create(of(companyPage(0, 0)));

      expect(root().textContent).toContain('Brak firm');
      expect(component.hasActiveFilters()).toBe(false);
    });
  });

  describe('paging', () => {
    it('onPageChange sends pageNumber (1-based) and pageSize', () => {
      create();

      component.onPageChange({ pageIndex: 2, pageSize: 25, length: 35, previousPageIndex: 0 });

      expect(getCompanies).toHaveBeenCalledTimes(2);
      expect(lastParams().pageNumber).toBe(3);
      expect(lastParams().pageSize).toBe(25);
    });

    it('clicking the paginator "next" button requests the next page', async () => {
      create();
      await fixture.whenStable();
      fixture.detectChanges();

      const next = root().querySelector<HTMLButtonElement>('.mat-mdc-paginator-navigation-next');
      expect(next).not.toBeNull();
      next!.click();

      expect(lastParams().pageNumber).toBe(2);
      expect(component.pageIndex()).toBe(1);
    });

    it('jumps to the last existing page when the requested page is out of range', () => {
      create(of(companyPage(10, 35)));
      getCompanies.mockReset();
      getCompanies
        .mockReturnValueOnce(of(companyPage(0, 11, 3)))
        .mockReturnValueOnce(of(companyPage(1, 11, 2)));

      component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });

      expect(getCompanies).toHaveBeenCalledTimes(2);
      expect(lastParams().pageNumber).toBe(2);
      expect(component.pageIndex()).toBe(1);
      expect(component.companies().length).toBe(1);
      expect(component.loading()).toBe(false);
    });
  });

  describe('sorting', () => {
    it.each([
      ['name', 'asc', 'nameAsc'],
      ['name', 'desc', 'nameDesc'],
      ['createdAt', 'asc', 'oldest'],
      ['createdAt', 'desc', 'newest']
    ] as const)('sorting by %s %s sends orderBy=%s', (active, direction, expected) => {
      create();

      component.onSortChange({ active, direction });

      expect(lastParams().orderBy).toBe(expected);
    });

    it('falls back to "newest" for an empty direction or an unknown column', () => {
      create();

      component.onSortChange({ active: 'name', direction: '' });
      expect(lastParams().orderBy).toBe('newest');

      component.onSortChange({ active: 'status', direction: 'asc' });
      expect(lastParams().orderBy).toBe('newest');
    });

    it('resets to the first page when the sort changes', () => {
      create();
      component.onPageChange({ pageIndex: 3, pageSize: 10, length: 35, previousPageIndex: 0 });

      component.onSortChange({ active: 'name', direction: 'asc' });

      expect(lastParams().pageNumber).toBe(1);
      expect(component.pageIndex()).toBe(0);
    });

    it('clicking the "Firma" header sends nameAsc', async () => {
      create();
      await fixture.whenStable();
      fixture.detectChanges();

      const headers = Array.from(root().querySelectorAll<HTMLElement>('th.mat-sort-header'));
      headers.find(h => h.textContent?.includes('Firma'))!.click();

      expect(lastParams().orderBy).toBe('nameAsc');
      expect(lastParams().pageNumber).toBe(1);
    });
  });

  describe('filters', () => {
    it.each([
      [CompanyStatus.Pending],
      [CompanyStatus.Approved],
      [CompanyStatus.Rejected]
    ])('status filter %i is sent (including Pending=0) and resets the page', status => {
      create();
      component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });

      component.onStatusChange(status);

      expect(lastParams().status).toBe(status);
      expect(lastParams().pageNumber).toBe(1);
      expect(component.hasActiveFilters()).toBe(true);
    });

    it('selecting "Wszystkie" removes the status filter', () => {
      create();
      component.onStatusChange(CompanyStatus.Rejected);

      component.onStatusChange(null);

      expect(lastParams().status).toBeNull();
      expect(component.hasActiveFilters()).toBe(false);
    });

    it('offers "Wszystkie" plus the three statuses', () => {
      create();

      expect(component.statusOptions.map(o => o.label)).toEqual(['Wszystkie', 'Oczekuje', 'Zatwierdzona', 'Odrzucona']);
    });

    it('search is debounced by 400 ms, trimmed and resets the page', () => {
      vi.useFakeTimers();
      create();
      component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });
      const callsBefore = getCompanies.mock.calls.length;

      component.onSearchInput('te');
      component.onSearchInput('tech  ');
      vi.advanceTimersByTime(399);
      expect(getCompanies.mock.calls.length).toBe(callsBefore);

      vi.advanceTimersByTime(1);

      expect(getCompanies.mock.calls.length).toBe(callsBefore + 1);
      expect(lastParams().searchTerm).toBe('tech');
      expect(lastParams().pageNumber).toBe(1);
      expect(component.hasActiveFilters()).toBe(true);
    });

    it('does not reload when the trimmed search term did not change', () => {
      vi.useFakeTimers();
      create();
      const callsBefore = getCompanies.mock.calls.length;

      component.onSearchInput('   ');
      vi.advanceTimersByTime(500);

      expect(getCompanies.mock.calls.length).toBe(callsBefore);
    });

    it('clearSearch reloads immediately and cancels a pending debounce', () => {
      vi.useFakeTimers();
      create();
      component.onSearchInput('abc');
      vi.advanceTimersByTime(400);
      expect(lastParams().searchTerm).toBe('abc');

      component.onSearchInput('abcd');
      component.clearSearch();
      const callsAfterClear = getCompanies.mock.calls.length;
      expect(lastParams().searchTerm).toBe('');

      vi.advanceTimersByTime(500);

      expect(getCompanies.mock.calls.length).toBe(callsAfterClear);
      expect(component.searchInput()).toBe('');
    });

    it('typing into the search input triggers a debounced request', () => {
      vi.useFakeTimers();
      create();
      const input = root().querySelector<HTMLInputElement>('input[matInput]')!;
      const callsBefore = getCompanies.mock.calls.length;

      input.value = 'nowa';
      input.dispatchEvent(new Event('input'));
      vi.advanceTimersByTime(400);

      expect(getCompanies.mock.calls.length).toBe(callsBefore + 1);
      expect(lastParams().searchTerm).toBe('nowa');
    });

    it('clearFilters resets search, status, sort and page', () => {
      create();
      component.onStatusChange(CompanyStatus.Pending);
      component.onSortChange({ active: 'name', direction: 'asc' });
      component.onPageChange({ pageIndex: 2, pageSize: 10, length: 35, previousPageIndex: 0 });

      component.clearFilters();

      const params = lastParams();
      expect(params.status).toBeNull();
      expect(params.searchTerm).toBe('');
      expect(params.orderBy).toBe('newest');
      expect(params.pageNumber).toBe(1);
      expect(component.hasActiveFilters()).toBe(false);
    });

    it('shows the "no results" state (not "no companies") when filters match nothing', () => {
      create(of(companyPage(10, 35)));
      getCompanies.mockReturnValue(of(companyPage(0, 0)));

      component.onStatusChange(CompanyStatus.Rejected);

      const text = root().textContent ?? '';
      expect(text).toContain('Brak wyników');
      expect(text).not.toContain('Nie zarejestrowano jeszcze żadnej firmy');
    });
  });

  describe('approve', () => {
    const mixed = (): Company[] => [
      makeCompany(1, { status: CompanyStatus.Pending }),
      makeCompany(2, { status: CompanyStatus.Approved }),
      makeCompany(3, { status: CompanyStatus.Rejected })
    ];

    it('shows "Zatwierdź" for pending and rejected companies but not for approved ones', () => {
      createWith(mixed());

      expect(actionButton(0, 'Zatwierdź')).not.toBeNull();
      expect(actionButton(1, 'Zatwierdź')).toBeNull();
      expect(actionButton(2, 'Zatwierdź')).not.toBeNull();
    });

    it('shows "Odrzuć" for pending and approved companies but not for rejected ones', () => {
      createWith(mixed());

      expect(actionButton(0, 'Odrzuć')).not.toBeNull();
      expect(actionButton(1, 'Odrzuć')).not.toBeNull();
      expect(actionButton(2, 'Odrzuć')).toBeNull();
    });

    it('approves via the button, notifies and refreshes the list', () => {
      createWith([makeCompany(5, { name: 'Nowa', status: CompanyStatus.Pending })]);
      approveCompany.mockReturnValue(of(makeCompany(5, { name: 'Nowa', status: CompanyStatus.Approved })));
      const callsBefore = getCompanies.mock.calls.length;

      actionButton(0, 'Zatwierdź')!.click();

      expect(approveCompany).toHaveBeenCalledWith(5);
      expect(snackBarOpen).toHaveBeenCalledWith('Firma „Nowa” została zatwierdzona', 'OK', expect.anything());
      expect(getCompanies.mock.calls.length).toBe(callsBefore + 1);
      expect(component.busyId()).toBeNull();
    });

    it('marks the row busy while approving', () => {
      create();
      approveCompany.mockReturnValue(new Subject<Company>());

      component.approve(makeCompany(4));

      expect(component.busyId()).toBe(4);
    });

    it('reports an error and does not refresh when approving fails', () => {
      create();
      approveCompany.mockReturnValue(throwError(() => httpError(500)));
      const callsBefore = getCompanies.mock.calls.length;

      component.approve(makeCompany(4));

      expect(snackBarOpen).toHaveBeenCalledWith('Błąd podczas zatwierdzania firmy', 'OK', expect.anything());
      expect(component.busyId()).toBeNull();
      expect(getCompanies.mock.calls.length).toBe(callsBefore);
    });

    it.each([401, 403])('does not show its own message when the interceptor handles %i', status => {
      create();
      approveCompany.mockReturnValue(throwError(() => httpError(status)));

      component.approve(makeCompany(4));

      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(component.busyId()).toBeNull();
    });
  });

  describe('reject, add and edit dialogs', () => {
    it('opens the reject dialog with the company and refreshes after a rejection', () => {
      create();
      const company = makeCompany(9);
      dialogOpen.mockReturnValue({ afterClosed: () => of(makeCompany(9, { status: CompanyStatus.Rejected })) });
      const callsBefore = getCompanies.mock.calls.length;

      component.openRejectDialog(company);

      expect(dialogOpen).toHaveBeenCalledWith(RejectCompanyDialogComponent, expect.objectContaining({ data: company }));
      expect(getCompanies.mock.calls.length).toBe(callsBefore + 1);
    });

    it('does not refresh when the reject dialog is cancelled', () => {
      create();
      const callsBefore = getCompanies.mock.calls.length;

      component.openRejectDialog(makeCompany(9));

      expect(getCompanies.mock.calls.length).toBe(callsBefore);
    });

    it('the "Odrzuć" button opens the reject dialog', () => {
      createWith([makeCompany(5, { status: CompanyStatus.Pending })]);

      actionButton(0, 'Odrzuć')!.click();

      expect(dialogOpen).toHaveBeenCalledWith(
        RejectCompanyDialogComponent, expect.objectContaining({ data: expect.objectContaining({ id: 5 }) }));
    });

    it('"Dodaj firmę" opens the company dialog in add mode (null data) and refreshes after saving', () => {
      create();
      dialogOpen.mockReturnValue({ afterClosed: () => of(makeCompany(99)) });
      const callsBefore = getCompanies.mock.calls.length;

      Array.from(root().querySelectorAll<HTMLButtonElement>('button'))
        .find(b => b.textContent?.includes('Dodaj firmę'))!.click();

      expect(dialogOpen).toHaveBeenCalledWith(CompanyDialogComponent, expect.objectContaining({ data: null, disableClose: true }));
      expect(getCompanies.mock.calls.length).toBe(callsBefore + 1);
    });

    it('"Edytuj" opens the company dialog with the company', () => {
      createWith([makeCompany(5)]);

      actionButton(0, 'Edytuj')!.click();

      expect(dialogOpen).toHaveBeenCalledWith(
        CompanyDialogComponent, expect.objectContaining({ data: expect.objectContaining({ id: 5 }) }));
    });

    it('does not refresh when the company dialog is cancelled', () => {
      create();
      const callsBefore = getCompanies.mock.calls.length;

      component.openAddDialog();

      expect(getCompanies.mock.calls.length).toBe(callsBefore);
    });
  });

  describe('deleting', () => {
    it('does nothing when the user cancels the confirmation', () => {
      create();
      confirmSpy.mockReturnValue(false);

      component.deleteCompany(makeCompany(3));

      expect(deleteCompany).not.toHaveBeenCalled();
    });

    it('asks for confirmation with the company name', () => {
      create();

      component.deleteCompany(makeCompany(3, { name: 'Acme' }));

      expect(confirmSpy).toHaveBeenCalledWith('Czy na pewno chcesz usunąć firmę „Acme”?');
    });

    it('deletes, notifies and reloads the current page', () => {
      create(of(companyPage(10, 35)));
      component.onPageChange({ pageIndex: 1, pageSize: 10, length: 35, previousPageIndex: 0 });

      component.deleteCompany(makeCompany(3));

      expect(deleteCompany).toHaveBeenCalledWith(3);
      expect(lastParams().pageNumber).toBe(2);
      expect(snackBarOpen).toHaveBeenCalledWith('Firma została usunięta', 'OK', expect.anything());
      expect(component.busyId()).toBeNull();
    });

    it('steps back one page after deleting the last item on a page', () => {
      create(of(companyPage(10, 11)));
      getCompanies.mockReturnValue(of(companyPage(1, 11, 2)));
      component.onPageChange({ pageIndex: 1, pageSize: 10, length: 11, previousPageIndex: 0 });
      getCompanies.mockReturnValue(of(companyPage(10, 10)));

      component.deleteCompany(makeCompany(1));

      expect(lastParams().pageNumber).toBe(1);
      expect(component.pageIndex()).toBe(0);
    });

    it('translates the 400 "Company has job offers or members" and keeps the list', () => {
      create();
      deleteCompany.mockReturnValue(throwError(() => httpError(400, { message: 'Company has job offers or members' })));
      const callsBefore = getCompanies.mock.calls.length;

      component.deleteCompany(makeCompany(3));

      expect(snackBarOpen).toHaveBeenCalledWith(
        'Nie można usunąć firmy, która ma oferty lub członków zespołu', 'OK', expect.anything());
      expect(component.busyId()).toBeNull();
      expect(getCompanies.mock.calls.length).toBe(callsBefore);
    });

    it('shows a generic message for a server error', () => {
      create();
      deleteCompany.mockReturnValue(throwError(() => httpError(500)));

      component.deleteCompany(makeCompany(3));

      expect(snackBarOpen).toHaveBeenCalledWith('Błąd podczas usuwania firmy', 'OK', expect.anything());
    });

    it.each([401, 403])('does not show its own message when the interceptor handles %i', status => {
      create();
      deleteCompany.mockReturnValue(throwError(() => httpError(status)));

      component.deleteCompany(makeCompany(3));

      expect(snackBarOpen).not.toHaveBeenCalled();
    });

    it('the "Usuń" button deletes after confirmation', () => {
      createWith([makeCompany(5)]);

      actionButton(0, 'Usuń')!.click();

      expect(deleteCompany).toHaveBeenCalledWith(5);
    });
  });

  describe('load errors', () => {
    it('notifies on a load error and stops the loading indicator', () => {
      create(throwError(() => httpError(500)));

      expect(snackBarOpen).toHaveBeenCalledWith('Błąd podczas ładowania firm', 'OK', expect.anything());
      expect(component.loading()).toBe(false);
      expect(component.companies()).toEqual([]);
    });

    it('does not notify on 401/403 (handled by the interceptor)', () => {
      create(throwError(() => httpError(403)));

      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(component.loading()).toBe(false);
    });

    it('keeps the loop alive after a failed request', () => {
      create(throwError(() => httpError(500)));
      getCompanies.mockReturnValue(of(companyPage(2, 2)));

      component.reload();

      expect(component.companies().length).toBe(2);
    });

    it('ignores a stale response when a newer request was issued (switchMap)', () => {
      const first = new Subject<PaginatedResult<Company>>();
      const second = new Subject<PaginatedResult<Company>>();
      create(first);
      getCompanies.mockReturnValue(second);

      component.reload();
      first.next(companyPage(5, 5));
      expect(component.companies().length).toBe(0);

      second.next(companyPage(2, 2));
      expect(component.companies().length).toBe(2);
    });
  });
});
