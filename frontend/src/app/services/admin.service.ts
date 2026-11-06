import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { AdminSettings } from '../models/admin-settings.model';
import { Company, SaveCompany } from '../models/company.model';
import { AdminCompanyParams } from '../models/company-params.model';
import { Category, SaveCategory } from '../models/taxonomy.model';
import { PaginatedResult } from '../models/pagination.model';
import { SKIP_FORBIDDEN_NOTICE } from '../interceptors/auth.interceptor';
import { toPaginatedResult } from '../utils/pagination';

@Injectable({
  providedIn: 'root'
})
export class AdminService {
  private http = inject(HttpClient);
  private readonly API_URL = '/api/admin';
  private readonly COMPANIES_URL = '/api/admin/companies';
  private readonly CATEGORIES_URL = '/api/admin/categories';

  getSettings(): Observable<AdminSettings> {
    return this.http.get<AdminSettings>(`${this.API_URL}/settings`);
  }

  refreshSeed(): Observable<{ message: string; timestamp: string }> {
    // 403 (odświeżanie wyłączone) obsługuje komponent własnym komunikatem
    return this.http.post<{ message: string; timestamp: string }>(`${this.API_URL}/refresh-seed`, {}, {
      context: new HttpContext().set(SKIP_FORBIDDEN_NOTICE, true)
    });
  }

  // --- moderacja firm ---

  /** Wszystkie firmy (każdy status) z liczbą członków. */
  getCompanies(companyParams: AdminCompanyParams): Observable<PaginatedResult<Company>> {
    let params = new HttpParams()
      .append('pageNumber', companyParams.pageNumber.toString())
      .append('pageSize', companyParams.pageSize.toString())
      .append('orderBy', companyParams.orderBy);

    if (companyParams.searchTerm) {
      params = params.append('searchTerm', companyParams.searchTerm);
    }

    if (companyParams.status !== null) {
      params = params.append('status', companyParams.status.toString());
    }

    return this.http.get<Company[]>(this.COMPANIES_URL, { params, observe: 'response' }).pipe(
      map(response => toPaginatedResult(response, companyParams.pageSize))
    );
  }

  /** Firma dodana przez admina jest od razu zatwierdzona. */
  createCompany(company: SaveCompany): Observable<Company> {
    return this.http.post<Company>(this.COMPANIES_URL, company);
  }

  updateCompany(id: number, company: SaveCompany): Observable<Company> {
    return this.http.put<Company>(`${this.COMPANIES_URL}/${id}`, company);
  }

  approveCompany(id: number): Observable<Company> {
    return this.http.post<Company>(`${this.COMPANIES_URL}/${id}/approve`, {});
  }

  rejectCompany(id: number, reason: string): Observable<Company> {
    return this.http.post<Company>(`${this.COMPANIES_URL}/${id}/reject`, { reason });
  }

  /** 400, gdy firma ma oferty lub członków. */
  deleteCompany(id: number): Observable<void> {
    return this.http.delete<void>(`${this.COMPANIES_URL}/${id}`);
  }

  // --- kategorie ofert (lista: CategoryService, publiczny GET /api/categories) ---

  createCategory(category: SaveCategory): Observable<Category> {
    return this.http.post<Category>(this.CATEGORIES_URL, category);
  }

  updateCategory(id: number, category: SaveCategory): Observable<Category> {
    return this.http.put<Category>(`${this.CATEGORIES_URL}/${id}`, category);
  }

  /** Oferty usuniętej kategorii tracą kategorię (null). */
  deleteCategory(id: number): Observable<void> {
    return this.http.delete<void>(`${this.CATEGORIES_URL}/${id}`);
  }
}
