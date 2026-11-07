import { ApplicationStatusCount, AdminStats, EmployerStats, StatsFunnel, StatsMode } from '../models/stats.model';
import { APPLICATION_STATUS_PRESENTATION, getJobState, JOB_STATE_PRESENTATION } from './status-labels';
import { MarkerShape } from './chart-scale';
import { formatFullDay } from './stats-range';

/** Seria wykresu liniowego: wartości odpowiadają kolejnym dniom zakresu. */
export interface ChartSeries {
  key: string;
  label: string;
  values: number[];
  /** Kolor linii (kontrast ≥ 3:1 na tle karty #161616). */
  color: string;
  /** Wzór kreskowania – seria rozróżnialna także bez koloru ('' = linia ciągła). */
  dash: string;
  marker: MarkerShape;
}

export interface KpiTile {
  label: string;
  value: string;
  hint?: string;
}

export interface FunnelStage {
  key: keyof StatsFunnel;
  label: string;
  count: number;
  /** Udział względem `submitted` w procentach (0 gdy brak złożonych). */
  percent: number;
}

export type JobColumnKey = 'title' | 'company' | 'status' | 'views' | 'applications' | 'conversion';

export interface JobRow {
  jobId: number;
  title: string;
  company: string;
  statusLabel: string;
  views: number;
  applications: number;
  /** Konwersja jako ułamek 0–1. */
  conversion: number;
}

export interface JobColumn {
  key: JobColumnKey;
  label: string;
  numeric: boolean;
}

export interface DashboardView {
  mode: StatsMode;
  from: string;
  to: string;
  kpis: KpiTile[];
  statusBreakdown: { label: string; count: number }[];
  dates: string[];
  series: ChartSeries[];
  funnel: FunnelStage[];
  jobsTitle: string;
  jobColumns: JobColumn[];
  jobRows: JobRow[];
  medianHours: number | null;
  /** Czy w zakresie wystąpiła jakakolwiek aktywność (aplikacje, wyświetlenia, nowe oferty/konta). */
  hasData: boolean;
}

// Kolory z tokenów (akcent, bursztyn, niebieski, zielony) – na karcie --color-surface kontrast elementów graficznych ≥ 3:1;
// serie rozróżnia też kreskowanie i kształt znaczników, więc informacja nie zależy od samego koloru
const SERIES_STYLES: { color: string; dash: string; marker: MarkerShape }[] = [
  { color: 'var(--color-accent)', dash: '', marker: 'circle' },
  { color: 'var(--status-review)', dash: '8 4', marker: 'square' },
  { color: 'var(--status-submitted)', dash: '2 4', marker: 'diamond' },
  { color: 'var(--status-offered)', dash: '10 3 2 3', marker: 'triangle' }
];

const FUNNEL_LABELS: { key: keyof StatsFunnel; label: string }[] = [
  { key: 'submitted', label: 'Złożone' },
  { key: 'inReview', label: 'W analizie' },
  { key: 'interview', label: 'Rozmowa' },
  { key: 'offered', label: 'Oferta' },
  { key: 'hired', label: 'Zatrudnieni' }
];

export function formatCount(value: number): string {
  return value.toLocaleString('pl-PL');
}

/** „~5 h” / „~1,5 h”; „brak danych”, gdy mediany nie da się policzyć. */
export function formatMedianHours(hours: number | null | undefined): string {
  if (hours === null || hours === undefined || !Number.isFinite(hours) || hours < 0) return 'brak danych';
  const rounded = hours < 10 ? Math.round(hours * 10) / 10 : Math.round(hours);
  return `~${rounded.toLocaleString('pl-PL')} h`;
}

/** „12,5%” – ułamek 0–1 jako procent z jednym miejscem po przecinku. */
export function formatPercent(fraction: number): string {
  const percent = Number.isFinite(fraction) ? fraction * 100 : 0;
  return `${(Math.round(percent * 10) / 10).toLocaleString('pl-PL')}%`;
}

export function buildFunnelStages(funnel: StatsFunnel): FunnelStage[] {
  const base = funnel.submitted;
  return FUNNEL_LABELS.map(({ key, label }) => {
    const count = funnel[key] ?? 0;
    return { key, label, count, percent: base > 0 ? Math.round((count / base) * 1000) / 10 : 0 };
  });
}

function styled(key: string, label: string, values: number[], index: number): ChartSeries {
  return { key, label, values, ...SERIES_STYLES[index] };
}

function statusBreakdown(byStatus: ApplicationStatusCount[]): { label: string; count: number }[] {
  return byStatus
    .map(({ status, count }) => ({ label: APPLICATION_STATUS_PRESENTATION[status]?.label ?? 'Nieznany', count }))
    .filter(item => item.count > 0);
}

function sum(values: readonly number[]): number {
  return values.reduce((total, value) => total + value, 0);
}

function hasActivity(dailyTotals: readonly number[], submitted: number): boolean {
  return submitted > 0 || dailyTotals.some(value => value > 0);
}

export function toAdminView(stats: AdminStats): DashboardView {
  const { totals, series } = stats;
  const users = totals.users.candidates + totals.users.employers + totals.users.admins;
  const jobs = totals.jobs.draft + totals.jobs.active + totals.jobs.expired + totals.jobs.closed;
  const views = series.map(point => point.jobViews);
  const applications = series.map(point => point.newApplications);

  return {
    mode: 'admin',
    from: stats.range.from,
    to: stats.range.to,
    kpis: [
      { label: 'Użytkownicy', value: formatCount(users), hint: `kandydaci ${formatCount(totals.users.candidates)}, pracodawcy ${formatCount(totals.users.employers)}, administratorzy ${formatCount(totals.users.admins)}` },
      { label: 'Firmy zatwierdzone', value: formatCount(totals.companies.approved), hint: `oczekujące ${formatCount(totals.companies.pending)}, odrzucone ${formatCount(totals.companies.rejected)}` },
      { label: 'Aktywne oferty', value: formatCount(totals.jobs.active), hint: `wszystkich ofert ${formatCount(jobs)}` },
      { label: 'Aplikacje (łącznie)', value: formatCount(totals.applications.total) },
      { label: 'Nowe aplikacje w zakresie', value: formatCount(sum(applications)) },
      { label: 'Wyświetlenia ofert w zakresie', value: formatCount(sum(views)) }
    ],
    statusBreakdown: statusBreakdown(totals.applications.byStatus),
    dates: series.map(point => point.date),
    series: [
      styled('newApplications', 'Nowe aplikacje', applications, 0),
      styled('jobViews', 'Wyświetlenia ofert', views, 1),
      styled('newJobs', 'Nowe oferty', series.map(point => point.newJobs), 2),
      styled('newUsers', 'Nowi użytkownicy', series.map(point => point.newUsers), 3)
    ],
    funnel: buildFunnelStages(stats.funnel),
    jobsTitle: 'Najpopularniejsze oferty w zakresie',
    jobColumns: [
      { key: 'title', label: 'Oferta', numeric: false },
      { key: 'company', label: 'Firma', numeric: false },
      { key: 'applications', label: 'Aplikacje', numeric: true },
      { key: 'views', label: 'Wyświetlenia', numeric: true }
    ],
    jobRows: stats.topJobs.map(job => ({
      jobId: job.jobId,
      title: job.title,
      company: job.company,
      statusLabel: '',
      views: job.views,
      applications: job.applications,
      conversion: job.views > 0 ? job.applications / job.views : 0
    })),
    medianHours: stats.medianFirstResponseHours,
    hasData: hasActivity(series.map(point => point.newApplications + point.jobViews + point.newJobs + point.newUsers), stats.funnel.submitted)
  };
}

export function toEmployerView(stats: EmployerStats): DashboardView {
  const { totals, series } = stats;
  const views = series.map(point => point.jobViews);
  const applications = series.map(point => point.newApplications);

  return {
    mode: 'employer',
    from: stats.range.from,
    to: stats.range.to,
    kpis: [
      { label: 'Aktywne oferty', value: formatCount(totals.jobs.active), hint: `szkice ${formatCount(totals.jobs.draft)}, wygasłe ${formatCount(totals.jobs.expired)}, zamknięte ${formatCount(totals.jobs.closed)}` },
      { label: 'Aplikacje (łącznie)', value: formatCount(totals.applications.total) },
      { label: 'Wyświetlenia ofert (łącznie)', value: formatCount(totals.views) },
      { label: 'Nowe aplikacje w zakresie', value: formatCount(sum(applications)) },
      { label: 'Wyświetlenia ofert w zakresie', value: formatCount(sum(views)) }
    ],
    statusBreakdown: statusBreakdown(totals.applications.byStatus),
    dates: series.map(point => point.date),
    series: [
      styled('newApplications', 'Nowe aplikacje', applications, 0),
      styled('jobViews', 'Wyświetlenia ofert', views, 1)
    ],
    funnel: buildFunnelStages(stats.funnel),
    jobsTitle: 'Oferty firmy',
    jobColumns: [
      { key: 'title', label: 'Oferta', numeric: false },
      { key: 'status', label: 'Status', numeric: false },
      { key: 'views', label: 'Wyświetlenia', numeric: true },
      { key: 'applications', label: 'Aplikacje', numeric: true },
      { key: 'conversion', label: 'Konwersja', numeric: true }
    ],
    jobRows: stats.jobs.map(job => ({
      jobId: job.jobId,
      title: job.title,
      company: '',
      statusLabel: JOB_STATE_PRESENTATION[getJobState(job)].label,
      views: job.views,
      applications: job.applications,
      conversion: job.conversionRate
    })),
    medianHours: stats.medianFirstResponseHours,
    hasData: hasActivity(series.map(point => point.newApplications + point.jobViews), stats.funnel.submitted)
  };
}

/** Opis serii dla czytników ekranu: zakres dat, sumy i dzień szczytu każdej serii. */
export function describeSeries(dates: readonly string[], series: readonly ChartSeries[]): string {
  if (dates.length === 0) return 'Wykres liniowy bez danych.';

  const parts = series.map(item => {
    const total = sum(item.values);
    const peak = Math.max(0, ...item.values);
    const peakDay = peak > 0 ? `, szczyt ${formatCount(peak)} dnia ${formatFullDay(dates[item.values.indexOf(peak)])}` : '';
    return `${item.label}: razem ${formatCount(total)}${peakDay}`;
  });
  return `Wykres liniowy od ${formatFullDay(dates[0])} do ${formatFullDay(dates[dates.length - 1])}. ${parts.join('. ')}.`;
}

export function describeFunnel(stages: readonly FunnelStage[]): string {
  const parts = stages.map(stage => `${stage.label}: ${formatCount(stage.count)} (${stage.percent.toLocaleString('pl-PL')}%)`);
  return `Lejek rekrutacji. ${parts.join('. ')}.`;
}
