import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { LiveAnnouncer } from '@angular/cdk/a11y';
import { MatSnackBar } from '@angular/material/snack-bar';
import { SaveJobButtonComponent } from './save-job-button.component';
import { AuthService } from '../../services/auth.service';
import { UserDto } from '../../models/user.model';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

@Component({
  standalone: true,
  imports: [SaveJobButtonComponent],
  template: `
    <div class="card" (click)="cardClicks = cardClicks + 1">
      @if (shown()) {
        <app-save-job-button [jobId]="5" jobTitle="Angular Developer" />
      }
    </div>
  `
})
class HostComponent {
  cardClicks = 0;
  readonly shown = signal(true);
}

describe('SaveJobButtonComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let http: HttpTestingController;
  let user: ReturnType<typeof signal<UserDto | null>>;
  let employer: boolean;
  let announce: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;

  function create(savedIds: number[] = []): void {
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    TestBed.tick();
    if (user() && !employer) {
      http.expectOne('/api/saved-jobs/ids').flush(savedIds);
    }
    fixture.detectChanges();
  }

  function button(): HTMLButtonElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector('button.save-job-button');
  }

  beforeEach(() => {
    user = signal<UserDto | null>({ email: 'jan@example.com', firstName: 'Jan', lastName: 'K', token: 't' });
    employer = false;
    announce = vi.fn().mockResolvedValue(undefined);
    snackBarOpen = vi.fn();

    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { currentUser: user, isAdmin: () => false, isEmployer: () => employer } },
        { provide: LiveAnnouncer, useValue: { announce } },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } }
      ]
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  it('renders an unpressed toggle with the "save" label for an unsaved offer', () => {
    create();

    expect(button()!.getAttribute('aria-pressed')).toBe('false');
    expect(button()!.getAttribute('aria-label')).toBe('Zapisz ofertę: Angular Developer');
    expect(button()!.textContent).toContain('favorite_border');
    expect(button()!.querySelector('mat-icon')!.getAttribute('aria-hidden')).toBe('true');
  });

  it('renders a pressed toggle with the "remove" label for a saved offer (shape, not only colour)', () => {
    create([5]);

    expect(button()!.getAttribute('aria-pressed')).toBe('true');
    expect(button()!.getAttribute('aria-label')).toBe('Usuń z zapisanych: Angular Developer');
    expect(button()!.textContent?.trim()).toBe('favorite');
  });

  it('is not rendered for an employer or an anonymous visitor', () => {
    employer = true;
    create();
    expect(button()).toBeNull();

    user.set(null);
    employer = false;
    fixture.detectChanges();
    expect(button()).toBeNull();
  });

  it('saves optimistically, announces the result and does not open the offer card', () => {
    create();

    button()!.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.cardClicks).toBe(0);
    expect(button()!.getAttribute('aria-pressed')).toBe('true');
    expect(button()!.getAttribute('aria-busy')).toBe('true');
    http.expectOne('/api/saved-jobs/5').flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    expect(button()!.getAttribute('aria-busy')).toBeNull();
    expect(announce).toHaveBeenCalledWith('Zapisano ofertę: Angular Developer');
  });

  it('ignores clicks while the request is pending (focus stays – aria-disabled instead of disabled)', () => {
    create();
    button()!.click();
    fixture.detectChanges();

    expect(button()!.disabled).toBe(false);
    expect(button()!.getAttribute('aria-disabled')).toBe('true');
    button()!.click();

    http.expectOne('/api/saved-jobs/5').flush(null);
  });

  it('removes a saved offer and announces it', () => {
    create([5]);

    button()!.click();
    const req = http.expectOne('/api/saved-jobs/5');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
    fixture.detectChanges();

    expect(button()!.getAttribute('aria-pressed')).toBe('false');
    expect(announce).toHaveBeenCalledWith('Usunięto z zapisanych: Angular Developer');
  });

  it('rolls back and shows the translated error when saving fails', () => {
    create();

    button()!.click();
    http.expectOne('/api/saved-jobs/5').flush({ message: 'Job offer not found' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(button()!.getAttribute('aria-pressed')).toBe('false');
    expect(snackBarOpen).toHaveBeenCalledWith('Oferta nie została znaleziona', 'OK', { duration: 5000 });
    expect(announce).not.toHaveBeenCalled();
  });

  it('leaves 401/403 to the interceptor', () => {
    create();

    button()!.click();
    http.expectOne('/api/saved-jobs/5').flush(null, { status: 403, statusText: 'Forbidden' });

    expect(snackBarOpen).not.toHaveBeenCalled();
  });

  it('stops listening when destroyed, but the request completes and the shared state stays consistent', () => {
    create();
    button()!.click();
    const req = http.expectOne('/api/saved-jobs/5');

    fixture.componentInstance.shown.set(false);
    fixture.detectChanges();
    req.flush(null);

    expect(req.cancelled).toBe(false);
    expect(announce).not.toHaveBeenCalled();

    fixture.componentInstance.shown.set(true);
    fixture.detectChanges();
    expect(button()!.getAttribute('aria-pressed')).toBe('true');
  });
});
