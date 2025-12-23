import { WorkType } from './job-offer.model';

export class JobParams {
  pageNumber = 1;
  pageSize = 10;
  searchTerm = '';
  workType: WorkType | null = null;
  minSalary: number | null = null;
  maxSalary: number | null = null;
  location = '';
  orderBy = 'newest';
}
