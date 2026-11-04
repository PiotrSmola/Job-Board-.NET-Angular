import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AuthService } from './auth.service';
import { UserDto } from '../models/user.model';
import { MS_ROLE_CLAIM, makeJwt, makeJwtExpiringIn } from '../../testing/jwt-helper';

@Component({ template: '' })
class DummyComponent {}

const TOKEN_KEY = 'jobboard_token';
const USER_KEY = 'jobboard_user';
const NOW = new Date('2026-01-01T12:00:00Z').getTime();
const SESSION_EXPIRED_MESSAGE = 'Sesja wygasła, zaloguj się ponownie';

function makeUser(token: string, overrides: Partial<UserDto> = {}): UserDto {
  return { email: 'jan@example.com', firstName: 'Jan', lastName: 'Kowalski', token, ...overrides };
}

function storeSession(user: UserDto): void {
  localStorage.setItem(TOKEN_KEY, user.token);
  localStorage.setItem(USER_KEY, JSON.stringify(user));
}

describe('AuthService', () => {
  let snackBarOpen: ReturnType<typeof vi.fn>;

  function setup(): { service: AuthService; router: Router; http: HttpTestingController; navigate: ReturnType<typeof vi.spyOn> } {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    return { service: TestBed.inject(AuthService), router, http: TestBed.inject(HttpTestingController), navigate };
  }

  beforeEach(() => {
    localStorage.clear();
    snackBarOpen = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'public', component: DummyComponent },
          { path: 'login', component: DummyComponent },
          { path: 'register', component: DummyComponent },
          { path: 'protected', component: DummyComponent, canActivate: [() => true] },
          { path: 'protected/:id', component: DummyComponent, canActivate: [() => true] }
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatSnackBar, useValue: { open: snackBarOpen } }
      ]
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    localStorage.clear();
  });

  describe('login / register / logout', () => {
    it('stores the session in localStorage and exposes the user after login', () => {
      const { service, http } = setup();
      const user = makeUser(makeJwtExpiringIn(3600));

      let result: UserDto | undefined;
      service.login({ email: user.email, password: 'secret1' }).subscribe(u => (result = u));

      const req = http.expectOne('/api/auth/login');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ email: user.email, password: 'secret1' });
      req.flush(user);

      expect(result).toEqual(user);
      expect(service.currentUser()).toEqual(user);
      expect(service.isAuthenticated()).toBe(true);
      expect(service.getToken()).toBe(user.token);
      expect(localStorage.getItem(TOKEN_KEY)).toBe(user.token);
      expect(JSON.parse(localStorage.getItem(USER_KEY)!)).toEqual(user);
    });

    it('does not store anything when login fails', () => {
      const { service, http } = setup();

      service.login({ email: 'a@b.c', password: 'bad-pass' }).subscribe({ error: () => undefined });
      http.expectOne('/api/auth/login').flush({ message: 'Invalid' }, { status: 401, statusText: 'Unauthorized' });

      expect(service.currentUser()).toBeNull();
      expect(localStorage.getItem(TOKEN_KEY)).toBeNull();
    });

    it('stores the session after register', () => {
      const { service, http } = setup();
      const user = makeUser(makeJwtExpiringIn(3600));

      service.register({ email: user.email, password: 'secret1', firstName: 'Jan', lastName: 'Kowalski' }).subscribe();
      http.expectOne('/api/auth/register').flush(user);

      expect(service.currentUser()).toEqual(user);
      expect(localStorage.getItem(TOKEN_KEY)).toBe(user.token);
    });

    it('logout clears the session, storage and navigates to /login', () => {
      storeSession(makeUser(makeJwtExpiringIn(3600)));
      const { service, navigate } = setup();
      expect(service.isAuthenticated()).toBe(true);

      service.logout();

      expect(service.currentUser()).toBeNull();
      expect(service.getToken()).toBeNull();
      expect(service.isAuthenticated()).toBe(false);
      expect(localStorage.getItem(TOKEN_KEY)).toBeNull();
      expect(localStorage.getItem(USER_KEY)).toBeNull();
      expect(navigate).toHaveBeenCalledWith(['/login']);
    });
  });

  describe('session restore on startup', () => {
    it('restores a valid session from localStorage', () => {
      const user = makeUser(makeJwtExpiringIn(3600));
      storeSession(user);

      const { service } = setup();

      expect(service.currentUser()).toEqual(user);
      expect(service.getToken()).toBe(user.token);
    });

    it('clears an expired session from localStorage', () => {
      storeSession(makeUser(makeJwtExpiringIn(-60)));

      const { service } = setup();

      expect(service.currentUser()).toBeNull();
      expect(service.isAuthenticated()).toBe(false);
      expect(localStorage.getItem(TOKEN_KEY)).toBeNull();
      expect(localStorage.getItem(USER_KEY)).toBeNull();
    });

    it('clears a session whose token has no readable exp', () => {
      storeSession(makeUser(makeJwt({ sub: '1' })));

      const { service } = setup();

      expect(service.currentUser()).toBeNull();
      expect(localStorage.getItem(TOKEN_KEY)).toBeNull();
    });

    it('clears the session when the user token differs from the stored token', () => {
      const user = makeUser(makeJwtExpiringIn(3600));
      localStorage.setItem(USER_KEY, JSON.stringify(user));
      localStorage.setItem(TOKEN_KEY, makeJwtExpiringIn(7200));

      const { service } = setup();

      expect(service.currentUser()).toBeNull();
      expect(localStorage.getItem(USER_KEY)).toBeNull();
    });

    it('clears a session when only one of the keys is present', () => {
      localStorage.setItem(TOKEN_KEY, makeJwtExpiringIn(3600));

      const { service } = setup();

      expect(service.currentUser()).toBeNull();
      expect(localStorage.getItem(TOKEN_KEY)).toBeNull();
    });

    it('treats corrupted JSON in localStorage as no session', () => {
      localStorage.setItem(USER_KEY, '{not json');
      localStorage.setItem(TOKEN_KEY, makeJwtExpiringIn(3600));

      const { service } = setup();

      expect(service.currentUser()).toBeNull();
      expect(localStorage.getItem(USER_KEY)).toBeNull();
      expect(localStorage.getItem(TOKEN_KEY)).toBeNull();
    });
  });

  describe('getToken / isAuthenticated', () => {
    it('does not return an expired token even though the session is still stored', () => {
      vi.useFakeTimers({ now: NOW });
      const user = makeUser(makeJwtExpiringIn(60, {}, NOW));
      storeSession(user);
      const { service } = setup();
      expect(service.getToken()).toBe(user.token);

      // Czas mija, ale timer jeszcze nie zadziałał (zmieniamy tylko zegar systemowy)
      vi.setSystemTime(NOW + 120_000);

      expect(service.getToken()).toBeNull();
      expect(service.isAuthenticated()).toBe(false);
    });

    it('returns null when there is no session', () => {
      const { service } = setup();
      expect(service.getToken()).toBeNull();
    });
  });

  describe('isAdmin', () => {
    it('is true for a token with the "role" claim equal to Admin', () => {
      storeSession(makeUser(makeJwtExpiringIn(3600, { role: 'Admin' })));
      expect(setup().service.isAdmin()).toBe(true);
    });

    it('is true for a token with the long Microsoft role claim', () => {
      storeSession(makeUser(makeJwtExpiringIn(3600, { [MS_ROLE_CLAIM]: ['User', 'Admin'] })));
      expect(setup().service.isAdmin()).toBe(true);
    });

    it('is false for a regular user', () => {
      storeSession(makeUser(makeJwtExpiringIn(3600, { role: 'User' })));
      expect(setup().service.isAdmin()).toBe(false);
    });

    it('is false for a token without roles', () => {
      storeSession(makeUser(makeJwtExpiringIn(3600)));
      expect(setup().service.isAdmin()).toBe(false);
    });

    it('is false when not logged in', () => {
      expect(setup().service.isAdmin()).toBe(false);
    });

    it('is false for an expired admin token', () => {
      vi.useFakeTimers({ now: NOW });
      storeSession(makeUser(makeJwtExpiringIn(60, { role: 'Admin' }, NOW)));
      const { service } = setup();
      expect(service.isAdmin()).toBe(true);

      vi.setSystemTime(NOW + 61_000);

      expect(service.isAdmin()).toBe(false);
    });
  });

  describe('auto logout timer', () => {
    it('clears the session and shows a notice when the token expires', async () => {
      vi.useFakeTimers({ now: NOW });
      storeSession(makeUser(makeJwtExpiringIn(60, {}, NOW)));
      const { service, navigate } = setup();
      expect(service.currentUser()).not.toBeNull();

      await vi.advanceTimersByTimeAsync(59_000);
      expect(service.currentUser()).not.toBeNull();
      expect(snackBarOpen).not.toHaveBeenCalled();

      await vi.advanceTimersByTimeAsync(1_000);

      expect(service.currentUser()).toBeNull();
      expect(localStorage.getItem(TOKEN_KEY)).toBeNull();
      expect(localStorage.getItem(USER_KEY)).toBeNull();
      expect(snackBarOpen).toHaveBeenCalledTimes(1);
      expect(snackBarOpen.mock.calls[0][0]).toBe(SESSION_EXPIRED_MESSAGE);
      // Domyślnie jesteśmy na trasie bez guarda ("/") – bez przekierowania
      expect(navigate).not.toHaveBeenCalled();
    });

    it('schedules the timer after login as well', async () => {
      vi.useFakeTimers({ now: NOW });
      const { service, http } = setup();
      const user = makeUser(makeJwtExpiringIn(30, {}, NOW));

      service.login({ email: user.email, password: 'secret1' }).subscribe();
      http.expectOne('/api/auth/login').flush(user);
      expect(service.currentUser()).not.toBeNull();

      await vi.advanceTimersByTimeAsync(30_000);

      expect(service.currentUser()).toBeNull();
      expect(snackBarOpen).toHaveBeenCalledTimes(1);
    });

    it('redirects to /login with returnUrl when expiry happens on a guarded route', async () => {
      const router = TestBed.inject(Router);
      await router.navigateByUrl('/protected/5?tab=2');
      vi.useFakeTimers({ now: NOW });
      storeSession(makeUser(makeJwtExpiringIn(10, {}, NOW)));
      const { navigate } = setup();

      await vi.advanceTimersByTimeAsync(10_000);

      expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/protected/5?tab=2' } });
    });

    it('does not redirect when expiry happens on a public route', async () => {
      const router = TestBed.inject(Router);
      await router.navigateByUrl('/public');
      vi.useFakeTimers({ now: NOW });
      storeSession(makeUser(makeJwtExpiringIn(10, {}, NOW)));
      const { service, navigate } = setup();

      await vi.advanceTimersByTimeAsync(10_000);

      expect(service.currentUser()).toBeNull();
      expect(snackBarOpen).toHaveBeenCalledTimes(1);
      expect(navigate).not.toHaveBeenCalled();
    });

    it('clears the timer on logout so no notice is shown later', async () => {
      vi.useFakeTimers({ now: NOW });
      storeSession(makeUser(makeJwtExpiringIn(60, {}, NOW)));
      const { service } = setup();

      service.logout();

      await vi.advanceTimersByTimeAsync(120_000);
      expect(snackBarOpen).not.toHaveBeenCalled();
    });

    it('replaces the previous timer when a new session is stored', async () => {
      vi.useFakeTimers({ now: NOW });
      storeSession(makeUser(makeJwtExpiringIn(60, {}, NOW)));
      const { service, http } = setup();

      const newUser = makeUser(makeJwtExpiringIn(600, {}, NOW), { firstName: 'Anna' });
      service.login({ email: newUser.email, password: 'secret1' }).subscribe();
      http.expectOne('/api/auth/login').flush(newUser);

      await vi.advanceTimersByTimeAsync(300_000);
      expect(service.currentUser()).toEqual(newUser);
      expect(snackBarOpen).not.toHaveBeenCalled();
    });

    it('clears the timer when the service is destroyed', async () => {
      vi.useFakeTimers({ now: NOW });
      storeSession(makeUser(makeJwtExpiringIn(60, {}, NOW)));
      setup();

      TestBed.resetTestingModule();

      await vi.advanceTimersByTimeAsync(120_000);
      expect(snackBarOpen).not.toHaveBeenCalled();
    });

    it('does not show any notice when there was no session', async () => {
      vi.useFakeTimers({ now: NOW });
      setup();

      await vi.advanceTimersByTimeAsync(120_000);
      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(vi.getTimerCount()).toBe(0);
    });

    it('re-schedules instead of logging out when the timer fires before the token is really expired', async () => {
      vi.useFakeTimers({ now: NOW });
      // Token ważny dłużej niż maksymalne opóźnienie setTimeout (~24,8 dnia)
      const thirtyDays = 30 * 24 * 3600;
      storeSession(makeUser(makeJwtExpiringIn(thirtyDays, {}, NOW)));
      const { service } = setup();

      await vi.advanceTimersByTimeAsync(2_147_483_647);
      expect(service.currentUser()).not.toBeNull();

      await vi.advanceTimersByTimeAsync(thirtyDays * 1000 - 2_147_483_647);
      expect(service.currentUser()).toBeNull();
      expect(snackBarOpen).toHaveBeenCalledTimes(1);
    });
  });

  describe('handleUnauthorized', () => {
    it('clears the session, shows a notice and redirects to login with the current URL', async () => {
      const router = TestBed.inject(Router);
      await router.navigateByUrl('/protected/7?x=1');
      storeSession(makeUser(makeJwtExpiringIn(3600)));
      const { service, navigate } = setup();

      service.handleUnauthorized();

      expect(service.currentUser()).toBeNull();
      expect(localStorage.getItem(TOKEN_KEY)).toBeNull();
      expect(snackBarOpen.mock.calls[0][0]).toBe(SESSION_EXPIRED_MESSAGE);
      expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/protected/7?x=1' } });
    });

    it.each(['/login', '/login?returnUrl=%2Fjobs', '/register'])(
      'only clears the session without notice or redirect on the auth page %s',
      async url => {
        const router = TestBed.inject(Router);
        await router.navigateByUrl(url);
        storeSession(makeUser(makeJwtExpiringIn(3600)));
        const { service, navigate } = setup();

        service.handleUnauthorized();

        expect(service.currentUser()).toBeNull();
        expect(snackBarOpen).not.toHaveBeenCalled();
        expect(navigate).not.toHaveBeenCalled();
      }
    );
  });
});
