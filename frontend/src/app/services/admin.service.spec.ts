import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AdminService } from './admin.service';
import { SKIP_FORBIDDEN_NOTICE } from '../interceptors/auth.interceptor';
import { AdminCompanyParams } from '../models/company-params.model';
import { CompanyStatus } from '../models/company.model';

describe('AdminService', () => {
  let service: AdminService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AdminService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('getSettings requests /api/admin/settings', () => {
    service.getSettings().subscribe();
    const req = http.expectOne('/api/admin/settings');
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  it('refreshSeed posts to /api/admin/refresh-seed and silences the generic 403 notice', () => {
    service.refreshSeed().subscribe();
    const req = http.expectOne('/api/admin/refresh-seed');
    expect(req.request.method).toBe('POST');
    expect(req.request.context.get(SKIP_FORBIDDEN_NOTICE)).toBe(true);
    req.flush({ message: 'ok', timestamp: 'now' });
  });

  describe('companies', () => {
    const body = { name: 'Acme', description: null, website: 'https://acme.pl', location: 'Rzeszów' };

    it('getCompanies sends only paging and sorting params by default and reads the Pagination header', () => {
      let total: number | undefined;
      service.getCompanies(new AdminCompanyParams()).subscribe(r => (total = r.pagination.TotalItems));

      const req = http.expectOne(r => r.url === '/api/admin/companies');
      expect(req.request.method).toBe('GET');
      expect(req.request.params.keys().sort()).toEqual(['orderBy', 'pageNumber', 'pageSize']);
      expect(req.request.params.get('orderBy')).toBe('newest');
      req.flush([], { headers: { Pagination: JSON.stringify({ CurrentPage: 1, ItemsPerPage: 10, TotalItems: 3, TotalPages: 1 }) } });
      expect(total).toBe(3);
    });

    it('getCompanies sends searchTerm and status (including 0 = Pending)', () => {
      const params = new AdminCompanyParams();
      params.searchTerm = 'acme';
      params.status = CompanyStatus.Pending;
      params.orderBy = 'nameAsc';
      service.getCompanies(params).subscribe();

      const req = http.expectOne(r => r.url === '/api/admin/companies');
      expect(req.request.params.get('searchTerm')).toBe('acme');
      expect(req.request.params.get('status')).toBe('0');
      expect(req.request.params.get('orderBy')).toBe('nameAsc');
      req.flush([]);
    });

    it('createCompany posts to /api/admin/companies', () => {
      service.createCompany(body).subscribe();
      const req = http.expectOne('/api/admin/companies');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      req.flush({});
    });

    it('updateCompany puts to /api/admin/companies/:id', () => {
      service.updateCompany(4, body).subscribe();
      const req = http.expectOne('/api/admin/companies/4');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(body);
      req.flush({});
    });

    it('approveCompany posts to /approve', () => {
      service.approveCompany(4).subscribe();
      const req = http.expectOne('/api/admin/companies/4/approve');
      expect(req.request.method).toBe('POST');
      req.flush({});
    });

    it('rejectCompany posts the reason to /reject', () => {
      service.rejectCompany(4, 'Brak danych').subscribe();
      const req = http.expectOne('/api/admin/companies/4/reject');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ reason: 'Brak danych' });
      req.flush({});
    });

    it('deleteCompany sends DELETE /api/admin/companies/:id', () => {
      service.deleteCompany(4).subscribe();
      const req = http.expectOne('/api/admin/companies/4');
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
    });
  });
});
