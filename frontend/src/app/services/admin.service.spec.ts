import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AdminService } from './admin.service';
import { SKIP_FORBIDDEN_NOTICE } from '../interceptors/auth.interceptor';

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
});
