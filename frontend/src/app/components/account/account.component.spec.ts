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
  let navigate: ReturnType<typeof vi.spyOn>;
  let role: 'User' | 'Employer' | 'Admin';

  async function create(tab: string | null = null): Promise<void> {
    queryParams = new BehaviorSubject(convertToParamMap(tab ? { tab } : {}));
    TestBed.overrideProvider(ActivatedRoute, { useValue: { queryParamMap: queryParams } });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(AccountComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function tabLabels(): string[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('[role="tab"]'))
      .map(tab => tab.textContent?.replace(/\s+/g, ' ').trim().replace(/^\S+ /, '') ?? '');
  }

  function activeContent(): string {
    return (fixture.nativeElement as HTMLElement).querySelector('.mat-mdc-tab-body-active')?.textContent?.trim() ?? '';
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

  it('has a heading and four labelled tabs for a candidate, the profile tab first', async () => {
    await create();

    expect((fixture.nativeElement as HTMLElement).querySelector('h1')?.textContent).toBe('Moje konto');
    expect(tabLabels()).toEqual(['Profil', 'Dokumenty', 'Bezpieczeństwo', 'Powiadomienia']);
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="tablist"]')?.closest('[aria-label]')).not.toBeNull();
    expect(activeContent()).toBe('profil');
  });

  it.each(['Employer', 'Admin'] as const)('hides the documents tab for %s', async r => {
    role = r;
    await create();

    expect(tabLabels()).toEqual(['Profil', 'Bezpieczeństwo', 'Powiadomienia']);
  });

  it('opens the tab from ?tab= (e.g. the link from the apply dialog)', async () => {
    await create('documents');

    expect(fixture.componentInstance.selectedIndex()).toBe(1);
    expect(activeContent()).toBe('dokumenty');
  });

  it('falls back to the profile tab for an unknown or unavailable tab', async () => {
    role = 'Employer';
    await create('documents');

    expect(fixture.componentInstance.selectedIndex()).toBe(0);
  });

  it('stores the chosen tab in the URL without adding history entries', async () => {
    await create();

    fixture.componentInstance.onTabChange(2);

    expect(navigate).toHaveBeenCalledWith([], expect.objectContaining({ queryParams: { tab: 'security' }, replaceUrl: true }));

    fixture.componentInstance.onTabChange(0);
    expect(navigate).toHaveBeenLastCalledWith([], expect.objectContaining({ queryParams: { tab: null }, replaceUrl: true }));
  });
});
