import { Currency, SeniorityLevel, WorkType } from './job-offer.model';

export type JobOrderBy =
  | 'newest'
  | 'oldest'
  | 'salaryHigh'
  | 'salaryLow'
  | 'titleAsc'
  | 'titleDesc'
  | 'companyAsc'
  | 'companyDesc'
  | 'locationAsc'
  | 'locationDesc';

export class JobParams {
  pageNumber = 1;
  pageSize = 10;
  searchTerm = '';
  /** Tryby pracy (OR w obrębie filtra); pusta lista = wszystkie. */
  workTypes: WorkType[] = [];
  minSalary: number | null = null;
  maxSalary: number | null = null;
  location = '';
  /** Oferty jednej firmy (null = wszystkie). */
  companyId: number | null = null;
  /** Slug kategorii (null = wszystkie). */
  category: string | null = null;
  /** Poziomy doświadczenia (OR); pusta lista = wszystkie. */
  seniority: SeniorityLevel[] = [];
  /** Maska bitowa ContractTypes – pasuje oferta z którymkolwiek zaznaczonym typem (0 = bez filtra). */
  contractTypes = 0;
  /** Nazwy umiejętności – oferta musi mieć wszystkie. */
  skills: string[] = [];
  /** Waluta widełek (null = dowolna). */
  currency: Currency | null = null;
  orderBy: JobOrderBy = 'newest';
}

/** Stan oferty w panelu admina: active = aktywna i niewygasła. */
export type JobState = 'draft' | 'active' | 'expired' | 'closed';

/** Parametry GET /api/jobs/manage – jak lista publiczna + filtr stanu (null = wszystkie). */
export class ManageJobParams extends JobParams {
  state: JobState | null = null;
}
