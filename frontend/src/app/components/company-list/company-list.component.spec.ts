import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';
import { CompanyListComponent, companiesNoun } from './company-list.component';
import { CompanyService } from '../../services/company.service';
import { Company } from '../../models/company.model';
import { CompanyParams } from '../../models/company-params.model';
import { PaginatedResult } from '../../models/pagination.model';
import { companyPage, makeCompany } from '../../../testing/company-fixtures';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('CompanyListComponent', () => {
  let fixture: ComponentFixture<CompanyListComponent>;
  let component: CompanyListComponent;
  let getCompanies: ReturnType<typeof vi.fn>;

  function lastParams(): CompanyParams {
    return getCompanies.mock.calls[getCompanies.mock.calls.length - 1][0] as CompanyParams;
  }

  function create(initial: Observable<PaginatedResult<Company>> = of(companyPage(12, 30, 1, 12))): void {
    getCompanies.mockReturnValue(initial);
    fixture = TestBed.createComponent(CompanyListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(async () => {
    getCompanies = vi.fn();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [CompanyListComponent],
      providers: [provideRouter([]), { provide: CompanyService, useValue: { getCompanies } }]
    }).compileComponents();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  describe('initial load', () => {
    it('requests the first page (12 per page, nameAsc) without a search term', () => {
      create();

      expect(getCompanies).toHaveBeenCalledTimes(1);
      const params = lastParams();
      expect(params.pageNumber).toBe(1);
      expect(params.pageSize).toBe(12);
      expect(params.orderBy).toBe('nameAsc');
      expect(params.searchTerm).toBe('');
      expect(component.companies().length).toBe(12);
      expect(component.totalItems()).toBe(30);
    });

    it('reserves the grid with skeleton cards while loading the first page', () => {
      create(new Subject<PaginatedResult<Company>>());

      expect(component.loading()).toBe(true);
      expect(root().querySelectorAll('.company-skeleton').length).toBe(6);
      expect(root().querySelector('.company-card')).toBeNull();
    });

    it('renders a card per company linking to its profile', () => {
      create(of({
        ...companyPage(0, 2),
        result: [makeCompany(4, { name: 'Alfa', location: 'Kraków' }), makeCompany(9, { name: 'Beta', location: null })]
      }));

      const cards = root().querySelectorAll<HTMLAnchorElement>('a.company-card');
      expect(cards.length).toBe(2);
      expect(cards[0].getAttribute('href')).toBe('/companies/4');
      expect(cards[1].getAttribute('href')).toBe('/companies/9');
      expect(cards[0].textContent).toContain('Alfa');
      expect(cards[0].textContent).toContain('Kraków');
      expect(cards[1].querySelector('.location')).toBeNull();
      expect(cards[0].querySelector('.name')?.textContent).toBe('Alfa');
    });

    it('announces the total count with the Polish plural in a status region', () => {
      create(of(companyPage(3, 3)));

      const info = root().querySelector('.results-info')!;
      expect(info.getAttribute('role')).toBe('status');
      expect(info.textContent?.trim()).toBe('Znaleziono 3 firmy');
    });

    it('shows cards without descriptions, borders or chips (name, city and offers only)', () => {
      create(of({ ...companyPage(0, 1), result: [makeCompany(1, { description: 'Długi opis firmy' })] }));

      expect(root().querySelector('.company-card')?.textContent).not.toContain('Długi opis firmy');
      expect(root().querySelector('mat-card')).toBeNull();
    });

    it.each([[1, 'firmę'], [3, 'firmy'], [5, 'firm'], [12, 'firm'], [22, 'firmy']])('companiesNoun(%i) = %s', (count, noun) => {
      expect(companiesNoun(count)).toBe(noun);
    });

    it('shows the empty state when there are no companies', () => {
      create(of(companyPage(0, 0)));

      expect(root().textContent).toContain('Brak firm');
      expect(root().querySelector('mat-paginator')).toBeNull();
    });
  });

  describe('active offers label (Polish plural)', () => {
    it.each([
      [0, '0 ofert'],
      [1, '1 oferta'],
      [2, '2 oferty'],
      [4, '4 oferty'],
      [5, '5 ofert'],
      [11, '11 ofert'],
      [12, '12 ofert'],
      [14, '14 ofert'],
      [21, '21 ofert'],
      [22, '22 oferty'],
      [112, '112 ofert'],
      [114, '114 ofert'],
      [122, '122 oferty']
    ])('%i -> "%s"', (count, expected) => {
      create();

      expect(component.jobsLabel(count)).toBe(expected);
    });

    it('is rendered on the cards', () => {
      create(of({ ...companyPage(0, 1), result: [makeCompany(1, { activeJobsCount: 3 })] }));

      expect(root().querySelector('.jobs-badge')?.textContent).toContain('3 oferty');
    });
  });

  describe('search', () => {
    it('is debounced by 400 ms, trimmed and resets the page', () => {
      vi.useFakeTimers();
      create();
      component.onPageChange({ pageIndex: 2, pageSize: 12, length: 30, previousPageIndex: 0 });
      const callsBefore = getCompanies.mock.calls.length;

      component.onSearchInput('te');
      component.onSearchInput('tech  ');
      vi.advanceTimersByTime(399);
      expect(getCompanies.mock.calls.length).toBe(callsBefore);

      vi.advanceTimersByTime(1);

      expect(getCompanies.mock.calls.length).toBe(callsBefore + 1);
      expect(lastParams().searchTerm).toBe('tech');
      expect(lastParams().pageNumber).toBe(1);
    });

    it('does not reload when the trimmed term did not change', () => {
      vi.useFakeTimers();
      create();
      const callsBefore = getCompanies.mock.calls.length;

      component.onSearchInput('   ');
      vi.advanceTimersByTime(500);

      expect(getCompanies.mock.calls.length).toBe(callsBefore);
    });

    it('typing into the input triggers a debounced request', () => {
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

    it('clearSearch reloads immediately without a term and cancels a pending debounce', () => {
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

    it('the clear button appears only when there is text and clears it', () => {
      create();
      expect(root().querySelector('button[aria-label="Wyczyść wyszukiwanie"]')).toBeNull();

      component.onSearchInput('abc');
      const clear = root().querySelector<HTMLButtonElement>('button[aria-label="Wyczyść wyszukiwanie"]')!;
      clear.click();

      expect(component.searchInput()).toBe('');
      expect(lastParams().searchTerm).toBe('');
    });
  });

  describe('sorting', () => {
    it('offers name A-Z, name Z-A and most offers', () => {
      create();

      expect(component.sortOptions.map(o => [o.value, o.label])).toEqual([
        ['nameAsc', 'Nazwa A–Z'],
        ['nameDesc', 'Nazwa Z–A'],
        ['jobsDesc', 'Najwięcej ofert']
      ]);
    });

    it.each(['nameDesc', 'jobsDesc'] as const)('sorting by %s sends orderBy and resets the page', orderBy => {
      create();
      component.onPageChange({ pageIndex: 2, pageSize: 12, length: 30, previousPageIndex: 0 });

      component.onOrderChange(orderBy);

      expect(lastParams().orderBy).toBe(orderBy);
      expect(lastParams().pageNumber).toBe(1);
      expect(component.pageIndex()).toBe(0);
    });

    it('selecting an option in the dropdown sends the new orderBy', async () => {
      create();
      await fixture.whenStable();
      fixture.detectChanges();

      root().querySelector<HTMLElement>('.mat-mdc-select-trigger')!.click();
      fixture.detectChanges();
      await fixture.whenStable();
      const options = Array.from(document.querySelectorAll<HTMLElement>('mat-option'));
      options.find(o => o.textContent?.includes('Najwięcej ofert'))!.click();
      fixture.detectChanges();

      expect(lastParams().orderBy).toBe('jobsDesc');
    });
  });

  describe('paging', () => {
    it('onPageChange sends pageNumber (1-based) and pageSize and scrolls to top', () => {
      create();

      component.onPageChange({ pageIndex: 1, pageSize: 24, length: 30, previousPageIndex: 0 });

      expect(lastParams().pageNumber).toBe(2);
      expect(lastParams().pageSize).toBe(24);
      expect(window.scrollTo).toHaveBeenCalledWith({ top: 0 });
    });

    it('clicking the paginator "next" button requests page 2', async () => {
      create();
      await fixture.whenStable();
      fixture.detectChanges();

      root().querySelector<HTMLButtonElement>('.mat-mdc-paginator-navigation-next')!.click();

      expect(lastParams().pageNumber).toBe(2);
    });

    it('jumps to the last existing page when the requested page is out of range', () => {
      create();
      getCompanies.mockReset();
      getCompanies
        .mockReturnValueOnce(of(companyPage(0, 13, 3, 12)))
        .mockReturnValueOnce(of(companyPage(1, 13, 2, 12)));

      component.onPageChange({ pageIndex: 2, pageSize: 12, length: 30, previousPageIndex: 0 });

      expect(getCompanies).toHaveBeenCalledTimes(2);
      expect(lastParams().pageNumber).toBe(2);
      expect(component.pageIndex()).toBe(1);
      expect(component.companies().length).toBe(1);
    });
  });

  describe('errors', () => {
    it('shows the error with a retry button, and retry reloads', () => {
      create(throwError(() => new HttpErrorResponse({ status: 500 })));

      expect(root().textContent).toContain('Nie udało się wczytać firm');
      expect(component.loading()).toBe(false);

      getCompanies.mockReturnValue(of(companyPage(2, 2)));
      Array.from(root().querySelectorAll<HTMLButtonElement>('button'))
        .find(b => b.textContent?.includes('Spróbuj ponownie'))!.click();

      expect(component.loadError()).toBeNull();
      expect(component.companies().length).toBe(2);
      expect(root().querySelectorAll('a.company-card').length).toBe(2);
    });

    it('shows a connection message when the server is unreachable', () => {
      create(throwError(() => new HttpErrorResponse({ status: 0 })));

      expect(component.loadError()).toBe('Brak połączenia z serwerem');
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
