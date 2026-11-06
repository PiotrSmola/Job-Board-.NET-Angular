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
    .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(190px, 1fr)); gap: 16px; margin: 0; }
    .tile { margin: 0; padding: 16px; min-height: 96px; background: var(--surface-color); border: 1px solid var(--border-color); border-radius: 12px; }
    dt { color: var(--text-secondary); font-size: 0.9rem; }
    dd { margin: 0; }
    .value { margin-top: 4px; color: var(--text-primary); font-size: 1.9rem; font-weight: 700; line-height: 1.2; }
    .hint { margin-top: 6px; color: var(--text-secondary); font-size: 0.8rem; }
  `]
})
export class StatsKpiTilesComponent {
  readonly tiles = input.required<readonly KpiTile[]>();
}
