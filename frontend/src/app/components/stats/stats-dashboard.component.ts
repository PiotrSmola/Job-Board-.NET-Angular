import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Observable, catchError, map, of, startWith, switchMap } from 'rxjs';
import { StatsMode, StatsRange } from '../../models/stats.model';
import { StatsService } from '../../services/stats.service';
import { getApiErrorMessage, isHandledByInterceptor } from '../../utils/http-errors';
import { defaultRange, formatFullDay, rangeFromParams } from '../../utils/stats-range';
import { DashboardView, formatMedianHours, KpiTile, toAdminView, toEmployerView } from '../../utils/stats-view';
import { StatsFunnelComponent } from './stats-funnel.component';
import { StatsJobsTableComponent } from './stats-jobs-table.component';
import { StatsKpiTilesComponent } from './stats-kpi-tiles.component';
import { StatsLineChartComponent } from './stats-line-chart.component';
import { StatsRangePickerComponent } from './stats-range-picker.component';

export type DashboardState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'ready'; view: DashboardView };

const LOAD_ERROR = 'Nie udało się wczytać statystyk';
const EXPORT_ERROR = 'Nie udało się wyeksportować danych';

/**
 * Wspólny dashboard statystyk (panel admina i pracodawcy). Zakres dat jest w query params URL (`from`, `to`),
 * a zmiana zakresu lub „Spróbuj ponownie” anuluje trwające żądanie (switchMap); zniszczenie komponentu kończy
 * wszystkie subskrypcje. Komponent ładowany leniwie (@defer w zakładkach), więc kod wykresów nie trafia do initial bundle.
 */
@Component({
  selector: 'app-stats-dashboard',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatButtonModule,
    MatIconModule,
    StatsFunnelComponent,
    StatsJobsTableComponent,
    StatsKpiTilesComponent,
    StatsLineChartComponent,
    StatsRangePickerComponent
  ],
  templateUrl: './stats-dashboard.component.html',
  styleUrl: './stats-dashboard.component.scss'
})
export class StatsDashboardComponent {
  private readonly statsService = inject(StatsService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  readonly mode = input.required<StatsMode>();

  // Zakres pochodzi z URL (źródło prawdy) – działa też przycisk „wstecz” przeglądarki
  private readonly queryParams = toSignal(this.route.queryParamMap, { requireSync: false });
  readonly range = computed<StatsRange>(() => {
    const params = this.queryParams();
    return params ? rangeFromParams(params.get('from'), params.get('to')) : defaultRange();
  }, { equal: (a, b) => a.from === b.from && a.to === b.to });

  private readonly reloadTick = signal(0);
  readonly state = signal<DashboardState>({ kind: 'loading' });
  readonly exporting = signal(false);
  readonly exportError = signal<string | null>(null);

  readonly skeletonTiles = [1, 2, 3, 4, 5, 6];
  readonly loading = computed(() => this.state().kind === 'loading');
  readonly errorMessage = computed(() => {
    const state = this.state();
    return state.kind === 'error' ? state.message : null;
  });

  readonly view = computed(() => {
    const state = this.state();
    return state.kind === 'ready' ? state.view : null;
  });

  readonly tiles = computed<KpiTile[]>(() => {
    const view = this.view();
    if (!view) return [];
    return [
      ...view.kpis,
      {
        label: 'Mediana czasu pierwszej odpowiedzi',
        value: formatMedianHours(view.medianHours),
        hint: 'od złożenia aplikacji do pierwszej zmiany statusu przez rekrutera'
      }
    ];
  });

  readonly announcement = computed(() => {
    const state = this.state();
    if (state.kind === 'loading') return 'Wczytywanie statystyk…';
    if (state.kind === 'ready') return `Statystyki za okres ${formatFullDay(state.view.from)} – ${formatFullDay(state.view.to)} zostały wczytane`;
    return '';
  });

  constructor() {
    toObservable(computed(() => ({ mode: this.mode(), range: this.range(), tick: this.reloadTick() }))).pipe(
      // switchMap anuluje poprzednie żądanie przy zmianie zakresu, trybu lub ponownej próbie
      switchMap(({ mode, range }) => this.load(mode, range).pipe(
        map((view): DashboardState => ({ kind: 'ready', view })),
        catchError((error: unknown) => {
          console.error('Error loading stats:', error);
          // 401/403 pokazał już interceptor – tu ogólny tekst, bez dublowania powodu
          return of<DashboardState>({
            kind: 'error',
            message: isHandledByInterceptor(error) ? LOAD_ERROR : getApiErrorMessage(error, LOAD_ERROR)
          });
        }),
        startWith<DashboardState>({ kind: 'loading' })
      )),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(state => this.state.set(state));
  }

  private load(mode: StatsMode, range: StatsRange): Observable<DashboardView> {
    return mode === 'admin'
      ? this.statsService.getAdminStats(range).pipe(map(toAdminView))
      : this.statsService.getEmployerStats(range).pipe(map(toEmployerView));
  }

  setRange(range: StatsRange): void {
    if (range.from === this.range().from && range.to === this.range().to) return;
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { from: range.from, to: range.to },
      queryParamsHandling: 'merge',
      replaceUrl: true
    });
  }

  reload(): void {
    this.reloadTick.update(tick => tick + 1);
  }

  exportCsv(): void {
    if (this.exporting()) return;

    this.exporting.set(true);
    this.exportError.set(null);
    this.statsService.exportCsv(this.mode(), this.range())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.exporting.set(false),
        error: (error: unknown) => {
          console.error('Error exporting stats:', error);
          this.exporting.set(false);
          if (!isHandledByInterceptor(error)) {
            this.exportError.set(getApiErrorMessage(error, EXPORT_ERROR));
          }
        }
      });
  }
}
