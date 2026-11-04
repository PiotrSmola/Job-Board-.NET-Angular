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
