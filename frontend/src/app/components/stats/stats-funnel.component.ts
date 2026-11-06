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
          <caption class="sr-only">Lejek rekrutacji – liczba aplikacji na każdym etapie</caption>
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
    h3 { font-size: 1.15rem; }
    .note { margin: 4px 0 12px; color: var(--text-secondary); font-size: 0.85rem; }
    .row { display: grid; grid-template-columns: 96px minmax(0, 1fr) 110px; align-items: center; gap: 12px; min-height: 36px; }
    .label { color: var(--text-primary); font-size: 0.95rem; }
    .bar { display: block; width: 100%; height: 18px; }
    .track { fill: #2a2a2a; }
    .fill { fill: #00e5ff; }
    .value { color: var(--text-secondary); font-size: 0.9rem; text-align: right; font-variant-numeric: tabular-nums; }
    .value strong { color: var(--text-primary); }
    .scroll { overflow-x: auto; }
    .scroll:focus-visible { outline: 2px solid var(--primary-color); outline-offset: 2px; }
    table { width: 100%; border-collapse: collapse; font-size: 0.9rem; }
    th, td { padding: 6px 12px; text-align: left; border-bottom: 1px solid var(--border-color); }
    .num { text-align: right; font-variant-numeric: tabular-nums; }
    .sr-only { position: absolute; width: 1px; height: 1px; margin: -1px; padding: 0; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
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
