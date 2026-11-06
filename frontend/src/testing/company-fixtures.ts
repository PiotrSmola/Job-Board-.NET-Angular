// Dane testowe firm i stron wyników współdzielone przez specy komponentów
import { Company, CompanyStatus } from '../app/models/company.model';
import { PaginatedResult } from '../app/models/pagination.model';

export function makeCompany(id = 1, overrides: Partial<Company> = {}): Company {
  return {
    id,
    name: `Firma ${id}`,
    description: 'Opis firmy',
    website: 'https://firma.example.com',
    location: 'Rzeszów',
    status: CompanyStatus.Approved,
    rejectionReason: null,
    createdAt: '2026-01-01T10:00:00Z',
    reviewedAt: null,
    activeJobsCount: 3,
    membersCount: 1,
    ...overrides
  };
}

/** Strona wyników: `items` firm z łącznej liczby `totalItems`. */
export function companyPage(items: number, totalItems: number, currentPage = 1, pageSize = 10): PaginatedResult<Company> {
  return {
    result: Array.from({ length: items }, (_, i) => makeCompany(i + 1)),
    pagination: {
      CurrentPage: currentPage,
      ItemsPerPage: pageSize,
      TotalItems: totalItems,
      TotalPages: Math.max(1, Math.ceil(totalItems / pageSize))
    }
  };
}
