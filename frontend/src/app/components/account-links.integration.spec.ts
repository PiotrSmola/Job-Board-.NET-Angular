import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ConfirmEmailComponent } from './confirm-email/confirm-email.component';
import { ResetPasswordComponent } from './reset-password/reset-password.component';
import { AuthService } from '../services/auth.service';
import { makeJwtExpiringIn } from '../../testing/jwt-helper';

vi.setConfig({ testTimeout: 20000 });

@Component({ template: '' })
class DummyComponent {}

/**
 * Linki z e-maili backendu przechodzą przez prawdziwy router Angulara (DefaultUrlSerializer): `/confirm-email?userId=…&token=…`
 * i `/reset-password?email=…&token=…`. Tokeny są Base64Url (A-Z a-z 0-9 - _), userId i e-mail kodowane `Uri.EscapeDataString`
 * (`+` jako %2B, `@` jako %40). Specy komponentów podstawiają `ActivatedRoute` z gotową mapą parametrów, więc tu sprawdzamy
 * sam odczyt linku i to, co komponent wysyła do API.
 */
describe('E-mail links read by the real router', () => {
  let http: HttpTestingController;
  let router: Router;
  let snackBarOpen: ReturnType<typeof vi.fn>;

  // token Base64Url jak z backendu (~270 znaków, z - i _)
  const BACKEND_TOKEN = 'Q2ZESjhCZ0xFLXRPVG5hYlVnYXdOb3dfVExVZV9NZy1xSkNwRzlwaExPN2lVLXhfTTU3OHhXMDVtTG1SWGZMZWRpb25UQjVUUGl2'
    + 'X1VCZDFXdFBSUS1lYkU2NHhNTGZtQUhDX1Z5UnFJRDY3Z0NHT2ZOV0p3QXlfZ2M0LXZ4MVBYUm5PcEE5QWxLd0dqUzNKcF9sdTR3ZUY3Z1ZtU1ZYdw';
  const USER_ID = 'fdab93be-9330-48f3-b54c-ec930fe754f2';

  beforeEach(() => {
    localStorage.clear();
    snackBarOpen = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'confirm-email', component: ConfirmEmailComponent },
          { path: 'reset-password', component: ResetPasswordComponent },
          { path: 'login', component: DummyComponent },
          { path: 'forgot-password', component: DummyComponent }
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatSnackBar, useValue: { open: snackBarOpen } }
      ]
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  function storeSession(userId: string, emailConfirmed: boolean): void {
    const token = makeJwtExpiringIn(3600, { nameid: userId });
    const user = { email: 'jan@example.com', firstName: 'Jan', lastName: 'Kowalski', token, emailConfirmed };
    localStorage.setItem('jobboard_token', token);
    localStorage.setItem('jobboard_user', JSON.stringify(user));
  }

  describe('/reset-password', () => {
    async function open(url: string): Promise<ResetPasswordComponent> {
      const harness = await RouterTestingHarness.create();
      return harness.navigateByUrl(url, ResetPasswordComponent);
    }

    function submit(component: ResetPasswordComponent, password = 'Nowe1234'): void {
      component.form.setValue({ newPassword: password, confirmPassword: password });
      component.submit();
    }

    it('reads an e-mail with a plus written as %2B and sends it back unchanged', async () => {
      const component = await open(`/reset-password?email=plus%2Btag.1%40test.local&token=${BACKEND_TOKEN}`);

      expect(component.linkValid()).toBe(true);
      expect(component.email).toBe('plus+tag.1@test.local');
      submit(component);

      const req = http.expectOne('/api/auth/reset-password');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ email: 'plus+tag.1@test.local', token: BACKEND_TOKEN, newPassword: 'Nowe1234' });
      req.flush(null, { status: 204, statusText: 'No Content' });
      await vi.waitFor(() => expect(router.url).toBe('/login'));
    });

    it('would read a raw "+" as a space - which is why the backend must write the plus as %2B', async () => {
      const component = await open(`/reset-password?email=plus+tag@test.local&token=${BACKEND_TOKEN}`);

      expect(component.email).toBe('plus tag@test.local');
    });

    it('passes a Base64Url token with "-" and "_" through untouched', async () => {
      expect(BACKEND_TOKEN).toMatch(/^[A-Za-z0-9_-]+$/);
      const component = await open(`/reset-password?email=jan%40example.com&token=${BACKEND_TOKEN}`);
      submit(component);

      expect(http.expectOne('/api/auth/reset-password').request.body.token).toBe(BACKEND_TOKEN);
    });

    it('still decodes a percent-encoded token written in the older style (%2B %2F %3D)', async () => {
      const component = await open('/reset-password?email=jan%40example.com&token=ab%2Bcd%2Fef%3D%3D');
      submit(component);

      expect(http.expectOne('/api/auth/reset-password').request.body.token).toBe('ab+cd/ef==');
    });

    it('trims the e-mail and keeps its case', async () => {
      const component = await open('/reset-password?email=%20Jan.Kowalski%40Example.COM%20&token=t');

      expect(component.email).toBe('Jan.Kowalski@Example.COM');
    });

    it('removes the query (token and e-mail) from the address bar and history', async () => {
      await open(`/reset-password?email=jan%40example.com&token=${BACKEND_TOKEN}`);

      expect(router.url).toBe('/reset-password');
    });

    it('ignores a fragment and unrelated parameters', async () => {
      const component = await open('/reset-password?utm_source=mail&email=jan%40example.com&token=t#top');
      submit(component);

      expect(http.expectOne('/api/auth/reset-password').request.body).toEqual({ email: 'jan@example.com', token: 't', newPassword: 'Nowe1234' });
    });

    it.each([
      ['no parameters', '/reset-password'],
      ['an empty token', '/reset-password?email=jan%40example.com&token='],
      ['an empty e-mail', '/reset-password?email=&token=t'],
      ['an e-mail without @', '/reset-password?email=jan&token=t'],
      ['an e-mail of only spaces', '/reset-password?email=%20%20&token=t'],
      ['a token over 2048 characters', `/reset-password?email=jan%40example.com&token=${'a'.repeat(2049)}`],
      ['an e-mail over 256 characters', `/reset-password?email=${'a'.repeat(250)}%40example.com&token=t`]
    ])('shows the invalid-link state and calls no API for %s', async (_name, url) => {
      const component = await open(url);

      expect(component.linkValid()).toBe(false);
      submit(component);
      http.expectNone('/api/auth/reset-password');
    });

    it('accepts a token of exactly 2048 characters and an e-mail of exactly 256 characters', async () => {
      const email = `${'a'.repeat(256 - '@example.com'.length)}@example.com`;
      const component = await open(`/reset-password?email=${encodeURIComponent(email)}&token=${'a'.repeat(2048)}`);

      expect(component.linkValid()).toBe(true);
      expect(component.email).toBe(email);
    });

    it('uses the first value when a parameter is repeated', async () => {
      const component = await open('/reset-password?email=first%40example.com&email=second%40example.com&token=t1&token=t2');
      submit(component);

      expect(http.expectOne('/api/auth/reset-password').request.body).toMatchObject({ email: 'first@example.com', token: 't1' });
    });
  });

  describe('/confirm-email', () => {
    async function open(url: string): Promise<ConfirmEmailComponent> {
      const harness = await RouterTestingHarness.create();
      return harness.navigateByUrl(url, ConfirmEmailComponent);
    }

    it('confirms automatically with the userId and the Base64Url token from the link', async () => {
      const component = await open(`/confirm-email?userId=${USER_ID}&token=${BACKEND_TOKEN}`);

      const req = http.expectOne('/api/auth/confirm-email');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ userId: USER_ID, token: BACKEND_TOKEN });
      expect(component.state()).toBe('confirming');
      req.flush(null, { status: 204, statusText: 'No Content' });
      expect(component.state()).toBe('confirmed');
      expect(router.url).toBe('/confirm-email');
    });

    it('hides the banner of the logged-in user whose "nameid" claim matches the userId in the link', async () => {
      storeSession(USER_ID, false);
      const auth = TestBed.inject(AuthService);
      expect(auth.emailConfirmationPending()).toBe(true);

      await open(`/confirm-email?userId=${USER_ID}&token=${BACKEND_TOKEN}`);
      http.expectOne('/api/auth/confirm-email').flush(null, { status: 204, statusText: 'No Content' });

      expect(auth.emailConfirmationPending()).toBe(false);
      expect(auth.currentUser()?.emailConfirmed).toBe(true);
    });

    it('keeps the banner of a different logged-in user', async () => {
      storeSession('00000000-0000-0000-0000-000000000000', false);
      const auth = TestBed.inject(AuthService);

      await open(`/confirm-email?userId=${USER_ID}&token=${BACKEND_TOKEN}`);
      http.expectOne('/api/auth/confirm-email').flush(null, { status: 204, statusText: 'No Content' });

      expect(auth.emailConfirmationPending()).toBe(true);
    });

    it('translates the answer for an invalid or expired link', async () => {
      const component = await open(`/confirm-email?userId=${USER_ID}&token=${BACKEND_TOKEN}`);

      http.expectOne('/api/auth/confirm-email')
        .flush({ message: 'Invalid or expired confirmation link' }, { status: 400, statusText: 'Bad Request' });

      expect(component.state()).toBe('error');
      expect(component.error()).toBe('Link potwierdzający jest nieprawidłowy lub wygasł');
    });

    it('decodes a percent-encoded token written in the older style', async () => {
      await open(`/confirm-email?userId=${USER_ID}&token=ab%2Bcd%2Fef%3D`);

      expect(http.expectOne('/api/auth/confirm-email').request.body.token).toBe('ab+cd/ef=');
    });

    it.each([
      ['no parameters', '/confirm-email'],
      ['an empty userId', '/confirm-email?userId=&token=t'],
      ['an empty token', `/confirm-email?userId=${USER_ID}&token=`],
      ['a userId over 128 characters', `/confirm-email?userId=${'a'.repeat(129)}&token=t`],
      ['a token over 2048 characters', `/confirm-email?userId=${USER_ID}&token=${'a'.repeat(2049)}`]
    ])('shows an error and calls no API for %s', async (_name, url) => {
      const component = await open(url);

      expect(component.state()).toBe('error');
      expect(component.error()).toBe('Link potwierdzający jest nieprawidłowy lub niekompletny');
      http.expectNone('/api/auth/confirm-email');
    });

    it('accepts a userId of exactly 128 characters and a token of exactly 2048 characters', async () => {
      await open(`/confirm-email?userId=${'a'.repeat(128)}&token=${'b'.repeat(2048)}`);

      const req = http.expectOne('/api/auth/confirm-email');
      expect(req.request.body.userId).toHaveLength(128);
      expect(req.request.body.token).toHaveLength(2048);
      req.flush(null, { status: 204, statusText: 'No Content' });
    });
  });
});
