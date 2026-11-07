import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject, of, throwError } from 'rxjs';
import { OfferBackgroundSettingsComponent } from './offer-background-settings.component';
import { offerHeroMask } from '../../../utils/offer-hero';
import { BackgroundFocus, OfferBackground } from '../../../models/company-media.model';
import { Company } from '../../../models/company.model';
import { ConfirmService } from '../../../services/confirm.service';
import { BackgroundUploadEvent, EmployerMediaService } from '../../../services/employer-media.service';
import { makeCompany } from '../../../../testing/company-fixtures';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

const BACKGROUND: OfferBackground = { url: '/api/companies/3/offer-background/file?v=abc', dim: 70, focus: BackgroundFocus.Top };

describe('OfferBackgroundSettingsComponent', () => {
  let fixture: ComponentFixture<OfferBackgroundSettingsComponent>;
  let component: OfferBackgroundSettingsComponent;
  let media: Record<string, ReturnType<typeof vi.fn>>;
  let confirm: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;
  let emitted: (OfferBackground | null)[];

  function create(company: Company = makeCompany(3, { name: 'Acme', location: 'Gdańsk', offerBackground: BACKGROUND })): void {
    fixture = TestBed.createComponent(OfferBackgroundSettingsComponent);
    fixture.componentRef.setInput('company', company);
    component = fixture.componentInstance;
    emitted = [];
    component.backgroundChange.subscribe(value => emitted.push(value));
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function slider(): HTMLInputElement {
    return root().querySelector<HTMLInputElement>('input[type="range"]')!;
  }

  function buttonWithText(text: string): HTMLButtonElement | undefined {
    return Array.from(root().querySelectorAll<HTMLButtonElement>('button')).find(b => b.textContent?.trim() === text);
  }

  beforeEach(async () => {
    media = {
      loadImage: vi.fn().mockReturnValue(new Subject<Blob>()),
      uploadBackground: vi.fn(),
      updateBackground: vi.fn().mockImplementation((s: { dim: number; focus: BackgroundFocus }) => of({ url: BACKGROUND.url, ...s })),
      deleteBackground: vi.fn().mockReturnValue(of(undefined))
    };
    confirm = vi.fn().mockReturnValue(of(true));
    snackBarOpen = vi.fn();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [OfferBackgroundSettingsComponent],
      providers: [
        { provide: EmployerMediaService, useValue: media },
        { provide: ConfirmService, useValue: { confirm } },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } }
      ]
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  it('previews the same mask as the offer page (shared offerHeroMask)', () => {
    create();
    expect(component.mask()).toBe(offerHeroMask(70));
  });

  it('shows the live preview with the saved settings, image loaded with the token', () => {
    create();

    const preview = root().querySelector('.preview')!;
    expect(preview.getAttribute('role')).toBe('img');
    expect(preview.getAttribute('aria-label')).toBe('Podgląd nagłówka oferty z tłem firmy, przyciemnienie 70%, punkt ostrości: Góra');
    expect(preview.textContent).toContain('Acme · Gdańsk');
    expect(media['loadImage']).toHaveBeenCalledWith(BACKGROUND.url);
    expect(slider().value).toBe('70');
    expect(slider().getAttribute('aria-valuetext')).toBe('70 procent');
    expect(slider().min).toBe('55');
    expect(slider().max).toBe('95');
  });

  it('updates the preview while sliding and enables saving only after a change', () => {
    create();
    expect(buttonWithText('Zapisz tło')!.disabled).toBe(true);

    slider().value = '85';
    slider().dispatchEvent(new Event('input'));

    expect(component.dim()).toBe(85);
    expect(root().querySelector('label[for="background-dim"]')?.textContent).toContain('85%');
    expect(slider().getAttribute('aria-valuetext')).toBe('85 procent');
    expect(component.mask()).toBe(offerHeroMask(85));
    expect(buttonWithText('Zapisz tło')!.disabled).toBe(false);
  });

  it('saves dim and focus and reports the change to the parent', () => {
    create();
    component.dim.set(80);
    component.focus.set(BackgroundFocus.Bottom);

    buttonWithText('Zapisz tło')!.click();

    expect(media['updateBackground']).toHaveBeenCalledWith({ dim: 80, focus: BackgroundFocus.Bottom });
    expect(emitted).toEqual([{ url: BACKGROUND.url, dim: 80, focus: BackgroundFocus.Bottom }]);
    expect(component.dirty()).toBe(false);
    expect(snackBarOpen).toHaveBeenCalledWith('Ustawienia tła zostały zapisane', 'OK', expect.objectContaining({ politeness: 'polite' }));
  });

  it('shows a validation error from the API in an alert', () => {
    create();
    media['updateBackground'].mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { errors: { Dim: ['x'] } } })));
    component.dim.set(60);

    component.save();

    expect(root().querySelector('[role="alert"]')?.textContent).toContain('Przesłane dane są nieprawidłowe');
  });

  it('without an image: plain header, "Dodaj obraz" and no remove button; settings can still be saved', () => {
    create(makeCompany(3, { offerBackground: null }));

    expect(root().querySelector('.preview img')).toBeNull();
    expect(root().textContent).toContain('Brak obrazu');
    expect(buttonWithText('Dodaj obraz')).toBeDefined();
    expect(buttonWithText('Usuń tło')).toBeUndefined();
    expect(component.dim()).toBe(75);

    media['updateBackground'].mockReturnValue(of({ url: null, dim: 60, focus: BackgroundFocus.Center }));
    component.dim.set(60);
    component.save();
    expect(emitted).toEqual([null]);
  });

  it('uploads a new image with progress and uses the versioned url from the API', () => {
    create();
    const upload = new Subject<BackgroundUploadEvent>();
    media['uploadBackground'].mockReturnValue(upload);
    const input = root().querySelector<HTMLInputElement>('input[type="file"]')!;
    const file = new File(['x'], 'tlo.jpg', { type: 'image/jpeg' });
    Object.defineProperty(input, 'files', { value: [file] });

    input.dispatchEvent(new Event('change'));
    upload.next({ type: 'progress', percent: 50 });

    expect(root().querySelector('mat-progress-bar')?.getAttribute('aria-valuetext')).toBe('Wysyłanie obrazu: 50%');
    upload.next({ type: 'done', background: { url: '/api/companies/3/offer-background/file?v=new', dim: 70, focus: BackgroundFocus.Top } });

    expect(component.url()).toBe('/api/companies/3/offer-background/file?v=new');
    expect(root().querySelector('mat-progress-bar')).toBeNull();
    expect(emitted[0]?.url).toContain('v=new');
  });

  it('rejects an unsupported file on the client', () => {
    create();
    const input = root().querySelector<HTMLInputElement>('input[type="file"]')!;
    Object.defineProperty(input, 'files', { value: [new File(['x'], 'tlo.gif', { type: 'image/gif' })] });

    input.dispatchEvent(new Event('change'));

    expect(media['uploadBackground']).not.toHaveBeenCalled();
    expect(root().querySelector('[role="alert"]')?.textContent).toContain('tlo.gif');
  });

  it('removes the image after confirmation', () => {
    create();

    buttonWithText('Usuń tło')!.click();

    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ destructive: true }));
    expect(media['deleteBackground']).toHaveBeenCalled();
    expect(component.url()).toBeNull();
    expect(emitted).toEqual([null]);
  });
});
