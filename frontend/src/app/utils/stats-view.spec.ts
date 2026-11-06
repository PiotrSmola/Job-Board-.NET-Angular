import { adminStatsFixture, employerStatsFixture } from '../../testing/stats-fixtures';
import {
  buildFunnelStages,
  describeFunnel,
  describeSeries,
  formatCount,
  formatMedianHours,
  formatPercent,
  toAdminView,
  toEmployerView
} from './stats-view';

describe('stats-view', () => {
  describe('formatMedianHours', () => {
    it('formats hours with the approximation sign', () => {
      expect(formatMedianHours(5)).toBe('~5 h');
      expect(formatMedianHours(1.54)).toBe(`~${(1.5).toLocaleString('pl-PL')} h`);
      expect(formatMedianHours(26.4)).toBe('~26 h');
      expect(formatMedianHours(0)).toBe('~0 h');
    });

    it('shows "brak danych" without a median', () => {
      expect(formatMedianHours(null)).toBe('brak danych');
      expect(formatMedianHours(undefined)).toBe('brak danych');
      expect(formatMedianHours(NaN)).toBe('brak danych');
      expect(formatMedianHours(-1)).toBe('brak danych');
    });
  });

  describe('formatPercent', () => {
    it('formats fractions as percent with one decimal', () => {
      expect(formatPercent(0.125)).toBe(`${(12.5).toLocaleString('pl-PL')}%`);
      expect(formatPercent(0)).toBe('0%');
      expect(formatPercent(NaN)).toBe('0%');
    });
  });

  describe('buildFunnelStages', () => {
    it('computes percentages relative to submitted', () => {
      const stages = buildFunnelStages({ submitted: 8, inReview: 4, interview: 2, offered: 1, hired: 0 });
      expect(stages.map(stage => stage.label)).toEqual(['Złożone', 'W analizie', 'Rozmowa', 'Oferta', 'Zatrudnieni']);
      expect(stages.map(stage => stage.percent)).toEqual([100, 50, 25, 12.5, 0]);
    });

    it('does not divide by zero when nothing was submitted', () => {
      const stages = buildFunnelStages({ submitted: 0, inReview: 0, interview: 0, offered: 0, hired: 0 });
      expect(stages.every(stage => stage.percent === 0 && stage.count === 0)).toBe(true);
    });
  });

  describe('toAdminView', () => {
    it('builds KPIs, four distinguishable series and top jobs', () => {
      const view = toAdminView(adminStatsFixture());

      expect(view.mode).toBe('admin');
      expect(view.kpis.find(tile => tile.label === 'Użytkownicy')?.value).toBe(formatCount(134));
      expect(view.kpis.find(tile => tile.label === 'Nowe aplikacje w zakresie')?.value).toBe('11');
      expect(view.kpis.find(tile => tile.label === 'Wyświetlenia ofert w zakresie')?.value).toBe('97');
      expect(view.dates).toEqual(['2026-11-01', '2026-11-02', '2026-11-03']);
      expect(view.series.map(series => series.key)).toEqual(['newApplications', 'jobViews', 'newJobs', 'newUsers']);
      expect(new Set(view.series.map(series => series.color)).size).toBe(4);
      expect(new Set(view.series.map(series => series.marker)).size).toBe(4);
      expect(new Set(view.series.map(series => series.dash)).size).toBe(4);
      expect(view.jobRows.map(row => row.title)).toEqual(['Programista .NET', 'Analityk']);
      expect(view.statusBreakdown).toEqual([{ label: 'Złożona', count: 100 }, { label: 'Zatrudniony', count: 5 }]);
      expect(view.hasData).toBe(true);
    });

    it('reports no data for an all-zero range', () => {
      const view = toAdminView(adminStatsFixture({
        series: [{ date: '2026-11-01', newApplications: 0, newJobs: 0, newUsers: 0, jobViews: 0 }],
        funnel: { submitted: 0, inReview: 0, interview: 0, offered: 0, hired: 0 },
        topJobs: []
      }));
      expect(view.hasData).toBe(false);
    });
  });

  describe('toEmployerView', () => {
    it('maps jobs with their state label and conversion', () => {
      const view = toEmployerView(employerStatsFixture());

      expect(view.mode).toBe('employer');
      expect(view.series.map(series => series.key)).toEqual(['newApplications', 'jobViews']);
      expect(view.jobRows.map(row => row.statusLabel)).toEqual(['Aktywna', 'Szkic', 'Wygasła']);
      expect(view.jobRows[0].conversion).toBe(0.1);
      expect(view.medianHours).toBeNull();
      expect(view.kpis.find(tile => tile.label === 'Wyświetlenia ofert (łącznie)')?.value).toBe('410');
    });
  });

  describe('descriptions', () => {
    it('summarizes the series for screen readers, including totals and the peak day', () => {
      const view = toAdminView(adminStatsFixture());
      const text = describeSeries(view.dates, view.series);

      expect(text).toContain('od 01.11.2026 do 03.11.2026');
      expect(text).toContain('Nowe aplikacje: razem 11, szczyt 7 dnia 03.11.2026');
      expect(text).toContain('Wyświetlenia ofert: razem 97');
    });

    it('omits the peak for a series of zeros and handles no dates', () => {
      const text = describeSeries(['2026-11-01'], [{ key: 'a', label: 'Zera', values: [0], color: '#fff', dash: '', marker: 'circle' }]);
      expect(text).toContain('Zera: razem 0.');
      expect(text).not.toContain('szczyt');
      expect(describeSeries([], [])).toBe('Wykres liniowy bez danych.');
    });

    it('summarizes the funnel', () => {
      const text = describeFunnel(buildFunnelStages({ submitted: 4, inReview: 2, interview: 1, offered: 0, hired: 0 }));
      expect(text).toContain('Złożone: 4 (100%)');
      expect(text).toContain('W analizie: 2 (50%)');
    });
  });
});
