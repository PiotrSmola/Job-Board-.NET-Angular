import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject, of, throwError } from 'rxjs';
import { ProfileTabComponent } from './profile-tab.component';
import { AccountService } from '../../../services/account.service';
import { Profile } from '../../../models/account.model';

vi.setConfig({ testTimeout: 20000 });

const PROFILE: Profile = {
  email: 'jan@example.com',
  firstName: 'Jan',
  lastName: 'Kowalski',
  phoneNumber: '+48 600 100 200',
  headline: 'Frontend Developer',
  location: 'Rzeszów',
  linkedInUrl: 'https://www.linkedin.com/in/jan',
  about: 'O mnie',
  emailConfirmed: false,
  roles: ['User'],
  createdAt: '2026-01-15T10:00:00Z'
};

describe('ProfileTabComponent', () => {
  let fixture: ComponentFixture<ProfileTabComponent>;
  let getProfile: ReturnType<typeof vi.fn>;
  let updateProfile: ReturnType<typeof vi.fn>;

  function create(profile: Profile | Subject<Profile> | HttpErrorResponse = PROFILE): void {
    getProfile.mockReturnValue(profile instanceof HttpErrorResponse
      ? throwError(() => profile)
      : profile instanceof Subject ? profile : of(profile));
    fixture = TestBed.createComponent(ProfileTabComponent);
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function input(name: string): HTMLInputElement {
    return root().querySelector<HTMLInputElement>(`[formControlName="${name}"]`)!;
  }

  function type(name: string, value: string): void {
    const element = input(name);
    element.value = value;
    element.dispatchEvent(new Event('input'));
    element.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
  }

  function submit(): void {
    root().querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  }

  beforeEach(() => {
    getProfile = vi.fn();
    updateProfile = vi.fn();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    TestBed.configureTestingModule({
      imports: [ProfileTabComponent],
      providers: [{ provide: AccountService, useValue: { getProfile, updateProfile } }]
    });
  });

  afterEach(() => vi.restoreAllMocks());

  it('fills the form with the profile and shows the e-mail status in words', () => {
    create();

    expect(input('firstName').value).toBe('Jan');
    expect(input('linkedInUrl').value).toBe('https://www.linkedin.com/in/jan');
    expect(root().textContent).toContain('jan@example.com');
    expect(root().textContent).toContain('(niepotwierdzony)');
    expect(root().textContent).toContain('15.01.2026');
  });

  it('uses autocomplete hints for personal data', () => {
    create();

    expect(input('firstName').getAttribute('autocomplete')).toBe('given-name');
    expect(input('lastName').getAttribute('autocomplete')).toBe('family-name');
    expect(input('phoneNumber').getAttribute('autocomplete')).toBe('tel');
    expect(input('phoneNumber').type).toBe('tel');
  });

  it('shows a status region while loading and an alert with retry on error', () => {
    const pending = new Subject<Profile>();
    create(pending);
    expect(root().querySelector('[role="status"]')?.textContent).toContain('Wczytywanie profilu');

    fixture.destroy();
    expect(pending.observed).toBe(false);

    create(new HttpErrorResponse({ status: 500 }));
    expect(root().querySelector('[role="alert"]')?.textContent).toContain('Nie udało się wczytać profilu');
    getProfile.mockReturnValue(of(PROFILE));
    root().querySelector<HTMLButtonElement>('[role="alert"] button')!.click();
    fixture.detectChanges();
    expect(input('firstName').value).toBe('Jan');
  });

  it('saves trimmed values and sends empty optional fields as null', () => {
    create();
    updateProfile.mockReturnValue(of({ ...PROFILE, firstName: 'Janek', phoneNumber: null }));

    type('firstName', '  Janek ');
    type('phoneNumber', '  ');
    submit();

    expect(updateProfile).toHaveBeenCalledWith({
      firstName: 'Janek',
      lastName: 'Kowalski',
      phoneNumber: null,
      headline: 'Frontend Developer',
      location: 'Rzeszów',
      linkedInUrl: 'https://www.linkedin.com/in/jan',
      about: 'O mnie'
    });
    expect(root().querySelector('.success[role="status"]')?.textContent).toBe('Zapisano zmiany w profilu');
    expect(input('firstName').value).toBe('Janek');
  });

  it.each([
    ['firstName', '   ', 'Imię jest wymagane'],
    ['phoneNumber', '600-abc', 'Dozwolone są cyfry, spacje, „+” i „-”'],
    ['linkedInUrl', 'http://linkedin.com/in/jan', 'Podaj adres profilu LinkedIn'],
    ['linkedInUrl', 'https://evil.com/linkedin.com/', 'Podaj adres profilu LinkedIn']
  ])('validates %s = "%s" on the client like the API', (name, value, message) => {
    create();

    type(name, value);
    submit();

    expect(updateProfile).not.toHaveBeenCalled();
    expect(root().textContent).toContain(message);
    expect(root().querySelector('.error[role="alert"]')?.textContent).toBe('Popraw zaznaczone pola formularza');
    expect(document.activeElement).toBe(input(name));
  });

  it('accepts a LinkedIn address without www', () => {
    create();
    updateProfile.mockReturnValue(of(PROFILE));

    type('linkedInUrl', 'https://linkedin.com/in/jan-kowalski');
    submit();

    expect(updateProfile).toHaveBeenCalled();
  });

  it('enforces the maximum lengths of the API (e.g. about ≤ 2000)', () => {
    create();
    expect(root().querySelector('textarea')!.getAttribute('maxlength')).toBe('2000');
    expect(input('headline').getAttribute('maxlength')).toBe('150');
  });

  it('announces a server error and keeps the entered data', () => {
    create();
    updateProfile.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 400, error: { errors: { LinkedInUrl: ['Invalid'] } }
    })));

    type('headline', 'Nowy nagłówek');
    submit();

    expect(root().querySelector('.error[role="alert"]')?.textContent).toBe('Przesłane dane są nieprawidłowe');
    expect(input('headline').value).toBe('Nowy nagłówek');
  });

  it('cancels a pending save when destroyed', () => {
    create();
    const saving = new Subject<Profile>();
    updateProfile.mockReturnValue(saving);

    submit();
    fixture.destroy();

    expect(saving.observed).toBe(false);
  });
});
