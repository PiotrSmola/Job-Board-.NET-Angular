import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { AccountComponent } from './account.component';
import { AuthService } from '../../services/auth.service';
import { ProfileTabComponent } from './profile-tab/profile-tab.component';
import { DocumentsTabComponent } from './documents-tab/documents-tab.component';
import { SecurityTabComponent } from './security-tab/security-tab.component';
import { NotificationsTabComponent } from './notifications-tab/notifications-tab.component';

vi.setConfig({ testTimeout: 20000 });

@Component({ selector: 'app-profile-tab', standalone: true, template: 'profil' })
class FakeProfileTab {}
@Component({ selector: 'app-documents-tab', standalone: true, template: 'dokumenty' })
class FakeDocumentsTab {}
@Component({ selector: 'app-security-tab', standalone: true, template: 'bezpieczeństwo' })
class FakeSecurityTab {}
@Component({ selector: 'app-notifications-tab', standalone: true, template: 'powiadomienia' })
class FakeNotificationsTab {}

describe('AccountComponent', () => {
  let fixture: ComponentFixture<AccountComponent>;
  let queryParams: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let navigateByUrl: ReturnType<typeof vi.spyOn>;
  let role: 'User' | 'Employer' | 'Admin';

  async function create(tab: string | null = null): Promise<void> {
    queryParams = new BehaviorSubject(convertToParamMap(tab ? { tab } : {}));
    TestBed.overrideProvider(ActivatedRoute, { useValue: { queryParamMap: queryParams } });
    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(AccountComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function links(): HTMLAnchorElement[] {
    return Array.from(root().querySelectorAll<HTMLAnchorElement>('nav[aria-label="Sekcje konta"] a'));
  }

  function current(): string | undefined {
    return links().find(link => link.getAttribute('aria-current') === 'true')?.textContent?.trim();
  }

  beforeEach(() => {
    role = 'User';
    TestBed.configureTestingModule({
      imports: [AccountComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isAdmin: () => role === 'Admin', isEmployer: () => role === 'Employer' } }
      ]
    });
    TestBed.overrideComponent(AccountComponent, {
      remove: { imports: [ProfileTabComponent, DocumentsTabComponent, SecurityTabComponent, NotificationsTabComponent] },
      add: { imports: [FakeProfileTab, FakeDocumentsTab, FakeSecurityTab, FakeNotificationsTab] }
    });
  });

  afterEach(() => vi.restoreAllMocks());

  it('renders all sections on one page with headings and a section menu (profile current)', async () => {
    await create();

    expect(root().querySelector('h1')?.textContent).toBe('Moje konto');
    expect(links().map(link => link.textContent?.trim())).toEqual(['Profil', 'Dokumenty', 'Hasło i bezpieczeństwo', 'Powiadomienia']);
    expect(Array.from(root().querySelectorAll('section h2')).map(h => h.textContent?.trim()))
      .toEqual(['Profil', 'Dokumenty', 'Hasło i bezpieczeństwo', 'Powiadomienia']);
    expect(root().querySelector('#dokumenty')?.textContent).toContain('dokumenty');
    expect(root().querySelector('#dokumenty')?.getAttribute('aria-labelledby')).toBe('dokumenty-heading');
    expect(current()).toBe('Profil');
    // pierwsze wejście bez ?tab= nie przejmuje fokusu
    expect(document.activeElement).toBe(document.body);
  });

  it('keeps the old ?tab= links as real hrefs', async () => {
    await create();

    expect(links().map(link => link.getAttribute('href')))
      .toEqual(['/account', '/account?tab=documents', '/account?tab=security', '/account?tab=notifications']);
  });

  it.each(['Employer', 'Admin'] as const)('hides the documents section for %s', async r => {
    role = r;
    await create();

    expect(links().map(link => link.textContent?.trim())).toEqual(['Profil', 'Hasło i bezpieczeństwo', 'Powiadomienia']);
    expect(root().querySelector('#dokumenty')).toBeNull();
  });

  it('opens the section from ?tab= (e.g. the link from the apply dialog): aria-current and focus on its heading', async () => {
    await create('documents');

    expect(current()).toBe('Dokumenty');
    expect(document.activeElement).toBe(root().querySelector('#dokumenty-heading'));
  });

  it('moves to the section when ?tab= changes while the page is open', async () => {
    await create();

    queryParams.next(convertToParamMap({ tab: 'notifications' }));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(current()).toBe('Powiadomienia');
    expect(document.activeElement).toBe(root().querySelector('#powiadomienia-heading'));
  });

  it('falls back to the profile for an unknown or unavailable section', async () => {
    role = 'Employer';
    await create('documents');

    expect(current()).toBe('Profil');
  });

  it('a click in the menu marks the section, focuses its heading and replaces the URL', async () => {
    await create();

    links()[2].click();
    fixture.detectChanges();

    expect(current()).toBe('Hasło i bezpieczeństwo');
    expect(document.activeElement).toBe(root().querySelector('#haslo-heading'));
    expect(navigateByUrl).toHaveBeenCalledWith(
      expect.objectContaining({ queryParams: { tab: 'security' } }), expect.objectContaining({ replaceUrl: true }));
  });

  it('leaves the page alone for a ctrl+click (new tab)', async () => {
    await create();

    // jsdom nie otwiera nowych kart – blokujemy tylko domyślną nawigację przeglądarki
    links()[1].addEventListener('click', event => event.preventDefault());
    links()[1].dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, ctrlKey: true }));
    fixture.detectChanges();

    expect(current()).toBe('Profil');
  });
});
