import { CompanyStatus } from './company.model';

export type CompanyOrderBy = 'nameAsc' | 'nameDesc' | 'jobsDesc';

/** Parametry publicznej listy firm (GET /api/companies). */
export class CompanyParams {
  pageNumber = 1;
  pageSize = 12;
  searchTerm = '';
  orderBy: CompanyOrderBy = 'nameAsc';
}

export type AdminCompanyOrderBy = 'newest' | 'oldest' | 'nameAsc' | 'nameDesc';

/** Parametry listy firm w panelu admina (GET /api/admin/companies). */
export class AdminCompanyParams {
  pageNumber = 1;
  pageSize = 10;
  searchTerm = '';
  status: CompanyStatus | null = null;
  orderBy: AdminCompanyOrderBy = 'newest';
}
