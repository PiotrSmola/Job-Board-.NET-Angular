import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CompanyService } from './company.service';
import { CompanyParams } from '../models/company-params.model';
import { Company } from '../models/company.model';
import { PaginatedResult } from '../models/pagination.model';

describe('CompanyService', () => {
  let service: CompanyService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(CompanyService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  describe('getCompanies', () => {
    it('sends paging and default sorting without an empty search term', () => {
      service.getCompanies(new CompanyParams()).subscribe();

      const req = http.expectOne(r => r.url === '/api/companies');
      expect(req.request.method).toBe('GET');
      expect(req.request.params.keys().sort()).toEqual(['orderBy', 'pageNumber', 'pageSize']);
      expect(req.request.params.get('pageNumber')).toBe('1');
      expect(req.request.params.get('pageSize')).toBe('12');
      expect(req.request.params.get('orderBy')).toBe('nameAsc');
      req.flush([]);
    });

    it('sends the search term and order and parses the Pagination header', () => {
      const params = new CompanyParams();
      params.pageNumber = 2;
      params.searchTerm = 'rzeszów';
      params.orderBy = 'jobsDesc';
      let result: PaginatedResult<Company> | undefined;
      service.getCompanies(params).subscribe(r => (result = r));

      const req = http.expectOne(r => r.url === '/api/companies');
      expect(req.request.params.get('pageNumber')).toBe('2');
      expect(req.request.params.get('searchTerm')).toBe('rzeszów');
      expect(req.request.params.get('orderBy')).toBe('jobsDesc');
      req.flush([], { headers: { Pagination: JSON.stringify({ CurrentPage: 2, ItemsPerPage: 12, TotalItems: 20, TotalPages: 2 }) } });

      expect(result?.pagination.TotalItems).toBe(20);
    });
  });

  it('getCompany requests /api/companies/:id', () => {
    service.getCompany(3).subscribe();
    const req = http.expectOne('/api/companies/3');
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  it('getMyCompany requests /api/employer/company', () => {
    service.getMyCompany().subscribe();
    const req = http.expectOne('/api/employer/company');
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  it('updateMyCompany puts the company data to /api/employer/company', () => {
    const body = { name: 'Nowa Firma', description: 'Opis', website: null, location: null };
    service.updateMyCompany(body).subscribe();
    const req = http.expectOne('/api/employer/company');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(body);
    req.flush({});
  });
});
