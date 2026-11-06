export enum WorkType {
  Remote = 0,
  Hybrid = 1,
  Onsite = 2
}

/** Poziom doświadczenia (liczby jak w API). */
export enum SeniorityLevel {
  Intern = 0,
  Junior = 1,
  Mid = 2,
  Senior = 3,
  Lead = 4,
  Manager = 5
}

/** Typy umów jako flagi bitowe – oferta może mieć kilka naraz (maska w `contractTypes`). */
export enum ContractTypes {
  None = 0,
  EmploymentContract = 1,
  B2B = 2,
  MandateContract = 4,
  SpecificTaskContract = 8,
  Internship = 16
}

export enum Currency {
  PLN = 0,
  EUR = 1,
  USD = 2,
  GBP = 3
}

export enum SalaryPeriod {
  Month = 0,
  Hour = 1,
  Year = 2
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
  categoryId: number | null;
  categoryName: string | null;
  categorySlug: string | null;
  seniority: SeniorityLevel | null;
  /** Maska bitowa ContractTypes (0 = nie podano). */
  contractTypes: number;
  salaryCurrency: Currency;
  salaryPeriod: SalaryPeriod;
  skills: string[];
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
  categoryId: number | null;
  seniority: SeniorityLevel | null;
  contractTypes: number;
  salaryCurrency: Currency;
  salaryPeriod: SalaryPeriod;
  /** 0–15 nazw (1–50 znaków po trim); nieznane API tworzy automatycznie. */
  skills: string[];
}
