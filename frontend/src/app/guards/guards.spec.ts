import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { adminGuard } from './admin.guard';
import { authGuard } from './auth.guard';
import { AuthService } from '../services/auth.service';
import { makeJwtExpiringIn } from '../../testing/jwt-helper';

describe('route guards', () => {
  let authenticated: boolean;
  let admin: boolean;
  let router: Router;

  function run(guard: typeof authGuard, url: string): boolean | UrlTree {
    const state = { url } as RouterStateSnapshot;
    return TestBed.runInInjectionContext(() => guard({} as ActivatedRouteSnapshot, state)) as boolean | UrlTree;
  }

  function serialize(result: boolean | UrlTree): string {
    expect(result).toBeInstanceOf(UrlTree);
    return router.serializeUrl(result as UrlTree);
  }

  beforeEach(() => {
    authenticated = false;
    admin = false;

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: { isAuthenticated: () => authenticated, isAdmin: () => admin }
        }
      ]
    });
    router = TestBed.inject(Router);
  });

  describe('authGuard', () => {
    it('allows an authenticated user', () => {
      authenticated = true;
      expect(run(authGuard, '/my-applications')).toBe(true);
    });

    it('redirects an anonymous user to /login with returnUrl', () => {
      expect(serialize(run(authGuard, '/my-applications'))).toBe('/login?returnUrl=%2Fmy-applications');
    });

    it('preserves the query string in returnUrl', () => {
      expect(serialize(run(authGuard, '/jobs/1?x=1&y=2'))).toBe('/login?returnUrl=%2Fjobs%2F1%3Fx%3D1%26y%3D2');
    });
  });

  describe('adminGuard', () => {
    it('allows an authenticated admin', () => {
      authenticated = true;
      admin = true;
      expect(run(adminGuard, '/admin')).toBe(true);
    });

    it('redirects an anonymous user to /login with returnUrl', () => {
      expect(serialize(run(adminGuard, '/admin'))).toBe('/login?returnUrl=%2Fadmin');
    });

    it('redirects an authenticated non-admin to the home page', () => {
      authenticated = true;
      admin = false;
      expect(serialize(run(adminGuard, '/admin'))).toBe('/');
    });
  });

  describe('with the real AuthService', () => {
    it('treats an expired stored session as anonymous', () => {
      TestBed.resetTestingModule();
      localStorage.clear();
      const token = makeJwtExpiringIn(-30, { role: 'Admin' });
      localStorage.setItem('jobboard_token', token);
      localStorage.setItem(
        'jobboard_user',
        JSON.stringify({ email: 'a@b.c', firstName: 'A', lastName: 'B', token })
      );
      TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient()] });
      router = TestBed.inject(Router);

      expect(serialize(run(adminGuard, '/admin'))).toBe('/login?returnUrl=%2Fadmin');
      localStorage.clear();
    });
  });
});
