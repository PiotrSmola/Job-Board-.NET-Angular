import { ComponentFixture, TestBed } from '@angular/core/testing';
import { StatsLineChartComponent } from './stats-line-chart.component';
import { ChartSeries, toAdminView } from '../../utils/stats-view';
import { adminStatsFixture } from '../../../testing/stats-fixtures';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

function series(overrides: Partial<ChartSeries>[] = []): ChartSeries[] {
  return toAdminView(adminStatsFixture()).series.map((item, index) => ({ ...item, ...overrides[index] }));
}

describe('StatsLineChartComponent', () => {
  let fixture: ComponentFixture<StatsLineChartComponent>;

  function create(dates: string[], data: ChartSeries[]): HTMLElement {
    fixture = TestBed.createComponent(StatsLineChartComponent);
    fixture.componentRef.setInput('title', 'Aktywność w czasie');
    fixture.componentRef.setInput('dates', dates);
    fixture.componentRef.setInput('series', data);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const DATES = ['2026-11-01', '2026-11-02', '2026-11-03'];

  beforeEach(() => TestBed.configureTestingModule({ imports: [StatsLineChartComponent] }).compileComponents());

  it('renders a responsive SVG exposed as an image with a descriptive label', () => {
    const root = create(DATES, series());
    const svg = root.querySelector('svg.chart') as SVGElement;

    expect(svg.getAttribute('viewBox')).toBe('0 0 720 300');
    expect(svg.getAttribute('role')).toBe('img');
    expect(svg.getAttribute('aria-label')).toContain('od 01.11.2026 do 03.11.2026');
    expect(svg.getAttribute('aria-label')).toContain('Nowe aplikacje: razem 11');
    expect(svg.hasAttribute('width')).toBe(false);
  });

  it('draws one line per series, distinguishable by dash pattern and marker, not only by colour', () => {
    const root = create(DATES, series());
    const lines = Array.from(root.querySelectorAll('svg.chart path.line'));
    const markers = Array.from(root.querySelectorAll('svg.chart path.marker'));

    expect(lines.length).toBe(4);
    expect(new Set(lines.map(line => line.getAttribute('stroke'))).size).toBe(4);
    expect(new Set(lines.map(line => line.getAttribute('stroke-dasharray'))).size).toBe(4);
    expect(new Set(markers.map(marker => marker.getAttribute('d'))).size).toBe(4);
    lines.forEach(line => expect(line.getAttribute('d')).toMatch(/^M[\d.]+ [\d.]+ L/));
  });

  it('labels the X axis with Polish short dates and the Y axis with integer ticks', () => {
    const root = create(DATES, series());
    const labels = Array.from(root.querySelectorAll('svg.chart text.tick-label')).map(node => node.textContent?.trim());

    expect(labels).toContain('1 lis');
    expect(labels).toContain('3 lis');
    expect(labels).toContain('0');
  });

  it('shows a text legend with all series names', () => {
    const root = create(DATES, series());
    const names = Array.from(root.querySelectorAll('.legend button')).map(button => button.textContent?.trim());

    expect(names).toEqual(['Nowe aplikacje', 'Wyświetlenia ofert', 'Nowe oferty', 'Nowi użytkownicy']);
  });

  it('hides and shows a series from the legend but keeps the last visible one', () => {
    const root = create(DATES, series());
    const buttons = Array.from(root.querySelectorAll<HTMLButtonElement>('.legend button'));

    buttons[1].click();
    fixture.detectChanges();
    expect(buttons[1].getAttribute('aria-pressed')).toBe('false');
    expect(root.querySelectorAll('svg.chart path.line').length).toBe(3);

    buttons[1].click();
    fixture.detectChanges();
    expect(root.querySelectorAll('svg.chart path.line').length).toBe(4);

    buttons[1].click();
    buttons[2].click();
    buttons[3].click();
    buttons[0].click();
    fixture.detectChanges();
    expect(root.querySelectorAll('svg.chart path.line').length).toBe(1);
    expect(buttons[0].getAttribute('aria-pressed')).toBe('true');
  });

  it('switches to a real table with a caption, header cells and every day', () => {
    const root = create(DATES, series());
    (root.querySelector('mat-slide-toggle button') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(root.querySelector('svg.chart')).toBeNull();
    const table = root.querySelector('table') as HTMLTableElement;
    expect(table.querySelector('caption')?.textContent).toContain('Aktywność w czasie');
    expect(Array.from(table.querySelectorAll('thead th')).map(th => th.textContent?.trim()))
      .toEqual(['Data', 'Nowe aplikacje', 'Wyświetlenia ofert', 'Nowe oferty', 'Nowi użytkownicy']);
    const rows = table.querySelectorAll('tbody tr');
    expect(rows.length).toBe(3);
    expect(rows[2].querySelector('th')?.textContent?.trim()).toBe('03.11.2026');
    expect(Array.from(rows[2].querySelectorAll('td')).map(td => td.textContent?.trim())).toEqual(['7', '55', '2', '0']);
    expect(root.querySelector('.table-scroll')?.getAttribute('tabindex')).toBe('0');
  });

  it('labels the toggle for assistive technology', () => {
    const root = create(DATES, series());
    expect(root.querySelector('mat-slide-toggle')?.textContent).toContain('Pokaż jako tabelę');
  });

  describe('degenerate data', () => {
    it('renders axes without lines for an empty series', () => {
      const root = create([], series().map(item => ({ ...item, values: [] })));

      expect(root.querySelector('svg.chart')?.getAttribute('aria-label')).toBe('Wykres liniowy bez danych.');
      root.querySelectorAll('svg.chart path.line').forEach(line => expect(line.getAttribute('d')).toBe(''));
    });

    it('renders a single point as markers in the centre without NaN', () => {
      const root = create(['2026-11-01'], series().map(item => ({ ...item, values: [3] })));
      const html = root.querySelector('svg.chart')?.innerHTML ?? '';

      expect(html).not.toContain('NaN');
      expect(root.querySelectorAll('svg.chart path.marker').length).toBe(4);
      expect(Array.from(root.querySelectorAll('svg.chart path.line')).every(line => /^M[\d.]+ [\d.]+$/.test(line.getAttribute('d') ?? ''))).toBe(true);
    });

    it('draws a flat line on the baseline for all zeros without NaN', () => {
      const root = create(DATES, series().map(item => ({ ...item, values: [0, 0, 0] })));
      const html = root.querySelector('svg.chart')?.innerHTML ?? '';
      const labels = Array.from(root.querySelectorAll('svg.chart text.tick-label')).map(node => node.textContent?.trim());

      expect(html).not.toContain('NaN');
      expect(html).not.toContain('Infinity');
      expect(labels).toEqual(expect.arrayContaining(['0', '1']));
    });

    it('limits the number of X labels and markers for a year-long series', () => {
      const dates = Array.from({ length: 366 }, (_, index) => new Date(Date.UTC(2025, 10, 6 + index)).toISOString().slice(0, 10));
      const root = create(dates, series().map(item => ({ ...item, values: dates.map((_, index) => index % 7) })));
      const xLabels = Array.from(root.querySelectorAll('svg.chart text.tick-label')).filter(node => node.getAttribute('text-anchor') === 'middle');

      expect(xLabels.length).toBeLessThanOrEqual(7);
      const marker = root.querySelector('svg.chart path.marker') as SVGElement;
      expect((marker.getAttribute('d') ?? '').split('Z').length - 1).toBeLessThanOrEqual(12);
    });
  });
});
