// Dane testowe ofert pracy współdzielone przez specy komponentów
import { Currency, JobOffer, JobOfferStatus, SalaryPeriod, WorkType } from '../app/models/job-offer.model';
import { PaginatedResult } from '../app/models/pagination.model';

export function makeJob(id = 1, overrides: Partial<JobOffer> = {}): JobOffer {
  return {
    id,
    title: `Oferta ${id}`,
    company: 'Acme',
    companyId: 1,
    description: 'Opis oferty',
    salaryMin: 1000,
    salaryMax: 2000,
    location: 'Rzeszów',
    workType: WorkType.Remote,
    createdAt: new Date('2026-01-01'),
    status: JobOfferStatus.Active,
    expiresAt: null,
    isExpired: false,
    categoryId: null,
    categoryName: null,
    categorySlug: null,
    seniority: null,
    contractTypes: 0,
    salaryCurrency: Currency.PLN,
    salaryPeriod: SalaryPeriod.Month,
    skills: [],
    ...overrides
  };
}

/** Strona wyników: `items` ofert z łącznej liczby `totalItems`. */
export function jobPage(items: number, totalItems: number, currentPage = 1, pageSize = 10): PaginatedResult<JobOffer> {
  return {
    result: Array.from({ length: items }, (_, i) => makeJob(i + 1)),
    pagination: {
      CurrentPage: currentPage,
      ItemsPerPage: pageSize,
      TotalItems: totalItems,
      TotalPages: Math.max(1, Math.ceil(totalItems / pageSize))
    }
  };
}
