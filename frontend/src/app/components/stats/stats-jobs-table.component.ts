import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { JobColumn, JobColumnKey, JobRow, formatCount, formatPercent } from '../../utils/stats-view';

type SortDirection = 'asc' | 'desc';

const collator = new Intl.Collator('pl');

/** Wartość komórki jako liczba (kolumny liczbowe) lub tekst – do sortowania. */
function sortValue(row: JobRow, key: JobColumnKey): number | string {
  switch (key) {
    case 'title': return row.title;
    case 'company': return row.company;
    case 'status': return row.statusLabel;
    case 'views': return row.views;
    case 'applications': return row.applications;
    case 'conversion': return row.conversion;
  }
}

/** Sortuje kopię wierszy; remisy rozstrzyga kolejność z API (sort stabilny). */
export function sortJobRows(rows: readonly JobRow[], key: JobColumnKey, direction: SortDirection): JobRow[] {
  const sign = direction === 'asc' ? 1 : -1;
  return [...rows].sort((a, b) => {
    const left = sortValue(a, key);
    const right = sortValue(b, key);
    const result = typeof left === 'number' && typeof right === 'number'
      ? left - right
      : collator.compare(String(left), String(right));
    return result * sign;
  });
}

/** Sortowalna tabela ofert: nagłówki to przyciski z `aria-sort`, zmiana sortowania jest ogłaszana. */
@Component({
  selector: 'app-stats-jobs-table',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule],
  template: `
    <h3>{{ title() }}</h3>
    @if (rows().length) {
      <div class="scroll" tabindex="0" role="region" [attr.aria-label]="title() + ' – tabela, przewiń poziomo na wąskim ekranie'">
        <table>
          <caption class="sr-only">{{ title() }}</caption>
          <thead>
            <tr>
              @for (column of columns(); track column.key) {
                <th scope="col" [class.num]="column.numeric" [attr.aria-sort]="ariaSort(column.key)">
                  <button type="button" (click)="sortBy(column.key)">
                    {{ column.label }}
                    <mat-icon aria-hidden="true">{{ sortIcon(column.key) }}</mat-icon>
                  </button>
                </th>
              }
            </tr>
          </thead>
          <tbody>
            @for (row of sortedRows(); track row.jobId) {
              <tr>
                @for (column of columns(); track column.key) {
                  @if (column.key === 'title') {
                    <th scope="row">{{ row.title }}</th>
                  } @else {
                    <td [class.num]="column.numeric">{{ cell(row, column) }}</td>
                  }
                }
              </tr>
            }
          </tbody>
        </table>
      </div>
    } @else {
      <p class="empty">Brak ofert do wyświetlenia.</p>
    }
    <p class="sr-only" role="status">{{ announcement() }}</p>
  `,
  styles: [`
    :host { display: block; }
    h3 { font-size: 1.15rem; margin-bottom: 8px; }
    .scroll { overflow-x: auto; border: 1px solid var(--border-color); border-radius: 12px; background: var(--surface-color); }
    .scroll:focus-visible, th button:focus-visible { outline: 2px solid var(--primary-color); outline-offset: 2px; }
    table { width: 100%; border-collapse: collapse; font-size: 0.95rem; }
    th, td { padding: 10px 16px; text-align: left; border-bottom: 1px solid var(--border-color); }
    tbody th { font-weight: 500; }
    tbody tr:last-child > * { border-bottom: 0; }
    thead th { padding: 4px 8px; white-space: nowrap; }
    th button { display: inline-flex; align-items: center; gap: 2px; min-height: 36px; padding: 0 8px; color: var(--text-primary);
      background: none; border: 0; border-radius: 6px; cursor: pointer; font: inherit; font-weight: 500; }
    th button:hover { background: var(--surface-hover); }
    th button mat-icon { width: 18px; height: 18px; font-size: 18px; color: var(--text-secondary); }
    th[aria-sort='ascending'] mat-icon, th[aria-sort='descending'] mat-icon { color: var(--primary-color); }
    .num { text-align: right; font-variant-numeric: tabular-nums; }
    .empty { color: var(--text-secondary); margin: 0; }
    .sr-only { position: absolute; width: 1px; height: 1px; margin: -1px; padding: 0; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
  `]
})
export class StatsJobsTableComponent {
  readonly title = input.required<string>();
  readonly columns = input.required<readonly JobColumn[]>();
  readonly rows = input.required<readonly JobRow[]>();

  /** Domyślnie zgodnie z API: najwięcej aplikacji na górze. */
  readonly sortKey = signal<JobColumnKey>('applications');
  readonly direction = signal<SortDirection>('desc');
  readonly announcement = signal('');

  readonly sortedRows = computed(() => sortJobRows(this.rows(), this.sortKey(), this.direction()));

  sortBy(key: JobColumnKey): void {
    const direction: SortDirection = this.sortKey() === key
      ? (this.direction() === 'asc' ? 'desc' : 'asc')
      // teksty rosnąco (A–Z), liczby malejąco (największe na górze)
      : (this.columns().find(column => column.key === key)?.numeric ? 'desc' : 'asc');

    this.sortKey.set(key);
    this.direction.set(direction);
    const label = this.columns().find(column => column.key === key)?.label ?? '';
    this.announcement.set(`Posortowano według kolumny ${label}, ${direction === 'asc' ? 'rosnąco' : 'malejąco'}`);
  }

  ariaSort(key: JobColumnKey): 'ascending' | 'descending' | 'none' {
    if (this.sortKey() !== key) return 'none';
    return this.direction() === 'asc' ? 'ascending' : 'descending';
  }

  sortIcon(key: JobColumnKey): string {
    if (this.sortKey() !== key) return 'unfold_more';
    return this.direction() === 'asc' ? 'arrow_upward' : 'arrow_downward';
  }

  cell(row: JobRow, column: JobColumn): string {
    switch (column.key) {
      case 'company': return row.company;
      case 'status': return row.statusLabel;
      case 'views': return formatCount(row.views);
      case 'applications': return formatCount(row.applications);
      case 'conversion': return formatPercent(row.conversion);
      default: return row.title;
    }
  }
}
