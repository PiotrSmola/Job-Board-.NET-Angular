import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { JobService } from './job.service';
import { CreateJobOffer, JobOffer, JobOfferStatus, WorkType } from '../models/job-offer.model';
import { JobParams, ManageJobParams } from '../models/job-params.model';
import { PaginatedResult } from '../models/pagination.model';

const JOB: JobOffer = {
  id: 1,
  title: 'Programista .NET',
  company: 'Acme',
  description: 'Opis',
  salaryMin: 8000,
  salaryMax: 12000,
  location: 'Rzeszów',
  workType: WorkType.Remote,
  createdAt: new Date('2026-01-01'),
  status: JobOfferStatus.Active,
  expiresAt: null,
  isExpired: false
};

describe('JobService', () => {
  let service: JobService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(JobService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  describe('getJobs', () => {
    it('sends only paging and sorting params for default JobParams', () => {
      service.getJobs(new JobParams()).subscribe();

      const req = http.expectOne(r => r.url === '/api/jobs');
      expect(req.request.method).toBe('GET');
      expect(req.request.params.keys().sort()).toEqual(['orderBy', 'pageNumber', 'pageSize']);
      expect(req.request.params.get('pageNumber')).toBe('1');
      expect(req.request.params.get('pageSize')).toBe('10');
      expect(req.request.params.get('orderBy')).toBe('newest');
      req.flush([]);
    });

    it('sends all provided filters', () => {
      const params = new JobParams();
      params.pageNumber = 3;
      params.pageSize = 25;
      params.searchTerm = 'angular';
      params.workType = WorkType.Hybrid;
      params.minSalary = 5000;
      params.maxSalary = 15000;
      params.location = 'Kraków';
      params.orderBy = 'salaryHigh';

      service.getJobs(params).subscribe();

      const req = http.expectOne(r => r.url === '/api/jobs');
      expect(req.request.params.get('pageNumber')).toBe('3');
      expect(req.request.params.get('pageSize')).toBe('25');
      expect(req.request.params.get('searchTerm')).toBe('angular');
      expect(req.request.params.get('workType')).toBe('1');
      expect(req.request.params.get('minSalary')).toBe('5000');
      expect(req.request.params.get('maxSalary')).toBe('15000');
      expect(req.request.params.get('location')).toBe('Kraków');
      expect(req.request.params.get('orderBy')).toBe('salaryHigh');
      req.flush([]);
    });

    it('sends workType=0 (Remote) even though it is falsy', () => {
      const params = new JobParams();
      params.workType = WorkType.Remote;

      service.getJobs(params).subscribe();

      const req = http.expectOne(r => r.url === '/api/jobs');
      expect(req.request.params.get('workType')).toBe('0');
      req.flush([]);
    });

    it('omits empty search term, location and null filters', () => {
      const params = new JobParams();
      params.searchTerm = '';
      params.location = '';
      params.workType = null;
      params.minSalary = null;
      params.maxSalary = null;

      service.getJobs(params).subscribe();

      const req = http.expectOne(r => r.url === '/api/jobs');
      for (const key of ['searchTerm', 'location', 'workType', 'minSalary', 'maxSalary']) {
        expect(req.request.params.has(key)).toBe(false);
      }
      req.flush([]);
    });

    it('encodes special characters in the search term', () => {
      const params = new JobParams();
      params.searchTerm = 'C# & .NET';

      service.getJobs(params).subscribe();

      const req = http.expectOne(r => r.url === '/api/jobs');
      expect(req.request.params.get('searchTerm')).toBe('C# & .NET');
      expect(req.request.urlWithParams).toContain('searchTerm=C%23%20%26%20.NET');
      req.flush([]);
    });

    it('maps the response body and Pagination header to a PaginatedResult', () => {
      let result: PaginatedResult<JobOffer> | undefined;
      service.getJobs(new JobParams()).subscribe(r => (result = r));

      http.expectOne(r => r.url === '/api/jobs').flush([JOB], {
        headers: { Pagination: JSON.stringify({ CurrentPage: 2, ItemsPerPage: 10, TotalItems: 11, TotalPages: 2 }) }
      });

      expect(result?.result).toEqual([JOB]);
      expect(result?.pagination).toEqual({ CurrentPage: 2, ItemsPerPage: 10, TotalItems: 11, TotalPages: 2 });
    });

    it('falls back to defaults when the Pagination header is missing', () => {
      const params = new JobParams();
      params.pageSize = 5;
      let result: PaginatedResult<JobOffer> | undefined;
      service.getJobs(params).subscribe(r => (result = r));

      http.expectOne(r => r.url === '/api/jobs').flush([JOB]);

      expect(result?.pagination).toEqual({ CurrentPage: 1, ItemsPerPage: 5, TotalItems: 1, TotalPages: 1 });
    });
  });

  describe('CRUD', () => {
    const create: CreateJobOffer = {
      title: 'T', company: 'C', description: 'D', salaryMin: null, salaryMax: null, location: 'L', workType: WorkType.Onsite,
      status: JobOfferStatus.Draft, expiresAt: '2026-12-31T22:59:59.999Z'
    };

    it('getJob requests /api/jobs/:id', () => {
      service.getJob(7).subscribe();
      const req = http.expectOne('/api/jobs/7');
      expect(req.request.method).toBe('GET');
      req.flush(JOB);
    });

    it('createJob posts the offer', () => {
      service.createJob(create).subscribe();
      const req = http.expectOne('/api/jobs');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(create);
      req.flush(JOB);
    });

    it('updateJob puts the offer under its id', () => {
      service.updateJob(4, create).subscribe();
      const req = http.expectOne('/api/jobs/4');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(create);
      req.flush(JOB);
    });

    it('deleteJob sends DELETE', () => {
      service.deleteJob(9).subscribe();
      const req = http.expectOne('/api/jobs/9');
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
    });

    it('updateJobStatus patches /:id/status with the new status', () => {
      service.updateJobStatus(4, JobOfferStatus.Closed).subscribe();
      const req = http.expectOne('/api/jobs/4/status');
      expect(req.request.method).toBe('PATCH');
      expect(req.request.body).toEqual({ status: JobOfferStatus.Closed });
      req.flush(JOB);
    });
  });

  describe('getManagedJobs', () => {
    it('requests /api/jobs/manage without state for "all"', () => {
      service.getManagedJobs(new ManageJobParams()).subscribe();

      const req = http.expectOne(r => r.url === '/api/jobs/manage');
      expect(req.request.params.keys().sort()).toEqual(['orderBy', 'pageNumber', 'pageSize']);
      req.flush([]);
    });

    it('sends the state filter together with the regular filters', () => {
      const params = new ManageJobParams();
      params.state = 'expired';
      params.searchTerm = 'dev';

      service.getManagedJobs(params).subscribe();

      const req = http.expectOne(r => r.url === '/api/jobs/manage');
      expect(req.request.params.get('state')).toBe('expired');
      expect(req.request.params.get('searchTerm')).toBe('dev');
      req.flush([]);
    });
  });
});
