import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject, of, throwError } from 'rxjs';
import { NotificationsTabComponent } from './notifications-tab.component';
import { AccountService } from '../../../services/account.service';
import { AuthService } from '../../../services/auth.service';
import { NotificationPreferences } from '../../../models/account.model';

vi.setConfig({ testTimeout: 20000 });

const PREFS: NotificationPreferences = { applicationStatusChanged: false, newApplication: true, companyReview: true };

describe('NotificationsTabComponent', () => {
  let fixture: ComponentFixture<NotificationsTabComponent>;
  let getNotificationPreferences: ReturnType<typeof vi.fn>;
  let updateNotificationPreferences: ReturnType<typeof vi.fn>;
  let role: 'User' | 'Employer' | 'Admin';

  function create(): void {
    fixture = TestBed.createComponent(NotificationsTabComponent);
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function labels(): string[] {
    return Array.from(root().querySelectorAll('mat-checkbox label')).map(l => l.textContent?.trim() ?? '');
  }

  function checkbox(index: number): HTMLInputElement {
    return root().querySelectorAll<HTMLInputElement>('input[type="checkbox"]')[index];
  }

  beforeEach(() => {
    role = 'User';
    getNotificationPreferences = vi.fn().mockReturnValue(of(PREFS));
    updateNotificationPreferences = vi.fn();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    TestBed.configureTestingModule({
      imports: [NotificationsTabComponent],
      providers: [
        { provide: AccountService, useValue: { getNotificationPreferences, updateNotificationPreferences } },
        { provide: AuthService, useValue: { isAdmin: () => role === 'Admin', isEmployer: () => role === 'Employer' } }
      ]
    });
  });

  afterEach(() => vi.restoreAllMocks());

  it('shows the candidate only the application e-mails, with a description linked to the checkbox', () => {
    create();

    expect(labels()).toEqual(['Moje aplikacje']);
    expect(checkbox(0).checked).toBe(false);
    const describedBy = checkbox(0).getAttribute('aria-describedby')!;
    expect(root().querySelector(`#${describedBy}`)?.textContent).toContain('zmiany jej statusu');
  });

  it('shows the employer the new-application and company-review e-mails', () => {
    role = 'Employer';
    create();

    expect(labels()).toEqual(['Nowe aplikacje', 'Weryfikacja firmy']);
    expect(checkbox(0).checked).toBe(true);
  });

  it('does not load anything for an administrator', () => {
    role = 'Admin';
    create();

    expect(getNotificationPreferences).not.toHaveBeenCalled();
    expect(root().textContent).toContain('nie ma powiadomień do skonfigurowania');
  });

  it('saves all preferences (fields of other roles unchanged) and announces it', () => {
    create();
    updateNotificationPreferences.mockReturnValue(of({ ...PREFS, applicationStatusChanged: true }));

    checkbox(0).click();
    fixture.detectChanges();
    root().querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    expect(updateNotificationPreferences).toHaveBeenCalledWith({ applicationStatusChanged: true, newApplication: true, companyReview: true });
    expect(root().querySelector('.success[role="status"]')?.textContent).toBe('Zapisano ustawienia powiadomień');
  });

  it('keeps the sent values when the API returns no body', () => {
    create();
    updateNotificationPreferences.mockReturnValue(of(null));

    checkbox(0).click();
    fixture.detectChanges();
    root().querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    expect(checkbox(0).checked).toBe(true);
  });

  it('announces a save error', () => {
    create();
    updateNotificationPreferences.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));

    root().querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    expect(root().querySelector('.error[role="alert"]')?.textContent).toBe('Brak połączenia z serwerem');
  });

  it('announces a loading error and cancels the request when destroyed', () => {
    getNotificationPreferences.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    create();
    expect(root().querySelector('[role="alert"]')?.textContent).toContain('Nie udało się wczytać ustawień powiadomień');

    const pending = new Subject<NotificationPreferences>();
    getNotificationPreferences.mockReturnValue(pending);
    create();
    fixture.destroy();
    expect(pending.observed).toBe(false);
  });
});
