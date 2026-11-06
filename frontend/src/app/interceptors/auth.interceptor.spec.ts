import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { SKIP_FORBIDDEN_NOTICE, authInterceptor } from './auth.interceptor';
import { AuthService } from '../services/auth.service';
import { UserDto } from '../models/user.model';
import { makeJwtExpiringIn } from '../../testing/jwt-helper';

@Component({ template: '' })
class DummyComponent {}

const TOKEN_KEY = 'jobboard_token';
const USER_KEY = 'jobboard_user';

function storeSession(token: string): void {
  const user: UserDto = { email: 'jan@example.com', firstName: 'Jan', lastName: 'Kowalski', token };
  localStorage.setItem(TOKEN_KEY, token);
  localStorage.setItem(USER_KEY, JSON.stringify(user));
}

describe('authInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let router: Router;
  let navigate: ReturnType<typeof vi.spyOn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;

  function init(): void {
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
  }

  beforeEach(() => {
    localStorage.clear();
    snackBarOpen = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'jobs/:id', component: DummyComponent },
          { path: 'my-applications', component: DummyComponent, canActivate: [() => true] },
          { path: 'login', component: DummyComponent }
        ]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: MatSnackBar, useValue: { open: snackBarOpen } }
      ]
    });
  });

  afterEach(() => {
    controller.verify();
    localStorage.clear();
  });

  describe('Authorization header', () => {
    it('adds a Bearer token to /api/ requests when the token is valid', () => {
      const token = makeJwtExpiringIn(3600);
      storeSession(token);
      init();

      http.get('/api/jobs').subscribe();

      const req = controller.expectOne('/api/jobs');
      expect(req.request.headers.get('Authorization')).toBe(`Bearer ${token}`);
      req.flush([]);
    });

    it('does not add the header when the user is not logged in', () => {
      init();

      http.get('/api/jobs').subscribe();

      const req = controller.expectOne('/api/jobs');
      expect(req.request.headers.has('Authorization')).toBe(false);
      req.flush([]);
    });

    it('does not add the header when the stored token has expired', () => {
      storeSession(makeJwtExpiringIn(-60));
      init();

      http.get('/api/jobs').subscribe();

      const req = controller.expectOne('/api/jobs');
      expect(req.request.headers.has('Authorization')).toBe(false);
      req.flush([]);
    });

    it.each([
      'https://external.example.com/api/jobs',
      '/assets/data.json',
      '/apiary/x', // prefiks musi być dokładnie "/api/"
      'api/jobs'
    ])('does not add the header to non-API request %s', url => {
      storeSession(makeJwtExpiringIn(3600));
      init();

      http.get(url).subscribe();

      const req = controller.expectOne(url);
      expect(req.request.headers.has('Authorization')).toBe(false);
      req.flush({});
    });
  });

  describe('401 handling', () => {
    it('logs out and redirects to /login with the current URL as returnUrl on a protected route', async () => {
      storeSession(makeJwtExpiringIn(3600));
      init();
      await router.navigateByUrl('/my-applications?x=1');
      const auth = TestBed.inject(AuthService);
      expect(auth.isAuthenticated()).toBe(true);

      let status: number | undefined;
      http.get('/api/jobapplications/my-applications').subscribe({ error: e => (status = e.status) });
      controller.expectOne('/api/jobapplications/my-applications')
        .flush({ message: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

      expect(status).toBe(401); // błąd jest propagowany do wywołującego
      expect(auth.currentUser()).toBeNull();
      expect(localStorage.getItem(TOKEN_KEY)).toBeNull();
      expect(snackBarOpen).toHaveBeenCalledTimes(1);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Sesja wygasła, zaloguj się ponownie');
      expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/my-applications?x=1' } });
    });

    it('logs out with a notice but stays on a public route', async () => {
      storeSession(makeJwtExpiringIn(3600));
      init();
      await router.navigateByUrl('/jobs/5');
      const auth = TestBed.inject(AuthService);

      http.get('/api/jobapplications/check/5').subscribe({ error: () => undefined });
      controller.expectOne('/api/jobapplications/check/5').flush({}, { status: 401, statusText: 'Unauthorized' });

      expect(auth.currentUser()).toBeNull();
      expect(snackBarOpen).toHaveBeenCalledTimes(1);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Sesja wygasła, zaloguj się ponownie');
      expect(navigate).not.toHaveBeenCalled();
    });

    it('ignores a late 401 for a request sent with the previous session token', async () => {
      const oldToken = makeJwtExpiringIn(3600, { sub: 'old' });
      storeSession(oldToken);
      init();
      await router.navigateByUrl('/jobs/5');
      const auth = TestBed.inject(AuthService);

      let status: number | undefined;
      http.get('/api/jobapplications/my-applications').subscribe({ error: e => (status = e.status) });
      const req = controller.expectOne('/api/jobapplications/my-applications');
      expect(req.request.headers.get('Authorization')).toBe(`Bearer ${oldToken}`);

      // W międzyczasie użytkownik loguje się ponownie (nowy token)
      const newToken = makeJwtExpiringIn(3600, { sub: 'new' });
      auth.login({ email: 'jan@example.com', password: 'secret1' }).subscribe();
      controller.expectOne('/api/auth/login')
        .flush({ email: 'jan@example.com', firstName: 'Jan', lastName: 'Kowalski', token: newToken });

      req.flush({ message: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

      expect(status).toBe(401);
      expect(auth.getToken()).toBe(newToken);
      expect(localStorage.getItem(TOKEN_KEY)).toBe(newToken);
      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(navigate).not.toHaveBeenCalled();
    });

    it('ignores a 401 for a request sent without a token even if the user logged in meanwhile', async () => {
      init();
      await router.navigateByUrl('/jobs/5');
      const auth = TestBed.inject(AuthService);

      http.get('/api/jobapplications/check/5').subscribe({ error: () => undefined });
      const req = controller.expectOne('/api/jobapplications/check/5');
      expect(req.request.headers.has('Authorization')).toBe(false);

      const newToken = makeJwtExpiringIn(3600);
      auth.login({ email: 'jan@example.com', password: 'secret1' }).subscribe();
      controller.expectOne('/api/auth/login')
        .flush({ email: 'jan@example.com', firstName: 'Jan', lastName: 'Kowalski', token: newToken });

      req.flush({}, { status: 401, statusText: 'Unauthorized' });

      expect(auth.getToken()).toBe(newToken);
      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(navigate).not.toHaveBeenCalled();
    });

    it.each(['/api/auth/login', '/api/auth/register', '/api/auth/login?x=1'])(
      'does not log out on 401 from %s',
      url => {
        storeSession(makeJwtExpiringIn(3600));
        init();
        const auth = TestBed.inject(AuthService);

        let status: number | undefined;
        http.post(url, {}).subscribe({ error: e => (status = e.status) });
        controller.expectOne(url).flush({ message: 'Bad credentials' }, { status: 401, statusText: 'Unauthorized' });

        expect(status).toBe(401);
        expect(auth.isAuthenticated()).toBe(true);
        expect(snackBarOpen).not.toHaveBeenCalled();
        expect(navigate).not.toHaveBeenCalled();
      }
    );

    it('does not log out on 401 from a non-API URL', () => {
      storeSession(makeJwtExpiringIn(3600));
      init();
      const auth = TestBed.inject(AuthService);

      http.get('https://external.example.com/data').subscribe({ error: () => undefined });
      controller.expectOne('https://external.example.com/data')
        .flush({}, { status: 401, statusText: 'Unauthorized' });

      expect(auth.isAuthenticated()).toBe(true);
      expect(snackBarOpen).not.toHaveBeenCalled();
    });
  });

  describe('403 handling', () => {
    it('shows a snackbar without logging out', () => {
      storeSession(makeJwtExpiringIn(3600));
      init();
      const auth = TestBed.inject(AuthService);

      let status: number | undefined;
      http.get('/api/admin/settings').subscribe({ error: e => (status = e.status) });
      controller.expectOne('/api/admin/settings').flush({}, { status: 403, statusText: 'Forbidden' });

      expect(status).toBe(403);
      expect(snackBarOpen).toHaveBeenCalledTimes(1);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Brak uprawnień');
      expect(auth.isAuthenticated()).toBe(true);
      expect(navigate).not.toHaveBeenCalled();
    });

    it('shows the translated reason for a known 403 message', () => {
      storeSession(makeJwtExpiringIn(3600));
      init();

      http.patch('/api/jobs/1/status', { status: 1 }).subscribe({ error: () => undefined });
      controller.expectOne('/api/jobs/1/status').flush(
        { message: 'Company must be approved before publishing offers' },
        { status: 403, statusText: 'Forbidden' }
      );

      expect(snackBarOpen).toHaveBeenCalledTimes(1);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Firma musi zostać zatwierdzona, zanim opublikujesz ofertę');
    });

    it('stays silent when SKIP_FORBIDDEN_NOTICE is set on the request context', () => {
      storeSession(makeJwtExpiringIn(3600));
      init();

      let status: number | undefined;
      http.post('/api/admin/refresh-seed', {}, { context: new HttpContext().set(SKIP_FORBIDDEN_NOTICE, true) })
        .subscribe({ error: e => (status = e.status) });
      controller.expectOne('/api/admin/refresh-seed').flush({}, { status: 403, statusText: 'Forbidden' });

      expect(status).toBe(403);
      expect(snackBarOpen).not.toHaveBeenCalled();
    });

    it('does not show the notice for 403 on auth endpoints', () => {
      init();

      http.post('/api/auth/login', {}).subscribe({ error: () => undefined });
      controller.expectOne('/api/auth/login').flush({}, { status: 403, statusText: 'Forbidden' });

      expect(snackBarOpen).not.toHaveBeenCalled();
    });
  });

  describe('other errors', () => {
    it.each([400, 404, 423, 500])('passes %i through without side effects', status => {
      storeSession(makeJwtExpiringIn(3600));
      init();
      const auth = TestBed.inject(AuthService);

      let received: number | undefined;
      http.get('/api/jobs/1').subscribe({ error: e => (received = e.status) });
      controller.expectOne('/api/jobs/1').flush({}, { status, statusText: 'Error' });

      expect(received).toBe(status);
      expect(auth.isAuthenticated()).toBe(true);
      expect(snackBarOpen).not.toHaveBeenCalled();
      expect(navigate).not.toHaveBeenCalled();
    });

    it('passes successful responses through', () => {
      init();

      let body: unknown;
      http.get('/api/jobs/1').subscribe(b => (body = b));
      controller.expectOne('/api/jobs/1').flush({ id: 1 });

      expect(body).toEqual({ id: 1 });
    });
  });

  describe('blob requests (file downloads)', () => {
    it('adds the Bearer token to blob requests', () => {
      const token = makeJwtExpiringIn(3600);
      storeSession(token);
      init();

      http.get('/api/jobapplications/1/cv', { responseType: 'blob' }).subscribe();

      const req = controller.expectOne('/api/jobapplications/1/cv');
      expect(req.request.headers.get('Authorization')).toBe(`Bearer ${token}`);
      req.flush(new Blob(['%PDF-']));
    });

    it('never sends the token to a blob URL outside /api', () => {
      storeSession(makeJwtExpiringIn(3600));
      init();

      http.get('https://files.example.com/cv.pdf', { responseType: 'blob' }).subscribe();

      const req = controller.expectOne('https://files.example.com/cv.pdf');
      expect(req.request.headers.has('Authorization')).toBe(false);
      req.flush(new Blob(['%PDF-']));
    });

    it('logs out on 401 for a blob request and passes the error on', () => {
      storeSession(makeJwtExpiringIn(3600));
      init();
      const auth = TestBed.inject(AuthService);

      let status: number | undefined;
      http.get('/api/jobapplications/1/cv', { responseType: 'blob' }).subscribe({ error: e => (status = e.status) });
      controller.expectOne('/api/jobapplications/1/cv').flush(new Blob([]), { status: 401, statusText: 'Unauthorized' });

      expect(status).toBe(401);
      expect(auth.isAuthenticated()).toBe(false);
    });
  });
});
