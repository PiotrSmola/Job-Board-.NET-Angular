import { JobOffer, JobOfferStatus } from '../models/job-offer.model';
import { ApplicationStatus } from '../models/job-application.model';
import { JobState } from '../models/job-params.model';
import { CompanyStatus } from '../models/company.model';

/** Kolor chipa statusu – klasy zdefiniowane w StatusChipComponent. */
export type StatusTone = 'neutral' | 'info' | 'accent' | 'warning' | 'success' | 'danger' | 'muted';

export interface StatusPresentation {
  label: string;
  tone: StatusTone;
}

// --- oferty ---

export const JOB_STATE_PRESENTATION: Record<JobState, StatusPresentation> = {
  draft: { label: 'Szkic', tone: 'neutral' },
  active: { label: 'Aktywna', tone: 'success' },
  expired: { label: 'Wygasła', tone: 'warning' },
  closed: { label: 'Zamknięta', tone: 'muted' }
};

export const JOB_STATE_OPTIONS: { value: JobState | null; label: string }[] = [
  { value: null, label: 'Wszystkie' },
  { value: 'draft', label: 'Szkice' },
  { value: 'active', label: 'Aktywne' },
  { value: 'expired', label: 'Wygasłe' },
  { value: 'closed', label: 'Zamknięte' }
];

/** Statusy do wyboru w formularzu oferty („wygasła” wynika z daty, nie wybiera się jej ręcznie). */
export const JOB_STATUS_OPTIONS: { value: JobOfferStatus; label: string }[] = [
  { value: JobOfferStatus.Draft, label: 'Szkic' },
  { value: JobOfferStatus.Active, label: 'Aktywna' },
  { value: JobOfferStatus.Closed, label: 'Zamknięta' }
];

/** Stan oferty z uwzględnieniem wygaśnięcia (flaga isExpired liczona przez API). */
export function getJobState(job: Pick<JobOffer, 'status' | 'isExpired'>): JobState {
  switch (job.status) {
    case JobOfferStatus.Draft: return 'draft';
    case JobOfferStatus.Closed: return 'closed';
    default: return job.isExpired ? 'expired' : 'active';
  }
}

export function getJobStatePresentation(job: Pick<JobOffer, 'status' | 'isExpired'>): StatusPresentation {
  return JOB_STATE_PRESENTATION[getJobState(job)];
}

// --- aplikacje ---

export const APPLICATION_STATUS_PRESENTATION: Record<ApplicationStatus, StatusPresentation> = {
  [ApplicationStatus.Submitted]: { label: 'Złożona', tone: 'neutral' },
  [ApplicationStatus.InReview]: { label: 'W analizie', tone: 'info' },
  [ApplicationStatus.Interview]: { label: 'Rozmowa', tone: 'accent' },
  [ApplicationStatus.Offered]: { label: 'Oferta', tone: 'warning' },
  [ApplicationStatus.Hired]: { label: 'Zatrudniony', tone: 'success' },
  [ApplicationStatus.Rejected]: { label: 'Odrzucona', tone: 'danger' },
  [ApplicationStatus.Withdrawn]: { label: 'Wycofana', tone: 'muted' }
};

const ALL_APPLICATION_STATUSES: ApplicationStatus[] = [
  ApplicationStatus.Submitted,
  ApplicationStatus.InReview,
  ApplicationStatus.Interview,
  ApplicationStatus.Offered,
  ApplicationStatus.Hired,
  ApplicationStatus.Rejected,
  ApplicationStatus.Withdrawn
];

/** Opcje filtra listy aplikacji (null = wszystkie). */
export const APPLICATION_STATUS_FILTER_OPTIONS: { value: ApplicationStatus | null; label: string }[] = [
  { value: null, label: 'Wszystkie' },
  ...ALL_APPLICATION_STATUSES.map(value => ({ value, label: APPLICATION_STATUS_PRESENTATION[value].label }))
];

/** Statusy, które może ustawić rekruter – „Wycofana” ustawia wyłącznie kandydat. */
export const RECRUITER_STATUS_OPTIONS: { value: ApplicationStatus; label: string }[] = ALL_APPLICATION_STATUSES
  .filter(value => value !== ApplicationStatus.Withdrawn)
  .map(value => ({ value, label: APPLICATION_STATUS_PRESENTATION[value].label }));

/** Statusy, z których kandydat może wycofać aplikację (zgodnie z API). */
export const WITHDRAWABLE_STATUSES: readonly ApplicationStatus[] = [
  ApplicationStatus.Submitted,
  ApplicationStatus.InReview,
  ApplicationStatus.Interview,
  ApplicationStatus.Offered
];

export function canWithdraw(status: ApplicationStatus): boolean {
  return WITHDRAWABLE_STATUSES.includes(status);
}

export function getApplicationStatusPresentation(status: ApplicationStatus): StatusPresentation {
  return APPLICATION_STATUS_PRESENTATION[status] ?? { label: 'Nieznany', tone: 'neutral' };
}

// --- firmy ---

export const COMPANY_STATUS_PRESENTATION: Record<CompanyStatus, StatusPresentation> = {
  [CompanyStatus.Pending]: { label: 'Oczekuje', tone: 'warning' },
  [CompanyStatus.Approved]: { label: 'Zatwierdzona', tone: 'success' },
  [CompanyStatus.Rejected]: { label: 'Odrzucona', tone: 'danger' }
};

/** Opcje filtra listy firm w panelu admina (null = wszystkie). */
export const COMPANY_STATUS_FILTER_OPTIONS: { value: CompanyStatus | null; label: string }[] = [
  { value: null, label: 'Wszystkie' },
  ...[CompanyStatus.Pending, CompanyStatus.Approved, CompanyStatus.Rejected]
    .map(value => ({ value, label: COMPANY_STATUS_PRESENTATION[value].label }))
];

export function getCompanyStatusPresentation(status: CompanyStatus): StatusPresentation {
  return COMPANY_STATUS_PRESENTATION[status] ?? { label: 'Nieznany', tone: 'neutral' };
}

// --- daty ważności ---

const DAY_MS = 24 * 60 * 60 * 1000;

/** Liczba dni kalendarzowych (czas lokalny) od dziś do daty; ujemna dla dat minionych. */
export function calendarDaysUntil(date: string | Date, now: Date = new Date()): number {
  const target = new Date(date);
  const startOfTarget = new Date(target.getFullYear(), target.getMonth(), target.getDate()).getTime();
  const startOfToday = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
  return Math.round((startOfTarget - startOfToday) / DAY_MS);
}

/**
 * Dyskretna informacja „wygasa za X dni” dla ofert wygasających w ciągu `withinDays` dni;
 * null, gdy oferta jest bezterminowa, odległa lub już wygasła.
 */
export function formatExpiresSoon(expiresAt: string | null, now: Date = new Date(), withinDays = 7): string | null {
  if (!expiresAt || new Date(expiresAt).getTime() <= now.getTime()) return null;

  const days = calendarDaysUntil(expiresAt, now);
  if (days > withinDays) return null;
  if (days <= 0) return 'Wygasa dziś';
  if (days === 1) return 'Wygasa jutro';
  return `Wygasa za ${days} dni`;
}
