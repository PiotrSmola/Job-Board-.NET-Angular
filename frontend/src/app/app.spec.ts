import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { makeJwtExpiringIn } from '../testing/jwt-helper';

describe('App', () => {
  beforeEach(async () => {
    localStorage.clear();

    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  afterEach(() => localStorage.clear());

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render the navbar and the router outlet', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('app-navbar')).not.toBeNull();
    expect(compiled.querySelector('router-outlet')).not.toBeNull();
    expect(compiled.querySelector('.logo')?.textContent).toContain('Job Board');
  });

  it('should show login and register links for an anonymous user', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Logowanie');
    expect(text).toContain('Rejestracja');
  });

  describe('e-mail confirmation banner', () => {
    function login(emailConfirmed: boolean | undefined): void {
      const token = makeJwtExpiringIn(3600);
      localStorage.setItem('jobboard_token', token);
      localStorage.setItem('jobboard_user', JSON.stringify({ email: 'jan@example.com', firstName: 'Jan', lastName: 'K', token, emailConfirmed }));
    }

    it('reserves its fixed-height slot from the first render and loads the banner lazily into it', async () => {
      login(false);
      const fixture = TestBed.createComponent(App);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;

      const slot = root.querySelector('.email-banner-slot');
      expect(slot).not.toBeNull();
      expect(slot?.previousElementSibling?.tagName.toLowerCase()).toBe('app-navbar');

      await vi.waitFor(() => {
        fixture.detectChanges();
        expect(slot?.querySelector('app-email-confirmation-banner')).not.toBeNull();
      });
      expect(root.textContent).toContain('Potwierdź adres e-mail.');
    });

    it.each([true, undefined])('is not shown when emailConfirmed is %s', confirmed => {
      login(confirmed);
      const fixture = TestBed.createComponent(App);
      fixture.detectChanges();

      expect((fixture.nativeElement as HTMLElement).querySelector('.email-banner-slot')).toBeNull();
    });
  });
});
