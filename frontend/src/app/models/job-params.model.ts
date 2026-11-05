import { WorkType } from './job-offer.model';

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
  workType: WorkType | null = null;
  minSalary: number | null = null;
  maxSalary: number | null = null;
  location = '';
  orderBy: JobOrderBy = 'newest';
}

/** Stan oferty w panelu admina: active = aktywna i niewygasła. */
export type JobState = 'draft' | 'active' | 'expired' | 'closed';

/** Parametry GET /api/jobs/manage – jak lista publiczna + filtr stanu (null = wszystkie). */
export class ManageJobParams extends JobParams {
  state: JobState | null = null;
}
