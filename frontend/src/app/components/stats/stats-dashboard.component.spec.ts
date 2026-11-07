import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Router, provideRouter } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';
import { StatsDashboardComponent } from './stats-dashboard.component';
import { StatsService } from '../../services/stats.service';
import { StatsMode } from '../../models/stats.model';
import { defaultRange, presetRange } from '../../utils/stats-range';
import { formatMedianHours } from '../../utils/stats-view';
import { adminStatsFixture, employerStatsFixture } from '../../../testing/stats-fixtures';

// Pierwszy test pliku kompiluje szablony komponentów Material – przy zimnym starcie bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

/** Obserwowalny, który zapamiętuje, czy ktoś się z niego wypisał (anulowanie żądania HTTP). */
function trackedRequest<T>(): { observable: Observable<T>; subject: Subject<T>; state: { subscribed: number; unsubscribed: number } } {
  const subject = new Subject<T>();
  const state = { subscribed: 0, unsubscribed: 0 };
  const observable = new Observable<T>(subscriber => {
    state.subscribed++;
    const inner = subject.subscribe(subscriber);
    return () => {
      state.unsubscribed++;
      inner.unsubscribe();
    };
  });
  return { observable, subject, state };
}

describe('StatsDashboardComponent', () => {
  let fixture: ComponentFixture<StatsDashboardComponent>;
  let router: Router;
  let getAdminStats: ReturnType<typeof vi.fn>;
  let getEmployerStats: ReturnType<typeof vi.fn>;
  let exportCsv: ReturnType<typeof vi.fn>;

  async function create(mode: StatsMode = 'admin', url = '/stats'): Promise<HTMLElement> {
    await router.navigateByUrl(url);
    fixture = TestBed.createComponent(StatsDashboardComponent);
    fixture.componentRef.setInput('mode', mode);
    await settle();
    return root();
  }

  async function settle(): Promise<void> {
    for (let i = 0; i < 3; i++) {
      fixture.detectChanges();
      await fixture.whenStable();
    }
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function text(selector: string): string {
    return root().querySelector(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  }

  function button(label: string): HTMLButtonElement {
    return Array.from(root().querySelectorAll<HTMLButtonElement>('button')).find(item => item.textContent?.includes(label)) as HTMLButtonElement;
  }

  beforeEach(async () => {
    getAdminStats = vi.fn().mockReturnValue(of(adminStatsFixture()));
    getEmployerStats = vi.fn().mockReturnValue(of(employerStatsFixture()));
    exportCsv = vi.fn().mockReturnValue(of(undefined));
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [StatsDashboardComponent],
      providers: [
        provideRouter([{ path: 'stats', children: [] }]),
        { provide: StatsService, useValue: { getAdminStats, getEmployerStats, exportCsv } }
      ]
    }).compileComponents();
    router = TestBed.inject(Router);
  });

  afterEach(() => vi.restoreAllMocks());

  describe('data', () => {
    it('loads admin stats for the default last-30-days range', async () => {
      await create('admin');

      expect(getAdminStats).toHaveBeenCalledTimes(1);
      expect(getAdminStats).toHaveBeenCalledWith(defaultRange());
      expect(getEmployerStats).not.toHaveBeenCalled();
    });

    it('loads employer stats in employer mode', async () => {
      await create('employer');

      expect(getEmployerStats).toHaveBeenCalledWith(defaultRange());
      expect(getAdminStats).not.toHaveBeenCalled();
      expect(text('app-stats-jobs-table h3')).toBe('Oferty firmy');
    });

    it('renders KPI tiles, the chart, the funnel and the jobs table', async () => {
      const el = await create('admin');

      const labels = Array.from(el.querySelectorAll('app-stats-kpi-tiles dt')).map(item => item.textContent?.trim());
      expect(labels).toContain('Użytkownicy');
      expect(labels).toContain('Mediana czasu pierwszej odpowiedzi');
      expect(el.querySelector('app-stats-line-chart svg[role="img"]')).not.toBeNull();
      expect(el.querySelector('app-stats-funnel [role="img"]')).not.toBeNull();
      expect(el.querySelectorAll('app-stats-jobs-table tbody tr').length).toBe(2);
      expect(text('.breakdown')).toContain('Złożona');
    });

    it('shows the median response time or "brak danych"', async () => {
      await create('admin');
      const median = Array.from(root().querySelectorAll('app-stats-kpi-tiles .tile')).find(tile => tile.textContent?.includes('Mediana'));
      expect(median?.querySelector('.value')?.textContent?.trim()).toBe(formatMedianHours(5.4));

      getAdminStats.mockReturnValue(of(adminStatsFixture({ medianFirstResponseHours: null })));
      fixture.componentInstance.reload();
      await settle();
      const empty = Array.from(root().querySelectorAll('app-stats-kpi-tiles .tile')).find(tile => tile.textContent?.includes('Mediana'));
      expect(empty?.querySelector('.value')?.textContent?.trim()).toBe('brak danych');
    });
  });

  describe('states', () => {
    it('shows a fixed-height skeleton and announces loading while the request is pending', async () => {
      const pending = trackedRequest<ReturnType<typeof adminStatsFixture>>();
      getAdminStats.mockReturnValue(pending.observable);

      const el = await create('admin');

      expect(el.querySelector('.skeleton')?.getAttribute('aria-hidden')).toBe('true');
      expect(el.querySelectorAll('.skeleton-block').length).toBeGreaterThan(3);
      expect(el.querySelector('app-stats-kpi-tiles')).toBeNull();
      expect(text('[role="status"].visually-hidden')).toBe('Wczytywanie statystyk…');

      pending.subject.next(adminStatsFixture());
      await settle();

      expect(el.querySelector('.skeleton')).toBeNull();
      expect(text('[role="status"].visually-hidden')).toContain('zostały wczytane');
    });

    it('shows an alert with a retry button on error and recovers after retrying', async () => {
      getAdminStats.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
      const el = await create('admin');

      expect(el.querySelector('.state-box[role="alert"]')?.textContent).toContain('Nie udało się wczytać statystyk');
      expect(el.querySelector('app-stats-line-chart')).toBeNull();

      getAdminStats.mockReturnValue(of(adminStatsFixture()));
      button('Spróbuj ponownie').click();
      await settle();

      expect(getAdminStats).toHaveBeenCalledTimes(2);
      expect(el.querySelector('.state-box[role="alert"]')).toBeNull();
      expect(el.querySelector('app-stats-line-chart')).not.toBeNull();
    });

    it('translates API messages and does not echo 403 reasons already shown by the interceptor', async () => {
      getAdminStats.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { message: 'Date range cannot exceed 366 days' } })));
      let el = await create('admin');
      expect(el.querySelector('.state-box')?.textContent).toContain('Zakres dat nie może przekraczać 366 dni');

      getAdminStats.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 403, error: { message: 'Forbidden details' } })));
      fixture.componentInstance.reload();
      await settle();
      el = root();
      expect(el.querySelector('.state-box')?.textContent).not.toContain('Forbidden details');
      expect(el.querySelector('.state-box')?.textContent).toContain('Nie udało się wczytać statystyk');
    });

    it('shows an empty state instead of charts when the range has no activity but keeps totals and the table', async () => {
      getEmployerStats.mockReturnValue(of(employerStatsFixture({
        series: [{ date: '2026-11-01', newApplications: 0, jobViews: 0 }],
        funnel: { submitted: 0, inReview: 0, interview: 0, offered: 0, hired: 0 }
      })));

      const el = await create('employer');

      expect(el.querySelector('.state-box.empty')?.textContent).toContain('Brak danych w wybranym zakresie');
      expect(el.querySelector('app-stats-line-chart')).toBeNull();
      expect(el.querySelector('app-stats-funnel')).toBeNull();
      expect(el.querySelector('app-stats-kpi-tiles')).not.toBeNull();
      expect(el.querySelector('app-stats-jobs-table')).not.toBeNull();
    });
  });

  describe('range', () => {
    it('takes the range from the query params', async () => {
      await create('admin', '/stats?from=2026-10-01&to=2026-10-31');

      expect(getAdminStats).toHaveBeenCalledWith({ from: '2026-10-01', to: '2026-10-31' });
    });

    it('falls back to the default range for invalid params', async () => {
      await create('admin', '/stats?from=2020-01-01&to=2026-10-31');

      expect(getAdminStats).toHaveBeenCalledWith(defaultRange());
    });

    it('writes the chosen range to the URL (replacing history) and reloads', async () => {
      await create('admin');
      const navigate = vi.spyOn(router, 'navigate');

      button('7 dni').click();
      await settle();

      const expected = presetRange('7d');
      expect(navigate).toHaveBeenCalledWith([], expect.objectContaining({ replaceUrl: true, queryParamsHandling: 'merge' }));
      expect(router.url).toContain(`from=${expected.from}`);
      expect(router.url).toContain(`to=${expected.to}`);
      expect(getAdminStats).toHaveBeenLastCalledWith(expected);
    });

    it('does not navigate or reload when the same range is chosen again', async () => {
      await create('admin');

      button('30 dni').click();
      await settle();

      expect(getAdminStats).toHaveBeenCalledTimes(1);
    });

    it('follows the URL when it changes elsewhere (browser back)', async () => {
      await create('admin');

      await router.navigateByUrl('/stats?from=2026-09-01&to=2026-09-30');
      await settle();

      expect(getAdminStats).toHaveBeenLastCalledWith({ from: '2026-09-01', to: '2026-09-30' });
    });

    it('cancels the in-flight request when the range changes (switchMap)', async () => {
      const first = trackedRequest<ReturnType<typeof adminStatsFixture>>();
      const second = trackedRequest<ReturnType<typeof adminStatsFixture>>();
      getAdminStats.mockReturnValueOnce(first.observable).mockReturnValueOnce(second.observable);
      await create('admin');
      expect(first.state).toEqual({ subscribed: 1, unsubscribed: 0 });

      await router.navigateByUrl('/stats?from=2026-09-01&to=2026-09-30');
      await settle();

      expect(first.state.unsubscribed).toBe(1);
      expect(second.state.subscribed).toBe(1);

      // spóźniona odpowiedź na anulowane żądanie nie nadpisuje stanu
      first.subject.next(adminStatsFixture({ medianFirstResponseHours: 99 }));
      second.subject.next(adminStatsFixture());
      await settle();
      expect(text('app-stats-kpi-tiles')).not.toContain('99');
    });

    it('cancels the pending request when retrying', async () => {
      const first = trackedRequest<ReturnType<typeof adminStatsFixture>>();
      getAdminStats.mockReturnValueOnce(first.observable).mockReturnValue(of(adminStatsFixture()));
      await create('admin');

      fixture.componentInstance.reload();
      await settle();

      expect(first.state.unsubscribed).toBe(1);
    });
  });

  describe('export', () => {
    it('exports the CSV for the current mode and range', async () => {
      await create('employer', '/stats?from=2026-10-01&to=2026-10-31');

      button('Eksportuj CSV').click();
      await settle();

      expect(exportCsv).toHaveBeenCalledWith('employer', { from: '2026-10-01', to: '2026-10-31' });
    });

    it('blocks double clicks while exporting and keeps the button focusable', async () => {
      const pending = trackedRequest<void>();
      exportCsv.mockReturnValue(pending.observable);
      await create('admin');

      button('Eksportuj CSV').click();
      await settle();
      const exporting = button('Eksportowanie');
      expect(exporting.getAttribute('aria-disabled')).toBe('true');
      expect(exporting.hasAttribute('disabled')).toBe(false);
      exporting.click();
      expect(exportCsv).toHaveBeenCalledTimes(1);

      pending.subject.next();
      pending.subject.complete();
      await settle();
      expect(button('Eksportuj CSV').getAttribute('aria-disabled')).not.toBe('true');
    });

    it('announces an export error', async () => {
      exportCsv.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { message: 'Invalid date range' } })));
      const el = await create('admin');

      button('Eksportuj CSV').click();
      await settle();

      expect(el.querySelector('.export-error[role="alert"]')?.textContent).toContain('Nieprawidłowy zakres dat');
      expect(button('Eksportuj CSV').getAttribute('aria-disabled')).not.toBe('true');
    });

    it('stays silent for 401/403, which the interceptor already reported', async () => {
      exportCsv.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 403 })));
      const el = await create('admin');

      button('Eksportuj CSV').click();
      await settle();

      expect(el.querySelector('.export-error')).toBeNull();
    });
  });

  describe('accessibility', () => {
    it('exposes the controls with names and a labelled region', async () => {
      const el = await create('admin');

      expect(el.querySelector('section')?.getAttribute('aria-label')).toBe('Statystyki');
      expect(button('Odśwież').getAttribute('aria-label')).toBe('Odśwież statystyki');
      el.querySelectorAll('mat-icon').forEach(icon => expect(icon.getAttribute('aria-hidden')).toBe('true'));
    });

    it('uses OnPush change detection', () => {
      const definition = (StatsDashboardComponent as unknown as { ɵcmp: { onPush: boolean } }).ɵcmp;
      expect(definition.onPush).toBe(true);
    });
  });

  describe('cleanup', () => {
    it('unsubscribes from the pending data request on destroy and leaves no timers of its own', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'setInterval', 'requestAnimationFrame', 'cancelAnimationFrame'] });
      try {
        const pending = trackedRequest<ReturnType<typeof adminStatsFixture>>();
        getAdminStats.mockReturnValue(pending.observable);
        await create('admin');
        expect(pending.state.subscribed).toBe(1);
        // Angular 22 planuje kolejne sprawdzenie zmian na najbliższą klatkę (wyścig setTimeout/requestAnimationFrame
        // planisty aplikacji, nie komponentu); w liczniku zostaje jeden timer frameworka/Materiala - porównujemy z nim
        vi.advanceTimersToNextFrame();
        const timersOfFramework = vi.getTimerCount();

        fixture.destroy();
        vi.advanceTimersToNextFrame();

        expect(pending.state.unsubscribed).toBe(1);
        expect(pending.subject.observed).toBe(false);
        expect(vi.getTimerCount()).toBeLessThanOrEqual(timersOfFramework);
      } finally {
        vi.useRealTimers();
      }
    });

    it('unsubscribes from a running export on destroy', async () => {
      const pending = trackedRequest<void>();
      exportCsv.mockReturnValue(pending.observable);
      await create('admin');
      button('Eksportuj CSV').click();
      await settle();

      fixture.destroy();

      expect(pending.state.unsubscribed).toBe(1);
      expect(pending.subject.observed).toBe(false);
    });

    it('does not react to URL changes after destroy', async () => {
      await create('admin');
      fixture.destroy();

      await router.navigateByUrl('/stats?from=2026-09-01&to=2026-09-30');

      expect(getAdminStats).toHaveBeenCalledTimes(1);
    });
  });
});
