export enum WorkType {
  Remote = 0,
  Hybrid = 1,
  Onsite = 2
}

/** Stan zapisany w bazie; „wygasła” to stan wyliczany (Active + expiresAt w przeszłości → isExpired). */
export enum JobOfferStatus {
  Draft = 0,
  Active = 1,
  Closed = 2
}

export interface JobOffer {
  id: number;
  title: string;
  /** Nazwa firmy (zgodność wsteczna) – identyfikator w companyId. */
  company: string;
  companyId: number;
  description: string;
  salaryMin: number | null;
  salaryMax: number | null;
  location: string;
  workType: WorkType;
  createdAt: Date;
  status: JobOfferStatus;
  /** Data ważności (UTC, ISO) albo null dla oferty bezterminowej. */
  expiresAt: string | null;
  isExpired: boolean;
}

export interface CreateJobOffer {
  title: string;
  /** Wymagane dla admina; dla pracodawcy API je ignoruje (zawsze jego firma). */
  companyId: number | null;
  description: string;
  salaryMin: number | null;
  salaryMax: number | null;
  location: string;
  workType: WorkType;
  status: JobOfferStatus;
  /** ISO 8601 w UTC albo null. */
  expiresAt: string | null;
}
