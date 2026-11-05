import { ApplicationStatus } from './job-application.model';

export type ApplicationOrderBy = 'newest' | 'oldest';

export class ApplicationParams {
  pageNumber = 1;
  pageSize = 10;
  searchTerm = '';
  jobOfferId: number | null = null;
  status: ApplicationStatus | null = null;
  orderBy: ApplicationOrderBy = 'newest';
}
