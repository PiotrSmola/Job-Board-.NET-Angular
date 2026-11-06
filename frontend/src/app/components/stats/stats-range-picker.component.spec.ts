import { ComponentFixture, TestBed } from '@angular/core/testing';
import { StatsRangePickerComponent } from './stats-range-picker.component';
import { StatsRange } from '../../models/stats.model';
import { presetRange } from '../../utils/stats-range';

vi.setConfig({ testTimeout: 20000 });

describe('StatsRangePickerComponent', () => {
  let fixture: ComponentFixture<StatsRangePickerComponent>;
  let emitted: StatsRange[];

  function create(range: StatsRange): HTMLElement {
    fixture = TestBed.createComponent(StatsRangePickerComponent);
    emitted = [];
    fixture.componentInstance.rangeChange.subscribe(value => emitted.push(value));
    fixture.componentRef.setInput('range', range);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function presetButtons(root: HTMLElement): HTMLButtonElement[] {
    return Array.from(root.querySelectorAll<HTMLButtonElement>('.presets button'));
  }

  beforeEach(() => TestBed.configureTestingModule({ imports: [StatsRangePickerComponent] }).compileComponents());

  it('offers the four presets in a labelled group', () => {
    const root = create(presetRange('30d'));

    expect(root.querySelector('.presets')?.getAttribute('aria-label')).toBe('Gotowe zakresy dat');
    expect(presetButtons(root).map(button => button.textContent?.trim())).toEqual(['7 dni', '30 dni', '90 dni', '12 miesięcy']);
  });

  it('marks the active preset with aria-pressed', () => {
    const root = create(presetRange('90d'));

    expect(presetButtons(root).map(button => button.getAttribute('aria-pressed'))).toEqual(['false', 'false', 'true', 'false']);
  });

  it('marks no preset for a custom range', () => {
    const root = create({ from: '2026-01-01', to: '2026-01-15' });

    expect(presetButtons(root).every(button => button.getAttribute('aria-pressed') === 'false')).toBe(true);
  });

  it('emits the preset range on click', () => {
    const root = create({ from: '2026-01-01', to: '2026-01-15' });

    presetButtons(root)[0].click();

    expect(emitted).toEqual([presetRange('7d')]);
  });

  it('shows the current range in the date inputs', () => {
    const root = create({ from: '2026-01-01', to: '2026-01-15' });

    const form = fixture.componentInstance.form.getRawValue();
    expect(form.start?.getDate()).toBe(1);
    expect(form.end?.getDate()).toBe(15);
    expect(root.querySelector('input.mat-start-date')).not.toBeNull();
    expect(root.querySelector('input.mat-end-date')).not.toBeNull();
    expect(root.querySelector('input.mat-start-date')?.getAttribute('aria-label')).toBe('Data początkowa');
  });

  it('emits a custom range once both dates are chosen', () => {
    create(presetRange('30d'));

    fixture.componentInstance.form.setValue({ start: new Date(2026, 0, 5), end: new Date(2026, 1, 3) });
    fixture.componentInstance.applyCustomRange();

    expect(emitted).toEqual([{ from: '2026-01-05', to: '2026-02-03' }]);
  });

  it('waits for the end date', () => {
    create(presetRange('30d'));

    fixture.componentInstance.form.setValue({ start: new Date(2026, 0, 5), end: null });
    fixture.componentInstance.applyCustomRange();

    expect(emitted).toEqual([]);
  });

  it('rejects ranges longer than 366 days with an announced error', () => {
    const root = create(presetRange('30d'));

    fixture.componentInstance.form.setValue({ start: new Date(2024, 0, 1), end: new Date(2026, 0, 1) });
    fixture.componentInstance.applyCustomRange();
    fixture.detectChanges();

    expect(emitted).toEqual([]);
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('366');
  });

  it('rejects an end date before the start date', () => {
    const root = create(presetRange('30d'));

    fixture.componentInstance.form.setValue({ start: new Date(2026, 5, 10), end: new Date(2026, 5, 1) });
    fixture.componentInstance.applyCustomRange();
    fixture.detectChanges();

    expect(emitted).toEqual([]);
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('wcześniejsza');
  });

  it('accepts exactly 366 days and clears an earlier error', () => {
    const root = create(presetRange('30d'));
    fixture.componentInstance.form.setValue({ start: new Date(2024, 0, 1), end: new Date(2026, 0, 1) });
    fixture.componentInstance.applyCustomRange();

    fixture.componentInstance.form.setValue({ start: new Date(2025, 0, 1), end: new Date(2026, 0, 1) });
    fixture.componentInstance.applyCustomRange();
    fixture.detectChanges();

    expect(emitted).toEqual([{ from: '2025-01-01', to: '2026-01-01' }]);
    expect(root.querySelector('[role="alert"]')).toBeNull();
  });
});
