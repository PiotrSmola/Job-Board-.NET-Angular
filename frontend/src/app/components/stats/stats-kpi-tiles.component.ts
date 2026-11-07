import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { KpiTile } from '../../utils/stats-view';

/** Kafelki KPI: liczba z etykietą (lista opisów – czytnik ekranu czyta „etykieta: wartość”). */
@Component({
  selector: 'app-stats-kpi-tiles',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dl class="tiles">
      @for (tile of tiles(); track tile.label) {
        <div class="tile">
          <dt>{{ tile.label }}</dt>
          <dd class="value">{{ tile.value }}</dd>
          @if (tile.hint) {
            <dd class="hint">{{ tile.hint }}</dd>
          }
        </div>
      }
    </dl>
  `,
  styles: [`
    :host { display: block; }
    .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(200px, 100%), 1fr)); gap: 12px; margin: 0; }
    .tile { display: flex; flex-direction: column; margin: 0; padding: 18px 20px; min-height: 96px; background: var(--color-surface); border-radius: var(--radius-card); }
    dt { color: var(--color-text-secondary); }
    dd { margin: 0; }
    /* wartość nad etykietą (makieta), w DOM etykieta pierwsza – czytnik czyta „etykieta: wartość” */
    .value { order: -1; color: var(--color-text); font-size: var(--font-size-h1); font-weight: 500; line-height: 1.25; font-variant-numeric: tabular-nums; }
    .hint { margin-top: 4px; color: var(--color-text-tertiary); font-size: var(--font-size-sm); }
  `]
})
export class StatsKpiTilesComponent {
  readonly tiles = input.required<readonly KpiTile[]>();
}
