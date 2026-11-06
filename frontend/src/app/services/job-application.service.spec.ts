import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { JobApplicationService } from './job-application.service';
import { ApplicationStatus, JobApplication } from '../models/job-application.model';
import { ApplicationParams } from '../models/application-params.model';
import { PaginatedResult } from '../models/pagination.model';

const APPLICATION = {
  id: 1,
  jobOfferId: 2,
  userId: 'u1',
  userEmail: 'jan@example.com',
  userFullName: 'Jan Kowalski',
  jobTitle: 'Programista',
  company: 'Acme',
  companyId: 1,
  note: null,
  appliedAt: '2026-01-01T10:00:00Z'
};

describe('JobApplicationService', () => {
  let service: JobApplicationService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(JobApplicationService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  describe('getAllApplications', () => {
    it('sends only paging and sorting params for default ApplicationParams', () => {
      service.getAllApplications(new ApplicationParams()).subscribe();

      const req = http.expectOne(r => r.url === '/api/jobapplications/all');
      expect(req.request.method).toBe('GET');
      expect(req.request.params.keys().sort()).toEqual(['orderBy', 'pageNumber', 'pageSize']);
      expect(req.request.params.get('pageNumber')).toBe('1');
      expect(req.request.params.get('pageSize')).toBe('10');
      expect(req.request.params.get('orderBy')).toBe('newest');
      req.flush([]);
    });

    it('sends searchTerm, jobOfferId and orderBy when provided', () => {
      const params = new ApplicationParams();
      params.pageNumber = 4;
      params.pageSize = 50;
      params.searchTerm = 'kowalski';
      params.jobOfferId = 12;
      params.orderBy = 'oldest';

      service.getAllApplications(params).subscribe();

      const req = http.expectOne(r => r.url === '/api/jobapplications/all');
      expect(req.request.params.get('pageNumber')).toBe('4');
      expect(req.request.params.get('pageSize')).toBe('50');
      expect(req.request.params.get('searchTerm')).toBe('kowalski');
      expect(req.request.params.get('jobOfferId')).toBe('12');
      expect(req.request.params.get('orderBy')).toBe('oldest');
      req.flush([]);
    });

    it('omits an empty searchTerm and a null jobOfferId', () => {
      const params = new ApplicationParams();
      params.searchTerm = '';
      params.jobOfferId = null;

      service.getAllApplications(params).subscribe();

      const req = http.expectOne(r => r.url === '/api/jobapplications/all');
      expect(req.request.params.has('searchTerm')).toBe(false);
      expect(req.request.params.has('jobOfferId')).toBe(false);
      req.flush([]);
    });

    it('maps the response body and Pagination header to a PaginatedResult', () => {
      let result: PaginatedResult<JobApplication> | undefined;
      service.getAllApplications(new ApplicationParams()).subscribe(r => (result = r));

      http.expectOne(r => r.url === '/api/jobapplications/all').flush([APPLICATION], {
        headers: { Pagination: JSON.stringify({ CurrentPage: 1, ItemsPerPage: 10, TotalItems: 1, TotalPages: 1 }) }
      });

      expect(result?.result.length).toBe(1);
      expect(result?.result[0].id).toBe(1);
      expect(result?.pagination).toEqual({ CurrentPage: 1, ItemsPerPage: 10, TotalItems: 1, TotalPages: 1 });
    });

    it('falls back to defaults when the Pagination header is missing', () => {
      const params = new ApplicationParams();
      params.pageSize = 25;
      let result: PaginatedResult<JobApplication> | undefined;
      service.getAllApplications(params).subscribe(r => (result = r));

      http.expectOne(r => r.url === '/api/jobapplications/all').flush([]);

      expect(result?.result).toEqual([]);
      expect(result?.pagination).toEqual({ CurrentPage: 1, ItemsPerPage: 25, TotalItems: 0, TotalPages: 1 });
    });
  });

  describe('other endpoints', () => {
    it('getMyApplications requests /my-applications', () => {
      service.getMyApplications().subscribe();
      const req = http.expectOne('/api/jobapplications/my-applications');
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });

    it('applyToJob posts the application', () => {
      const body = { jobOfferId: 3, note: 'Cześć' };
      service.applyToJob(body).subscribe();
      const req = http.expectOne('/api/jobapplications');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      req.flush(APPLICATION);
    });

    it('checkIfApplied requests /check/:id', () => {
      let result: { hasApplied: boolean } | undefined;
      service.checkIfApplied(5).subscribe(r => (result = r));
      const req = http.expectOne('/api/jobapplications/check/5');
      expect(req.request.method).toBe('GET');
      req.flush({ hasApplied: true });
      expect(result).toEqual({ hasApplied: true });
    });

    it('deleteApplication sends DELETE', () => {
      service.deleteApplication(8).subscribe();
      const req = http.expectOne('/api/jobapplications/8');
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
    });

    it('getApplication requests /:id', () => {
      service.getApplication(6).subscribe();
      const req = http.expectOne('/api/jobapplications/6');
      expect(req.request.method).toBe('GET');
      req.flush(APPLICATION);
    });

    it('updateStatus puts status and comment to /:id/status', () => {
      const body = { status: ApplicationStatus.Interview, comment: 'Zapraszamy' };
      service.updateStatus(6, body).subscribe();
      const req = http.expectOne('/api/jobapplications/6/status');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(body);
      req.flush(APPLICATION);
    });

    it('withdraw posts to /:id/withdraw', () => {
      service.withdraw(6).subscribe();
      const req = http.expectOne('/api/jobapplications/6/withdraw');
      expect(req.request.method).toBe('POST');
      req.flush(APPLICATION);
    });
  });

  describe('getAllApplications status filter', () => {
    it.each([
      [ApplicationStatus.Submitted, '0'],
      [ApplicationStatus.Rejected, '5']
    ])('sends status %s as a number', (status, expected) => {
      const params = new ApplicationParams();
      params.status = status;

      service.getAllApplications(params).subscribe();

      const req = http.expectOne(r => r.url === '/api/jobapplications/all');
      expect(req.request.params.get('status')).toBe(expected);
      req.flush([]);
    });

    it('omits a null status', () => {
      service.getAllApplications(new ApplicationParams()).subscribe();

      const req = http.expectOne(r => r.url === '/api/jobapplications/all');
      expect(req.request.params.has('status')).toBe(false);
      req.flush([]);
    });
  });
});
