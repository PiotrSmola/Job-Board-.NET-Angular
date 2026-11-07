import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ViewportScroller } from '@angular/common';
import { Title } from '@angular/platform-browser';
import { Router, TitleStrategy, provideRouter } from '@angular/router';
import { LiveAnnouncer } from '@angular/cdk/a11y';
import { App } from './app';
import { AppTitleStrategy } from './services/app-title.strategy';
import { PageTitleService } from './services/page-title.service';
import { DYNAMIC_TITLE } from './utils/page-title';
import { makeJwtExpiringIn } from '../testing/jwt-helper';

vi.setConfig({ testTimeout: 20000 });

@Component({ template: '<h1>Strona</h1>' })
class PageComponent {}

describe('App', () => {
  let announce: ReturnType<typeof vi.fn>;
  let scrollToPosition: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    localStorage.clear();
    announce = vi.fn().mockResolvedValue(undefined);
    scrollToPosition = vi.fn();

    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([
          { path: 'jobs', title: 'Oferty pracy', component: PageComponent },
          { path: 'jobs/:id', title: 'Oferta pracy', data: { [DYNAMIC_TITLE]: true }, component: PageComponent },
          { path: 'companies', title: 'Firmy', component: PageComponent }
        ]),
        { provide: TitleStrategy, useClass: AppTitleStrategy },
        { provide: LiveAnnouncer, useValue: { announce } },
        { provide: ViewportScroller, useValue: { scrollToPosition } },
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    }).compileComponents();
  });

  afterEach(() => localStorage.clear());

  function create() {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  it('should create the app', () => {
    expect(create().fixture.componentInstance).toBeTruthy();
  });

  describe('landmarks', () => {
    it('renders the skip link first, the navbar inside <header> and the router outlet inside <main>', async () => {
      const { fixture, root } = create();
      await fixture.whenStable();

      const skip = root.firstElementChild as HTMLAnchorElement;
      expect(skip.classList).toContain('skip-link');
      expect(skip.textContent?.trim()).toBe('Przejdź do treści');
      expect(skip.getAttribute('href')).toBe('#main');

      expect(root.querySelector('header app-navbar')).not.toBeNull();
      const main = root.querySelector('main')!;
      expect(main.id).toBe('main');
      expect(main.getAttribute('tabindex')).toBe('-1');
      expect(main.querySelector('router-outlet')).not.toBeNull();
      expect(root.querySelector('.logo')?.textContent).toContain('Job Board');
    });

    it('the skip link moves focus to <main> without changing the URL', () => {
      const { root } = create();
      const skip = root.querySelector<HTMLAnchorElement>('.skip-link')!;
      const event = new MouseEvent('click', { bubbles: true, cancelable: true });

      skip.dispatchEvent(event);

      expect(event.defaultPrevented).toBe(true);
      expect(document.activeElement).toBe(root.querySelector('main'));
    });

    it('shows login and register links for an anonymous user', async () => {
      const { fixture, root } = create();
      await fixture.whenStable();

      expect(root.textContent).toContain('Logowanie');
      expect(root.textContent).toContain('Rejestracja');
    });
  });

  describe('after navigation', () => {
    async function setup() {
      const { fixture, root } = create();
      const router = TestBed.inject(Router);
      await router.navigateByUrl('/jobs');
      fixture.detectChanges();
      announce.mockClear();
      scrollToPosition.mockClear();
      return { fixture, root, router, main: root.querySelector('main')! };
    }

    afterEach(() => {
      (document.activeElement as HTMLElement | null)?.blur();
    });

    it('sets the page title with the application name', async () => {
      await setup();

      expect(TestBed.inject(Title).getTitle()).toBe('Oferty pracy – Job Board');
    });

    it('does not move focus or announce on the first navigation (page load)', async () => {
      const { fixture } = create();
      await TestBed.inject(Router).navigateByUrl('/jobs');

      expect(document.activeElement).toBe(document.body);
      expect(announce).not.toHaveBeenCalled();
      expect(scrollToPosition).not.toHaveBeenCalled();
    });

    it('moves focus to <main>, scrolls to the top and announces the new title on a page change', async () => {
      const { router, main } = await setup();
      const focus = vi.spyOn(main, 'focus');

      await router.navigateByUrl('/companies');

      expect(document.activeElement).toBe(main);
      expect(focus).toHaveBeenCalledWith({ preventScroll: true });
      expect(scrollToPosition).toHaveBeenCalledWith([0, 0]);
      expect(announce).toHaveBeenCalledWith('Firmy – Job Board', 'polite');
    });

    it('leaves focus and scroll alone when only the query parameters or the fragment change', async () => {
      const { router, root } = await setup();
      const filter = document.createElement('button');
      root.appendChild(filter);
      filter.focus();

      await router.navigateByUrl('/jobs?page=2&search=net');
      await router.navigateByUrl('/jobs?page=2#apply');

      expect(document.activeElement).toBe(filter);
      expect(scrollToPosition).not.toHaveBeenCalled();
      expect(announce).not.toHaveBeenCalled();
    });

    it('leaves the announcement of a data-dependent title to the view (PageTitleService)', async () => {
      const { router, main } = await setup();

      await router.navigateByUrl('/jobs/5');

      expect(document.activeElement).toBe(main);
      expect(TestBed.inject(Title).getTitle()).toBe('Oferta pracy – Job Board');
      expect(announce).not.toHaveBeenCalled();

      TestBed.inject(PageTitleService).set('Senior .NET Developer');

      expect(TestBed.inject(Title).getTitle()).toBe('Senior .NET Developer – Job Board');
      expect(announce).toHaveBeenCalledExactlyOnceWith('Senior .NET Developer – Job Board', 'polite');
    });
  });

  describe('e-mail confirmation banner', () => {
    function login(emailConfirmed: boolean | undefined): void {
      const token = makeJwtExpiringIn(3600);
      localStorage.setItem('jobboard_token', token);
      localStorage.setItem('jobboard_user', JSON.stringify({ email: 'jan@example.com', firstName: 'Jan', lastName: 'K', token, emailConfirmed }));
    }

    it('reserves its slot in the header from the first render and loads the banner lazily into it', async () => {
      login(false);
      const { fixture, root } = create();

      const slot = root.querySelector('.email-banner-slot');
      expect(slot).not.toBeNull();
      expect(slot?.parentElement?.tagName.toLowerCase()).toBe('header');
      expect(slot?.previousElementSibling?.tagName.toLowerCase()).toBe('app-navbar');

      await vi.waitFor(() => {
        fixture.detectChanges();
        expect(slot?.querySelector('app-email-confirmation-banner')).not.toBeNull();
      });
      expect(root.textContent).toContain('Potwierdź adres e-mail.');
    });

    it.each([true, undefined])('is not shown when emailConfirmed is %s', confirmed => {
      login(confirmed);
      const { root } = create();

      expect(root.querySelector('.email-banner-slot')).toBeNull();
    });
  });
});
