import { ApplicationStatus } from './job-application.model';
import { JobOfferStatus } from './job-offer.model';

/** Zakres statystyk – dni w formacie YYYY-MM-DD (UTC), włącznie. */
export interface StatsRange {
  from: string;
  to: string;
}

export interface JobStatusCounts {
  draft: number;
  active: number;
  expired: number;
  closed: number;
}

export interface ApplicationStatusCount {
  status: ApplicationStatus;
  count: number;
}

export interface ApplicationTotals {
  total: number;
  byStatus: ApplicationStatusCount[];
}

/** Aplikacje złożone w zakresie, które osiągnęły dany etap (wg historii statusów). */
export interface StatsFunnel {
  submitted: number;
  inReview: number;
  interview: number;
  offered: number;
  hired: number;
}

/** Wspólna część punktu szeregu czasowego (każdy dzień zakresu, także zera). */
export interface StatsSeriesPoint {
  date: string;
  newApplications: number;
  jobViews: number;
}

export interface AdminSeriesPoint extends StatsSeriesPoint {
  newJobs: number;
  newUsers: number;
}

export interface AdminStats {
  range: StatsRange;
  totals: {
    users: { candidates: number; employers: number; admins: number };
    companies: { pending: number; approved: number; rejected: number };
    jobs: JobStatusCounts;
    applications: ApplicationTotals;
  };
  series: AdminSeriesPoint[];
  funnel: StatsFunnel;
  topJobs: AdminTopJob[];
  /** Mediana godzin od złożenia aplikacji do pierwszej zmiany statusu przez rekrutera; null bez danych. */
  medianFirstResponseHours: number | null;
}

export interface AdminTopJob {
  jobId: number;
  title: string;
  company: string;
  applications: number;
  views: number;
}

export interface EmployerStats {
  range: StatsRange;
  totals: {
    jobs: JobStatusCounts;
    applications: ApplicationTotals;
    views: number;
  };
  series: StatsSeriesPoint[];
  funnel: StatsFunnel;
  jobs: EmployerJobStat[];
  medianFirstResponseHours: number | null;
}

export interface EmployerJobStat {
  jobId: number;
  title: string;
  status: JobOfferStatus;
  isExpired: boolean;
  views: number;
  applications: number;
  /** applications / views (0, gdy views = 0). */
  conversionRate: number;
}

export type StatsMode = 'admin' | 'employer';
