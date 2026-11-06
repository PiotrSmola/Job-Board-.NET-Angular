import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { NavbarComponent } from './navbar.component';
import { AuthService } from '../../services/auth.service';
import { UserDto } from '../../models/user.model';
import { MS_ROLE_CLAIM, makeJwtExpiringIn } from '../../../testing/jwt-helper';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

type Role = 'Admin' | 'Employer' | 'User' | null;

describe('NavbarComponent', () => {
  let fixture: ComponentFixture<NavbarComponent>;

  // Prawdziwy AuthService czytający role z JWT; sesja odtwarzana z localStorage
  function create(role: Role, overrides: Partial<UserDto> = {}): void {
    localStorage.clear();
    if (role !== null) {
      const token = makeJwtExpiringIn(3600, { [MS_ROLE_CLAIM]: role });
      const user: UserDto = { email: 'jan@example.com', firstName: 'Jan', lastName: 'Kowalski', token, ...overrides };
      localStorage.setItem('jobboard_token', token);
      localStorage.setItem('jobboard_user', JSON.stringify(user));
    }

    TestBed.configureTestingModule({ imports: [NavbarComponent], providers: [provideRouter([])] });
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(NavbarComponent);
    fixture.detectChanges();
  }

  function links(): { text: string; href: string | null }[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('nav a')).map(a => ({
      text: a.textContent?.replace(/\s+/g, ' ').trim().replace(/^\S+ /, '') ?? '',
      href: a.getAttribute('href')
    }));
  }

  function linkTexts(): string[] {
    return links().map(l => l.text);
  }

  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it('always shows the "Oferty" and "Firmy" links pointing to /jobs and /companies', () => {
    create(null);

    expect(links().slice(0, 2)).toEqual([
      { text: 'Oferty', href: '/jobs' },
      { text: 'Firmy', href: '/companies' }
    ]);
  });

  it('the logo links to /jobs', () => {
    create(null);

    expect((fixture.nativeElement as HTMLElement).querySelector('.logo')?.getAttribute('routerLink')).toBe('/jobs');
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

    it('has no user menu', () => {
      create(null);

      expect((fixture.nativeElement as HTMLElement).querySelector('button[aria-haspopup="menu"]')).toBeNull();
    });
  });

  describe('candidate (role User)', () => {
    it('sees "Moje Aplikacje" and no admin or employer panel', () => {
      create('User');

      expect(linkTexts()).toEqual(['Oferty', 'Firmy', 'Moje Aplikacje']);
      expect(links()[2].href).toBe('/my-applications');
    });

    it('does not see Logowanie / Rejestracja', () => {
      create('User');

      expect(linkTexts()).not.toContain('Logowanie');
      expect(linkTexts()).not.toContain('Rejestracja');
    });
  });

  describe('employer (role Employer)', () => {
    it('sees "Panel pracodawcy" instead of "Moje Aplikacje"', () => {
      create('Employer');

      expect(linkTexts()).toEqual(['Oferty', 'Firmy', 'Panel pracodawcy']);
      expect(links()[2].href).toBe('/employer');
      expect(linkTexts()).not.toContain('Moje Aplikacje');
      expect(linkTexts()).not.toContain('Panel Admina');
    });
  });

  describe('admin (role Admin)', () => {
    it('sees "Panel Admina" and neither employer panel nor "Moje Aplikacje"', () => {
      create('Admin');

      expect(linkTexts()).toEqual(['Oferty', 'Firmy', 'Panel Admina']);
      expect(links()[2].href).toBe('/admin');
    });
  });

  describe('multiple roles', () => {
    it('shows only the admin panel for a token with Admin and Employer roles', () => {
      localStorage.clear();
      const token = makeJwtExpiringIn(3600, { [MS_ROLE_CLAIM]: ['Admin', 'Employer'] });
      const user: UserDto = { email: 'a@example.com', firstName: 'Ala', lastName: 'K', token };
      localStorage.setItem('jobboard_token', token);
      localStorage.setItem('jobboard_user', JSON.stringify(user));
      TestBed.configureTestingModule({ imports: [NavbarComponent], providers: [provideRouter([])] });
      fixture = TestBed.createComponent(NavbarComponent);
      fixture.detectChanges();

      expect(linkTexts()).toEqual(['Oferty', 'Firmy', 'Panel Admina']);
    });
  });

  describe('user menu', () => {
    it('shows the first name on the menu button', () => {
      create('Employer', { firstName: 'Marta' });

      const button = (fixture.nativeElement as HTMLElement).querySelector('nav button')!;
      expect(button.textContent).toContain('Marta');
    });

    it('shows the e-mail (disabled item) and logs out on "Wyloguj"', async () => {
      create('User', { email: 'jan@example.com' });
      const logout = vi.spyOn(TestBed.inject(AuthService), 'logout');
      (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('nav button')!.click();
      fixture.detectChanges();
      await fixture.whenStable();

      const items = Array.from(document.querySelectorAll<HTMLButtonElement>('button[mat-menu-item]'));
      expect(items[0].textContent).toContain('jan@example.com');
      expect(items[0].disabled).toBe(true);

      items.find(i => i.textContent?.includes('Wyloguj'))!.click();

      expect(logout).toHaveBeenCalledTimes(1);
    });
  });

  describe('account links in the user menu', () => {
    async function menuLinks(): Promise<{ text: string; href: string | null }[]> {
      (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('nav button')!.click();
      fixture.detectChanges();
      await fixture.whenStable();
      return Array.from(document.querySelectorAll<HTMLAnchorElement>('a[mat-menu-item]')).map(a => ({
        text: a.textContent?.replace(/s+/g, ' ').trim().replace(/^S+ /, '') ?? '',
        href: a.getAttribute('href')
      }));
    }

    it('offers "Moje konto" and "Zapisane oferty" to a candidate', async () => {
      create('User');

      expect(await menuLinks()).toEqual([
        { text: 'Moje konto', href: '/account' },
        { text: 'Zapisane oferty', href: '/saved-jobs' }
      ]);
    });

    it.each(['Employer', 'Admin'] as const)('offers only "Moje konto" to %s', async role => {
      create(role);

      expect(await menuLinks()).toEqual([{ text: 'Moje konto', href: '/account' }]);
    });
  });

  it('reacts to a logout: the logged-out links appear', () => {
    create('User');
    expect(linkTexts()).toContain('Moje Aplikacje');

    TestBed.inject(AuthService).logout();
    fixture.detectChanges();

    expect(linkTexts()).toEqual(['Oferty', 'Firmy', 'Logowanie', 'Rejestracja']);
  });
});
