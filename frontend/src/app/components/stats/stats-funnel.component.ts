import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { FunnelStage, describeFunnel, formatCount } from '../../utils/stats-view';

const BAR_WIDTH = 100;
const BAR_HEIGHT = 14;

/**
 * Lejek rekrutacji: poziome słupki (SVG) z liczbą i udziałem względem złożonych aplikacji.
 * Każdy wiersz podpisany tekstem (etykieta, liczba, %), więc informacja nie zależy od koloru ani długości słupka;
 * cały wykres jest jednym obrazkiem z opisem (role="img"), a alternatywą jest tabela.
 */
@Component({
  selector: 'app-stats-funnel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatSlideToggleModule],
  template: `
    <div class="header">
      <h3>Lejek rekrutacji</h3>
      <mat-slide-toggle [checked]="showTable()" (change)="showTable.set($event.checked)">Pokaż jako tabelę</mat-slide-toggle>
    </div>
    <p class="note">Aplikacje złożone w wybranym zakresie, które osiągnęły dany etap.</p>

    @if (showTable()) {
      <div class="scroll" tabindex="0" role="region" aria-label="Lejek rekrutacji – tabela danych">
        <table>
          <caption class="visually-hidden">Lejek rekrutacji – liczba aplikacji na każdym etapie</caption>
          <thead>
            <tr>
              <th scope="col">Etap</th>
              <th scope="col" class="num">Liczba</th>
              <th scope="col" class="num">% złożonych</th>
            </tr>
          </thead>
          <tbody>
            @for (row of rows(); track row.key) {
              <tr>
                <th scope="row">{{ row.label }}</th>
                <td class="num">{{ row.count }}</td>
                <td class="num">{{ row.percent }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    } @else {
      <div class="funnel" role="img" [attr.aria-label]="summary()">
        @for (row of rows(); track row.key) {
          <div class="row" aria-hidden="true">
            <span class="label">{{ row.label }}</span>
            <svg class="bar" [attr.viewBox]="viewBox" preserveAspectRatio="none" focusable="false">
              <rect class="track" x="0" y="0" [attr.width]="barWidth" [attr.height]="barHeight" />
              <rect class="fill" x="0" y="0" [attr.width]="row.width" [attr.height]="barHeight" />
            </svg>
            <span class="value"><strong>{{ row.count }}</strong> · {{ row.percent }}</span>
          </div>
        }
      </div>
    }
  `,
  styles: [`
    :host { display: block; }
    .header { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 8px 16px; }
    h3 { margin: 0; font-size: var(--font-size-lg); font-weight: 500; }
    .note { margin: 4px 0 16px; color: var(--color-text-secondary); font-size: var(--font-size-sm); }
    .row { display: grid; grid-template-columns: minmax(0, 1fr) auto; align-items: center; gap: 4px 8px; margin-bottom: 12px; }
    .label { color: var(--color-text); }
    .bar { grid-column: 1 / -1; display: block; width: 100%; height: 10px; border-radius: 5px; overflow: hidden; }
    .track { fill: var(--color-selected); }
    .fill { fill: var(--color-accent); }
    .value { color: var(--color-text-secondary); text-align: right; font-variant-numeric: tabular-nums; }
    .value strong { color: var(--color-text); font-weight: 500; }
    .scroll { overflow-x: auto; }
    table { width: 100%; border-collapse: collapse; }
    th, td { padding: 8px 12px; text-align: left; box-shadow: inset 0 -1px 0 var(--color-hairline); }
    thead th { color: var(--color-text-tertiary); font-size: var(--font-size-sm); font-weight: 500; }
    tbody th { font-weight: 400; }
    .num { text-align: right; font-variant-numeric: tabular-nums; }
  `]
})
export class StatsFunnelComponent {
  readonly stages = input.required<readonly FunnelStage[]>();

  readonly showTable = signal(false);
  readonly barWidth = BAR_WIDTH;
  readonly barHeight = BAR_HEIGHT;
  readonly viewBox = `0 0 ${BAR_WIDTH} ${BAR_HEIGHT}`;

  readonly summary = computed(() => describeFunnel(this.stages()));

  readonly rows = computed(() => this.stages().map(stage => ({
    key: stage.key,
    label: stage.label,
    count: formatCount(stage.count),
    percent: `${stage.percent.toLocaleString('pl-PL')}%`,
    // niepusty etap ma zawsze widoczny słupek (min. 1% szerokości)
    width: stage.count > 0 ? Math.min(BAR_WIDTH, Math.max(1, stage.percent)) : 0
  })));
}
