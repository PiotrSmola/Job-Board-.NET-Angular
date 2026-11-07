import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
import { NavbarComponent } from './navbar.component';
import { AuthService } from '../../services/auth.service';
import { UserDto } from '../../models/user.model';
import { MS_ROLE_CLAIM, makeJwtExpiringIn } from '../../../testing/jwt-helper';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

type Role = 'Admin' | 'Employer' | 'User' | null;

@Component({ template: '' })
class DummyComponent {}

describe('NavbarComponent', () => {
  let fixture: ComponentFixture<NavbarComponent>;

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function storeSession(roles: string | string[], overrides: Partial<UserDto> = {}): void {
    const token = makeJwtExpiringIn(3600, { [MS_ROLE_CLAIM]: roles });
    const user: UserDto = { email: 'jan@example.com', firstName: 'Jan', lastName: 'Kowalski', token, ...overrides };
    localStorage.setItem('jobboard_token', token);
    localStorage.setItem('jobboard_user', JSON.stringify(user));
  }

  // Prawdziwy AuthService czytający role z JWT; sesja odtwarzana z localStorage
  function create(role: Role, overrides: Partial<UserDto> = {}): void {
    localStorage.clear();
    if (role !== null) storeSession(role, overrides);

    TestBed.configureTestingModule({
      imports: [NavbarComponent],
      providers: [provideRouter([{ path: '**', component: DummyComponent }])]
    });
    fixture = TestBed.createComponent(NavbarComponent);
    fixture.detectChanges();
  }

  /** Linki nawigacji z dostępną nazwą (etykieta – ikony są ukryte przed czytnikami). */
  function links(): { text: string; href: string | null }[] {
    return Array.from(root().querySelectorAll<HTMLAnchorElement>('nav a')).map(a => ({
      text: a.querySelector('.nav-label')?.textContent?.trim() ?? '',
      href: a.getAttribute('href')
    }));
  }

  function linkTexts(): string[] {
    return links().map(l => l.text);
  }

  /** Tekst przycisku bez elementów aria-hidden (przybliżenie nazwy dostępnej z treści). */
  function accessibleText(element: HTMLElement): string {
    const clone = element.cloneNode(true) as HTMLElement;
    clone.querySelectorAll('[aria-hidden="true"]').forEach(node => node.remove());
    return (clone.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  function accountButton(): HTMLButtonElement | null {
    return root().querySelector<HTMLButtonElement>('button[aria-haspopup="menu"]');
  }

  async function openMenu(): Promise<void> {
    accountButton()!.click();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it('is a labelled navigation landmark', () => {
    create(null);

    expect(root().querySelector('nav')?.getAttribute('aria-label')).toBe('Główna nawigacja');
  });

  it('always shows the "Oferty" and "Firmy" links pointing to /jobs and /companies', () => {
    create(null);

    expect(links().slice(0, 2)).toEqual([
      { text: 'Oferty', href: '/jobs' },
      { text: 'Firmy', href: '/companies' }
    ]);
  });

  it('the logo is a real link to /jobs outside the navigation list', () => {
    create(null);

    const logo = root().querySelector<HTMLAnchorElement>('a.logo')!;
    expect(logo.getAttribute('href')).toBe('/jobs');
    expect(logo.textContent?.trim()).toBe('Job Board');
    expect(logo.closest('nav')).toBeNull();
  });

  it('keeps every link label in the DOM (visually hidden on narrow screens, never removed) and hides the icons', () => {
    create('User');

    for (const link of Array.from(root().querySelectorAll('nav a'))) {
      expect(link.querySelector('.nav-label')?.textContent?.trim()).not.toBe('');
      expect(link.querySelector('mat-icon')?.getAttribute('aria-hidden')).toBe('true');
    }
  });

  it('marks the link of the current page with aria-current="page"', async () => {
    create(null);
    await TestBed.inject(Router).navigateByUrl('/companies');
    fixture.detectChanges();

    const current = Array.from(root().querySelectorAll('nav a[aria-current="page"]'));
    expect(current.map(a => a.querySelector('.nav-label')?.textContent?.trim())).toEqual(['Firmy']);
    expect(current[0].classList).toContain('active');
  });

  describe('anonymous visitor', () => {
    it('sees Logowanie and Rejestracja, and no panel or applications links', () => {
      create(null);

      expect(linkTexts()).toEqual(['Oferty', 'Firmy', 'Logowanie', 'Rejestracja']);
      expect(links().slice(2)).toEqual([
        { text: 'Logowanie', href: '/login' },
        { text: 'Rejestracja', href: '/register' }
      ]);
    });

    it('has no account menu', () => {
      create(null);

      expect(accountButton()).toBeNull();
    });
  });

  describe('candidate (role User)', () => {
    it('sees "Moje aplikacje" and "Zapisane", and no admin or employer panel', () => {
      create('User');

      expect(links()).toEqual([
        { text: 'Oferty', href: '/jobs' },
        { text: 'Firmy', href: '/companies' },
        { text: 'Moje aplikacje', href: '/my-applications' },
        { text: 'Zapisane', href: '/saved-jobs' }
      ]);
    });

    it('does not see Logowanie / Rejestracja', () => {
      create('User');

      expect(linkTexts()).not.toContain('Logowanie');
      expect(linkTexts()).not.toContain('Rejestracja');
    });
  });

  describe('employer (role Employer)', () => {
    it('sees "Panel pracodawcy" instead of the candidate links', () => {
      create('Employer');

      expect(linkTexts()).toEqual(['Oferty', 'Firmy', 'Panel pracodawcy']);
      expect(links()[2].href).toBe('/employer');
    });
  });

  describe('admin (role Admin)', () => {
    it('sees "Panel admina" and neither employer panel nor candidate links', () => {
      create('Admin');

      expect(linkTexts()).toEqual(['Oferty', 'Firmy', 'Panel admina']);
      expect(links()[2].href).toBe('/admin');
    });
  });

  describe('multiple roles', () => {
    it('shows only the admin panel for a token with Admin and Employer roles', () => {
      localStorage.clear();
      storeSession(['Admin', 'Employer'], { email: 'a@example.com', firstName: 'Ala', lastName: 'K' });
      TestBed.configureTestingModule({ imports: [NavbarComponent], providers: [provideRouter([])] });
      fixture = TestBed.createComponent(NavbarComponent);
      fixture.detectChanges();

      expect(linkTexts()).toEqual(['Oferty', 'Firmy', 'Panel admina']);
    });
  });

  describe('account menu', () => {
    it('shows the initials and the first name; the accessible name contains the visible name (WCAG 2.5.3)', () => {
      create('Employer', { firstName: 'Marta', lastName: 'Nowak' });

      const button = accountButton()!;
      expect(button.querySelector('.avatar')?.textContent?.trim()).toBe('MN');
      expect(button.querySelector('.avatar')?.getAttribute('aria-hidden')).toBe('true');
      expect(button.querySelector('.account-name')?.textContent?.trim()).toBe('Marta');
      // nazwa dostępna z treści: ukryty prefiks + widoczne imię + ukryte nazwisko; bez inicjałów i ligatury ikony
      expect(button.hasAttribute('aria-label')).toBe(false);
      expect(button.querySelector('.chevron')?.getAttribute('aria-hidden')).toBe('true');
      expect(accessibleText(button)).toBe('Menu konta: Marta Nowak');
      expect(button.getAttribute('aria-expanded')).toBe('false');
    });

    it('falls back to the first letter of the e-mail without first and last name', () => {
      create('User', { firstName: '', lastName: '', email: 'zofia@example.com' });

      expect(accountButton()!.querySelector('.avatar')?.textContent?.trim()).toBe('Z');
      expect(accessibleText(accountButton()!)).toBe('Menu konta: zofia@example.com');
    });

    it('shows the e-mail (disabled item) and logs out on "Wyloguj"', async () => {
      create('User', { email: 'jan@example.com' });
      const logout = vi.spyOn(TestBed.inject(AuthService), 'logout').mockImplementation(() => undefined);
      await openMenu();

      expect(accountButton()!.getAttribute('aria-expanded')).toBe('true');
      const items = Array.from(document.querySelectorAll<HTMLButtonElement>('button[mat-menu-item]'));
      expect(items[0].textContent).toContain('jan@example.com');
      expect(items[0].disabled).toBe(true);

      items.find(i => i.textContent?.includes('Wyloguj'))!.click();

      expect(logout).toHaveBeenCalledTimes(1);
    });

    it.each(['User', 'Employer', 'Admin'] as const)('offers "Moje konto" to %s', async role => {
      create(role);
      await openMenu();

      const menuLinks = Array.from(document.querySelectorAll<HTMLAnchorElement>('a[mat-menu-item]')).map(a => ({
        text: a.textContent?.replace(/\s+/g, ' ').trim().replace(/^\S+ /, '') ?? '',
        href: a.getAttribute('href')
      }));
      expect(menuLinks).toEqual([{ text: 'Moje konto', href: '/account' }]);
    });
  });

  it('reacts to a logout: the logged-out links appear', () => {
    create('User');
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    expect(linkTexts()).toContain('Moje aplikacje');

    TestBed.inject(AuthService).logout();
    fixture.detectChanges();

    expect(linkTexts()).toEqual(['Oferty', 'Firmy', 'Logowanie', 'Rejestracja']);
    expect(accountButton()).toBeNull();
  });
});
