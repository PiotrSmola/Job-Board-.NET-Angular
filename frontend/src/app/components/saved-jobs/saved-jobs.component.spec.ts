import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { SavedJobsComponent } from './saved-jobs.component';
import { SavedJobsService } from '../../services/saved-jobs.service';
import { SavedJob } from '../../models/account.model';
import { PaginatedResult } from '../../models/pagination.model';
import { JobOfferStatus } from '../../models/job-offer.model';
import { makeJob } from '../../../testing/job-fixtures';

vi.setConfig({ testTimeout: 20000 });

function page(items: SavedJob[], totalItems = items.length, currentPage = 1): PaginatedResult<SavedJob> {
  return {
    result: items,
    pagination: { CurrentPage: currentPage, ItemsPerPage: 10, TotalItems: totalItems, TotalPages: Math.ceil(totalItems / 10) }
  };
}

describe('SavedJobsComponent', () => {
  let fixture: ComponentFixture<SavedJobsComponent>;
  let getSavedJobs: ReturnType<typeof vi.fn>;
  let savedIds: ReturnType<typeof signal<ReadonlySet<number>>>;

  const ITEMS: SavedJob[] = [
    { savedAt: '2026-03-02T10:00:00Z', job: makeJob(1, { title: 'Angular Developer' }) },
    { savedAt: '2026-03-01T10:00:00Z', job: makeJob(2, { title: 'Stara oferta', isExpired: true }) },
    { savedAt: '2026-02-01T10:00:00Z', job: makeJob(3, { title: 'Zamknięta', status: JobOfferStatus.Closed }) }
  ];

  function create(result: unknown = of(page(ITEMS))): void {
    getSavedJobs.mockReturnValue(result);
    fixture = TestBed.createComponent(SavedJobsComponent);
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    getSavedJobs = vi.fn();
    savedIds = signal<ReadonlySet<number>>(new Set([1, 2, 3]));
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    TestBed.configureTestingModule({
      imports: [SavedJobsComponent],
      providers: [
        provideRouter([]),
        {
          provide: SavedJobsService,
          useValue: {
            getSavedJobs,
            enabled: () => true,
            isSaved: (id: number) => savedIds().has(id),
            isPending: () => false,
            ensureLoaded: vi.fn(),
            toggle: vi.fn()
          }
        }
      ]
    });
  });

  afterEach(() => vi.restoreAllMocks());

  it('loads the first page and lists the offers with links, saved date and a heart toggle', () => {
    create();

    expect(getSavedJobs).toHaveBeenCalledWith(1, 10);
    const titles = Array.from(root().querySelectorAll('.title a')).map(a => [a.textContent?.trim(), a.getAttribute('href')]);
    expect(titles).toEqual([['Angular Developer', '/jobs/1'], ['Stara oferta', '/jobs/2'], ['Zamknięta', '/jobs/3']]);
    expect(root().textContent).toContain('Zapisano: 02.03.2026');
    expect(root().querySelectorAll('app-save-job-button button[aria-pressed="true"]').length).toBe(3);
  });

  it('marks expired and closed offers with a text label (not only colour)', () => {
    create();
    const chips = Array.from(root().querySelectorAll('.item')).map(item => item.querySelector('app-status-chip')?.textContent?.trim() ?? null);

    expect(chips).toEqual([null, 'Wygasła', 'Zamknięta']);
  });

  it('keeps an offer removed from saved on the list with a note, so it can be restored and focus is not lost', () => {
    create();

    savedIds.set(new Set([2, 3]));
    fixture.detectChanges();

    const first = root().querySelector('.item')!;
    expect(first.classList).toContain('removed');
    expect(first.textContent).toContain('Usunięto z zapisanych');
    expect(first.querySelector('app-save-job-button button')?.getAttribute('aria-pressed')).toBe('false');
  });

  it('shows the empty state with a link to the offers', () => {
    create(of(page([])));

    expect(root().textContent).toContain('Nie masz jeszcze zapisanych ofert');
    expect(root().querySelector('a[href="/jobs"]')).not.toBeNull();
  });

  it('announces a loading error and retries', () => {
    create(throwError(() => new HttpErrorResponse({ status: 0 })));
    expect(root().querySelector('[role="alert"]')?.textContent).toContain('Brak połączenia z serwerem');

    getSavedJobs.mockReturnValue(of(page(ITEMS)));
    root().querySelector<HTMLButtonElement>('[role="alert"] button')!.click();
    fixture.detectChanges();

    expect(root().querySelectorAll('.item').length).toBe(3);
  });

  it('paginates and cancels the previous page request', () => {
    const first = new Subject<PaginatedResult<SavedJob>>();
    create(first);
    expect(root().querySelector('[aria-busy="true"]')).not.toBeNull();
    first.next(page(ITEMS, 25));
    fixture.detectChanges();

    const second = new Subject<PaginatedResult<SavedJob>>();
    getSavedJobs.mockReturnValue(second);
    fixture.componentInstance.onPageChange({ pageIndex: 1, pageSize: 10, length: 25 });
    expect(getSavedJobs).toHaveBeenLastCalledWith(2, 10);

    const third = new Subject<PaginatedResult<SavedJob>>();
    getSavedJobs.mockReturnValue(third);
    fixture.componentInstance.onPageChange({ pageIndex: 2, pageSize: 10, length: 25 });

    expect(second.observed).toBe(false);
    expect(third.observed).toBe(true);
  });

  it('shows the paginator only when there is more than one page', () => {
    create(of(page(ITEMS, 3)));
    expect(root().querySelector('mat-paginator')).toBeNull();

    create(of(page(ITEMS, 30)));
    expect(root().querySelector('mat-paginator')).not.toBeNull();
  });

  it('cancels the request when the page is left', () => {
    const pending = new Subject<PaginatedResult<SavedJob>>();
    create(pending);

    fixture.destroy();

    expect(pending.observed).toBe(false);
  });
});
