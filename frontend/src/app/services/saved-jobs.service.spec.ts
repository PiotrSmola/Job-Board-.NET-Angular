import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SavedJobsService } from './saved-jobs.service';
import { AuthService } from './auth.service';
import { UserDto } from '../models/user.model';
import { PaginatedResult } from '../models/pagination.model';
import { SavedJob } from '../models/account.model';
import { makeJob } from '../../testing/job-fixtures';

type Role = 'User' | 'Employer' | 'Admin';

describe('SavedJobsService', () => {
  let service: SavedJobsService;
  let http: HttpTestingController;
  let user: ReturnType<typeof signal<UserDto | null>>;
  let role: ReturnType<typeof signal<Role>>;

  function login(email = 'jan@example.com', as: Role = 'User', token = 't1'): void {
    role.set(as);
    user.set({ email, firstName: 'Jan', lastName: 'K', token });
    TestBed.tick();
  }

  function logout(): void {
    user.set(null);
    TestBed.tick();
  }

  function flushIds(ids: number[]): void {
    const req = http.expectOne('/api/saved-jobs/ids');
    expect(req.request.method).toBe('GET');
    req.flush(ids);
  }

  beforeEach(() => {
    user = signal<UserDto | null>(null);
    role = signal<Role>('User');
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: AuthService,
          useValue: {
            currentUser: user,
            isAdmin: () => user() !== null && role() === 'Admin',
            isEmployer: () => user() !== null && role() === 'Employer'
          }
        }
      ]
    });
    service = TestBed.inject(SavedJobsService);
    http = TestBed.inject(HttpTestingController);
    TestBed.tick();
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  describe('loading the saved ids', () => {
    it('does nothing for an anonymous visitor', () => {
      expect(service.enabled()).toBe(false);
      http.expectNone('/api/saved-jobs/ids');
      expect(service.loadState()).toBe('idle');
    });

    it('loads the ids once after a candidate logs in', () => {
      login();
      expect(service.loadState()).toBe('loading');
      flushIds([1, 5]);

      expect(service.loadState()).toBe('loaded');
      expect(service.isSaved(1)).toBe(true);
      expect(service.isSaved(5)).toBe(true);
      expect(service.isSaved(2)).toBe(false);

      service.ensureLoaded();
      TestBed.tick();
      http.expectNone('/api/saved-jobs/ids');
    });

    it('does not reload when the same user gets a new token (e.g. after changing the password)', () => {
      login('jan@example.com', 'User', 't1');
      flushIds([1]);

      login('jan@example.com', 'User', 't2');

      http.expectNone('/api/saved-jobs/ids');
      expect(service.isSaved(1)).toBe(true);
    });

    it.each(['Employer', 'Admin'] as Role[])('is disabled for the %s role (the API answers 403)', as => {
      login('boss@example.com', as);

      expect(service.enabled()).toBe(false);
      http.expectNone('/api/saved-jobs/ids');
    });

    it('clears the state on logout and cancels a pending load', () => {
      login();
      const req = http.expectOne('/api/saved-jobs/ids');

      logout();

      expect(req.cancelled).toBe(true);
      expect(service.savedIds().size).toBe(0);
      expect(service.loadState()).toBe('idle');
    });

    it('reloads for another user and drops the previous user ids', () => {
      login('a@example.com');
      flushIds([1]);

      login('b@example.com');

      expect(service.isSaved(1)).toBe(false);
      flushIds([9]);
      expect(service.isSaved(9)).toBe(true);
    });

    it('retries after a failed load when ensureLoaded is called', () => {
      login();
      http.expectOne('/api/saved-jobs/ids').flush(null, { status: 500, statusText: 'Server Error' });
      expect(service.loadState()).toBe('error');

      service.ensureLoaded();
      flushIds([3]);

      expect(service.isSaved(3)).toBe(true);
    });
  });

  describe('toggle', () => {
    beforeEach(() => {
      login();
      flushIds([1]);
    });

    it('saves optimistically with PUT and reports the new state', () => {
      const results: boolean[] = [];
      service.toggle(2).subscribe(saved => results.push(saved));

      expect(service.isSaved(2)).toBe(true);
      expect(service.isPending(2)).toBe(true);
      const req = http.expectOne('/api/saved-jobs/2');
      expect(req.request.method).toBe('PUT');
      req.flush(null, { status: 204, statusText: 'No Content' });

      expect(results).toEqual([true]);
      expect(service.isPending(2)).toBe(false);
      expect(service.isSaved(2)).toBe(true);
    });

    it('removes optimistically with DELETE', () => {
      service.toggle(1).subscribe();

      expect(service.isSaved(1)).toBe(false);
      const req = http.expectOne('/api/saved-jobs/1');
      expect(req.request.method).toBe('DELETE');
      req.flush(null, { status: 204, statusText: 'No Content' });
      expect(service.isSaved(1)).toBe(false);
    });

    it('rolls back and rethrows when the request fails (e.g. 404 for an offer no longer public)', () => {
      let error: unknown;
      service.toggle(2).subscribe({ error: e => (error = e) });
      http.expectOne('/api/saved-jobs/2').flush({ message: 'Job offer not found' }, { status: 404, statusText: 'Not Found' });

      expect(service.isSaved(2)).toBe(false);
      expect(service.isPending(2)).toBe(false);
      expect(error).toBeInstanceOf(HttpErrorResponse);
    });

    it('rolls back a failed removal', () => {
      service.toggle(1).subscribe({ error: () => undefined });
      http.expectOne('/api/saved-jobs/1').flush(null, { status: 500, statusText: 'Server Error' });

      expect(service.isSaved(1)).toBe(true);
    });

    it('ignores a second click while the first request is pending', () => {
      service.toggle(2).subscribe();
      let emitted = false;
      service.toggle(2).subscribe({ complete: () => (emitted = true) });

      expect(emitted).toBe(true); // EMPTY
      http.expectOne('/api/saved-jobs/2').flush(null);
    });

    it('finishes the request even when the caller unsubscribes (state stays consistent)', () => {
      const subscription = service.toggle(2).subscribe();
      subscription.unsubscribe();

      const req = http.expectOne('/api/saved-jobs/2');
      expect(req.cancelled).toBe(false);
      req.flush(null);
      expect(service.isSaved(2)).toBe(true);
      expect(service.isPending(2)).toBe(false);
    });

    it('does not let a late response of the previous session change the new one', () => {
      service.toggle(2).subscribe({ error: () => undefined });
      const req = http.expectOne('/api/saved-jobs/2');

      login('other@example.com');
      flushIds([]);
      req.flush(null, { status: 500, statusText: 'Server Error' });

      expect(service.isSaved(2)).toBe(false);
      expect(service.isPending(2)).toBe(false);
    });

    it('does nothing when the user is not a candidate', () => {
      logout();
      service.toggle(2).subscribe();
      http.expectNone('/api/saved-jobs/2');
    });
  });

  it('keeps a toggle clicked while the ids are still loading', () => {
    login();
    const idsReq = http.expectOne('/api/saved-jobs/ids');
    service.toggle(7).subscribe();
    const putReq = http.expectOne('/api/saved-jobs/7');

    idsReq.flush([1]);

    expect(service.isSaved(7)).toBe(true);
    expect(service.isSaved(1)).toBe(true);
    putReq.flush(null);
  });

  describe('getSavedJobs', () => {
    it('sends paging params and maps the Pagination header', () => {
      login();
      flushIds([]);
      let page: PaginatedResult<SavedJob> | undefined;
      service.getSavedJobs(2, 10).subscribe(p => (page = p));

      const req = http.expectOne(r => r.url === '/api/saved-jobs');
      expect(req.request.params.get('pageNumber')).toBe('2');
      expect(req.request.params.get('pageSize')).toBe('10');
      req.flush([{ savedAt: '2026-02-01T10:00:00Z', job: makeJob(4) }], {
        headers: { Pagination: JSON.stringify({ CurrentPage: 2, ItemsPerPage: 10, TotalItems: 11, TotalPages: 2 }) }
      });

      expect(page?.result[0].job.id).toBe(4);
      expect(page?.pagination.TotalItems).toBe(11);
      // oferty z listy zapisanych są zapisane – serduszko pełne także przed wczytaniem /ids
      expect(service.isSaved(4)).toBe(true);
    });
  });
});
