export enum CompanyStatus {
  Pending = 0,
  Approved = 1,
  Rejected = 2
}

export interface Company {
  id: number;
  name: string;
  description: string | null;
  website: string | null;
  location: string | null;
  /** Status, powód odrzucenia i data weryfikacji – API wypełnia tylko dla admina i członków firmy. */
  status?: CompanyStatus | null;
  rejectionReason?: string | null;
  createdAt: string;
  reviewedAt?: string | null;
  /** Liczba ofert widocznych publicznie. */
  activeJobsCount: number;
  /** Tylko w liście admina (GET /api/admin/companies). */
  membersCount?: number;
}

/** Body POST/PUT firmy (admin) i PUT /api/employer/company. */
export interface SaveCompany {
  name: string;
  description: string | null;
  website: string | null;
  location: string | null;
}

export interface RejectCompany {
  reason: string;
}
