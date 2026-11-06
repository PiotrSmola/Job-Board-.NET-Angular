import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import {
  buildLinePath,
  linearScale,
  markerPath,
  niceScale,
  pickSpreadIndexes,
  pointPositions
} from '../../utils/chart-scale';
import { formatFullDay, formatShortDay } from '../../utils/stats-range';
import { ChartSeries, describeSeries, formatCount } from '../../utils/stats-view';

// Układ w jednostkach viewBox – wykres skaluje się do szerokości kontenera
export const CHART_WIDTH = 720;
export const CHART_HEIGHT = 300;
const MARGIN = { top: 14, right: 18, bottom: 34, left: 50 };
const MAX_X_LABELS = 7;
const MAX_MARKERS = 12;
const MARKER_RADIUS = 4.5;

/**
 * Wykres liniowy szeregu czasowego we własnym SVG. Serie są rozróżnialne kolorem, kreskowaniem i kształtem znaczników;
 * legenda to przyciski (aria-pressed) włączające/wyłączające serie – skala osi Y dopasowuje się do widocznych serii.
 * Alternatywa dla wykresu: prawdziwa tabela z danymi (przełącznik „Pokaż jako tabelę”).
 */
@Component({
  selector: 'app-stats-line-chart',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatSlideToggleModule],
  template: `
    <div class="header">
      <h3>{{ title() }}</h3>
      <mat-slide-toggle [checked]="showTable()" (change)="showTable.set($event.checked)">Pokaż jako tabelę</mat-slide-toggle>
    </div>

    <ul class="legend" aria-label="Legenda – kliknij, aby ukryć lub pokazać serię">
      @for (item of series(); track item.key) {
        <li>
          <button type="button" [attr.aria-pressed]="isVisible(item.key)" (click)="toggleSeries(item.key)">
            <svg width="40" height="14" viewBox="0 0 40 14" aria-hidden="true" focusable="false">
              <path d="M2 7 H38" fill="none" stroke-width="2.5" [attr.stroke]="item.color" [attr.stroke-dasharray]="item.dash || null" />
              <path [attr.d]="legendMarker(item)" [attr.fill]="item.color" />
            </svg>
            <span [class.off]="!isVisible(item.key)">{{ item.label }}</span>
          </button>
        </li>
      }
    </ul>

    @if (showTable()) {
      <div class="scroll table-scroll" tabindex="0" role="region" [attr.aria-label]="title() + ' – tabela danych'">
        <table>
          <caption class="sr-only">{{ title() }} – wartości dzienne</caption>
          <thead>
            <tr>
              <th scope="col">Data</th>
              @for (item of series(); track item.key) {
                <th scope="col" class="num">{{ item.label }}</th>
              }
            </tr>
          </thead>
          <tbody>
            @for (row of tableRows(); track row.date) {
              <tr>
                <th scope="row">{{ row.date }}</th>
                @for (value of row.values; track $index) {
                  <td class="num">{{ value }}</td>
                }
              </tr>
            }
          </tbody>
        </table>
      </div>
    } @else {
      <div class="scroll" tabindex="0" role="region" [attr.aria-label]="title() + ' – wykres, przewiń poziomo na wąskim ekranie'">
        <svg class="chart" [attr.viewBox]="viewBox" role="img" [attr.aria-label]="summary()">
          @for (tick of geometry().yTicks; track tick.value) {
            <line class="grid" [attr.x1]="left" [attr.x2]="right" [attr.y1]="tick.y" [attr.y2]="tick.y" />
            <text class="tick-label" text-anchor="end" [attr.x]="left - 8" [attr.y]="tick.y + 4">{{ tick.label }}</text>
          }
          <line class="axis" [attr.x1]="left" [attr.x2]="right" [attr.y1]="bottom" [attr.y2]="bottom" />
          <line class="axis" [attr.x1]="left" [attr.x2]="left" [attr.y1]="top" [attr.y2]="bottom" />
          @for (tick of geometry().xTicks; track tick.index) {
            <line class="axis" [attr.x1]="tick.x" [attr.x2]="tick.x" [attr.y1]="bottom" [attr.y2]="bottom + 5" />
            <text class="tick-label" text-anchor="middle" [attr.x]="tick.x" [attr.y]="bottom + 20">{{ tick.label }}</text>
          }
          @for (line of geometry().lines; track line.key) {
            <path class="line" fill="none" stroke-width="2.5" stroke-linejoin="round" stroke-linecap="round"
              [attr.d]="line.path" [attr.stroke]="line.color" [attr.stroke-dasharray]="line.dash || null" />
            <path class="marker" [attr.d]="line.markers" [attr.fill]="line.color" />
          }
        </svg>
      </div>
    }
  `,
  styles: [`
    :host { display: block; }
    .header { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 8px 16px; }
    h3 { font-size: 1.15rem; }
    .legend { display: flex; flex-wrap: wrap; gap: 4px 16px; list-style: none; margin: 8px 0; padding: 0; }
    .legend button { display: inline-flex; align-items: center; gap: 8px; min-height: 32px; padding: 2px 8px; color: var(--text-primary);
      background: none; border: 1px solid transparent; border-radius: 6px; cursor: pointer; font: inherit; font-size: 0.9rem; }
    .legend button[aria-pressed='true'] { border-color: var(--border-color); }
    .legend button:hover { background: var(--surface-hover); }
    .legend button:focus-visible, .scroll:focus-visible { outline: 2px solid var(--primary-color); outline-offset: 2px; }
    .off { color: var(--text-secondary); text-decoration: line-through; }
    .scroll { overflow-x: auto; }
    .chart { display: block; width: 100%; min-width: 480px; height: auto; }
    .grid { stroke: #3d3d3d; stroke-width: 1; }
    .axis { stroke: #8a8a8a; stroke-width: 1; }
    .tick-label { fill: #b0b0b0; font-size: 13px; }
    .table-scroll { max-height: 360px; overflow-y: auto; }
    table { width: 100%; border-collapse: collapse; font-size: 0.9rem; }
    th, td { padding: 6px 12px; text-align: left; border-bottom: 1px solid var(--border-color); }
    thead th { position: sticky; top: 0; background: var(--surface-color); }
    .num { text-align: right; font-variant-numeric: tabular-nums; }
    .sr-only { position: absolute; width: 1px; height: 1px; margin: -1px; padding: 0; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
  `]
})
export class StatsLineChartComponent {
  readonly title = input.required<string>();
  /** Dni YYYY-MM-DD, po jednym na punkt każdej serii. */
  readonly dates = input.required<readonly string[]>();
  readonly series = input.required<readonly ChartSeries[]>();

  readonly showTable = signal(false);
  private readonly hidden = signal<ReadonlySet<string>>(new Set());

  readonly viewBox = `0 0 ${CHART_WIDTH} ${CHART_HEIGHT}`;
  readonly left = MARGIN.left;
  readonly right = CHART_WIDTH - MARGIN.right;
  readonly top = MARGIN.top;
  readonly bottom = CHART_HEIGHT - MARGIN.bottom;

  private readonly visibleSeries = computed(() => this.series().filter(item => !this.hidden().has(item.key)));

  readonly summary = computed(() => describeSeries(this.dates(), this.visibleSeries()));

  readonly tableRows = computed(() => this.dates().map((date, index) => ({
    date: formatFullDay(date),
    values: this.series().map(item => formatCount(item.values[index] ?? 0))
  })));

  readonly geometry = computed(() => {
    const dates = this.dates();
    const visible = this.visibleSeries();
    const maxValue = Math.max(0, ...visible.flatMap(item => item.values));
    const scale = niceScale(maxValue);

    const xs = pointPositions(dates.length, this.left, this.right);
    const toY = linearScale([0, scale.max], [this.bottom, this.top]);
    const markerIndexes = pickSpreadIndexes(dates.length, MAX_MARKERS);

    return {
      yTicks: scale.ticks.map(value => ({ value, y: toY(value), label: formatCount(value) })),
      xTicks: pickSpreadIndexes(dates.length, MAX_X_LABELS).map(index => ({ index, x: xs[index], label: formatShortDay(dates[index]) })),
      lines: visible.map(item => {
        const ys = item.values.map(toY);
        return {
          key: item.key,
          color: item.color,
          dash: item.dash,
          path: buildLinePath(xs, ys),
          markers: markerIndexes.map(index => markerPath(item.marker, xs[index], ys[index], MARKER_RADIUS)).join(' ')
        };
      })
    };
  });

  isVisible(key: string): boolean {
    return !this.hidden().has(key);
  }

  /** Ukrywa/pokazuje serię; ostatniej widocznej serii nie da się ukryć (wykres nigdy nie jest pusty). */
  toggleSeries(key: string): void {
    const next = new Set(this.hidden());
    if (next.has(key)) {
      next.delete(key);
    } else if (this.visibleSeries().length > 1) {
      next.add(key);
    }
    this.hidden.set(next);
  }

  legendMarker(item: ChartSeries): string {
    return markerPath(item.marker, 20, 7, 4);
  }
}
