import { JobOffer } from './job-offer.model';

export enum ApplicationStatus {
  Submitted = 0,
  InReview = 1,
  Interview = 2,
  Offered = 3,
  Hired = 4,
  Rejected = 5,
  Withdrawn = 6
}

export interface ApplicationStatusHistoryEntry {
  status: ApplicationStatus;
  changedAt: string;
  /** Komentarz rekrutera – widoczny także dla kandydata. */
  comment: string | null;
  /** Autor zmiany – API wypełnia tylko dla admina i pracodawcy firmy oferty. */
  changedBy: string | null;
}

export interface JobApplication {
  id: number;
  jobOfferId: number;
  userId: string;
  userEmail: string;
  userFullName: string;
  jobTitle: string;
  /** Nazwa firmy oferty. */
  company: string;
  companyId: number;
  note: string | null;
  appliedAt: Date;
  status: ApplicationStatus;
  statusChangedAt: string;
  jobOffer?: JobOffer;
  /** Tylko w szczegółach aplikacji (GET /{id}) i odpowiedziach na zmianę statusu. */
  history?: ApplicationStatusHistoryEntry[] | null;
}

export interface CreateJobApplication {
  jobOfferId: number;
  note: string | null;
}

export interface UpdateApplicationStatus {
  status: ApplicationStatus;
  comment: string | null;
}

/** Odpowiedź GET /api/jobapplications/check/{jobId}. */
export interface ApplicationCheck {
  /** true, gdy istnieje aplikacja w statusie innym niż wycofana. */
  hasApplied: boolean;
  applicationId: number | null;
  status: ApplicationStatus | null;
}
