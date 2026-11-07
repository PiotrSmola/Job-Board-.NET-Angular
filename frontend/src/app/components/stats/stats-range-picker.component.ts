import { ChangeDetectionStrategy, Component, computed, effect, input, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MAT_DATE_LOCALE, provideNativeDateAdapter } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { StatsRange } from '../../models/stats.model';
import {
  MAX_RANGE_DAYS,
  RANGE_PRESETS,
  RangePreset,
  dayToLocalDate,
  formatLocalDay,
  matchPreset,
  presetRange,
  rangeLengthDays
} from '../../utils/stats-range';

/**
 * Wybór zakresu statystyk: gotowe przedziały (7/30/90 dni, 12 miesięcy) oraz własny zakres w MatDateRangePicker.
 * Zakres własny dłuższy niż 366 dni nie jest emitowany – pokazujemy komunikat (ogłaszany jako alert).
 */
@Component({
  selector: 'app-stats-range-picker',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatDatepickerModule, MatFormFieldModule],
  providers: [provideNativeDateAdapter(), { provide: MAT_DATE_LOCALE, useValue: 'pl-PL' }],
  template: `
    <div class="presets" role="group" aria-label="Gotowe zakresy dat">
      @for (preset of presets; track preset.id) {
        <button
          type="button"
          [class.active]="activePreset() === preset.id"
          [attr.aria-pressed]="activePreset() === preset.id"
          (click)="selectPreset(preset.id)">
          {{ preset.label }}
        </button>
      }
    </div>

    <mat-form-field appearance="outline" class="custom-range" subscriptSizing="dynamic">
      <mat-label>Własny zakres dat</mat-label>
      <mat-date-range-input [formGroup]="form" [rangePicker]="picker">
        <input matStartDate formControlName="start" placeholder="Od" aria-label="Data początkowa">
        <input matEndDate formControlName="end" placeholder="Do" aria-label="Data końcowa" (dateChange)="applyCustomRange()">
      </mat-date-range-input>
      <mat-datepicker-toggle matIconSuffix [for]="picker" aria-label="Otwórz kalendarz"></mat-datepicker-toggle>
      <mat-date-range-picker #picker></mat-date-range-picker>
    </mat-form-field>

    @if (error(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }
  `,
  styles: [`
    :host { display: flex; flex-wrap: wrap; align-items: center; gap: 12px 16px; }
    /* segmentowany wybór zakresu (makieta: .seg) */
    .presets { display: inline-flex; flex-wrap: wrap; gap: 2px; padding: 4px; border-radius: var(--radius-button); background: var(--color-input-bg); }
    .presets button { height: 32px; padding: 0 14px; border: 0; border-radius: 16px; background: transparent; color: var(--color-text-secondary);
      font: inherit; font-weight: 500; cursor: pointer; }
    .presets button:hover { color: var(--color-text); }
    .presets button[aria-pressed='true'] { background: var(--color-selected); color: var(--color-text); }
    .custom-range { width: 280px; max-width: 100%; }
    .error { flex-basis: 100%; margin: 0; color: var(--color-danger); font-size: var(--font-size-sm); }
  `]
})
export class StatsRangePickerComponent {
  readonly range = input.required<StatsRange>();
  readonly rangeChange = output<StatsRange>();

  readonly presets = RANGE_PRESETS;
  readonly error = signal<string | null>(null);
  readonly activePreset = computed(() => matchPreset(this.range()));

  readonly form = new FormGroup({
    start: new FormControl<Date | null>(null),
    end: new FormControl<Date | null>(null)
  });

  constructor() {
    // Kontrolki odzwierciedlają zakres z URL (także po cofnięciu w historii przeglądarki)
    effect(() => {
      const { from, to } = this.range();
      this.form.setValue({ start: dayToLocalDate(from), end: dayToLocalDate(to) }, { emitEvent: false });
      this.error.set(null);
    });
  }

  selectPreset(preset: RangePreset): void {
    this.error.set(null);
    this.rangeChange.emit(presetRange(preset));
  }

  /** Wywoływane po wybraniu daty końcowej; przy niepełnym zakresie (tylko początek) nic nie robi. */
  applyCustomRange(): void {
    const { start, end } = this.form.getRawValue();
    if (!start || !end) return;

    const range: StatsRange = { from: formatLocalDay(start), to: formatLocalDay(end) };
    const length = rangeLengthDays(range);
    if (!(length >= 1)) {
      this.error.set('Data końcowa nie może być wcześniejsza niż początkowa');
      return;
    }
    if (length > MAX_RANGE_DAYS) {
      this.error.set(`Zakres nie może przekraczać ${MAX_RANGE_DAYS} dni`);
      return;
    }

    this.error.set(null);
    this.rangeChange.emit(range);
  }
}
