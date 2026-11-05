import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { Company, SaveCompany } from '../models/company.model';
import { CompanyParams } from '../models/company-params.model';
import { PaginatedResult } from '../models/pagination.model';
import { toPaginatedResult } from '../utils/pagination';

/** Publiczne profile firm oraz firma zalogowanego pracodawcy. */
@Injectable({
  providedIn: 'root'
})
export class CompanyService {
  private http = inject(HttpClient);
  private readonly API_URL = '/api/companies';
  private readonly EMPLOYER_URL = '/api/employer/company';

  /** Publiczna lista – tylko firmy zatwierdzone. */
  getCompanies(companyParams: CompanyParams): Observable<PaginatedResult<Company>> {
    let params = new HttpParams()
      .append('pageNumber', companyParams.pageNumber.toString())
      .append('pageSize', companyParams.pageSize.toString())
      .append('orderBy', companyParams.orderBy);

    if (companyParams.searchTerm) {
      params = params.append('searchTerm', companyParams.searchTerm);
    }

    return this.http.get<Company[]>(this.API_URL, { params, observe: 'response' }).pipe(
      map(response => toPaginatedResult(response, companyParams.pageSize))
    );
  }

  /** Profil firmy – niezatwierdzona widoczna tylko dla admina i członków firmy (inaczej 404). */
  getCompany(id: number): Observable<Company> {
    return this.http.get<Company>(`${this.API_URL}/${id}`);
  }

  /** Firma zalogowanego pracodawcy (ze statusem i powodem odrzucenia). */
  getMyCompany(): Observable<Company> {
    return this.http.get<Company>(this.EMPLOYER_URL);
  }

  /** Edycja danych firmy; odrzucona firma wraca do weryfikacji. */
  updateMyCompany(company: SaveCompany): Observable<Company> {
    return this.http.put<Company>(this.EMPLOYER_URL, company);
  }
}
