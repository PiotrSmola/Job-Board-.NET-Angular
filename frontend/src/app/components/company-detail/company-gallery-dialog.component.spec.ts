import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { CompanyGalleryDialogComponent, CompanyGalleryDialogData } from './company-gallery-dialog.component';
import { CompanyPhoto } from '../../models/company-media.model';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

const PHOTOS: CompanyPhoto[] = [
  { id: 1, caption: 'Open space', width: 1600, height: 900, url: '/api/companies/7/photos/1/file' },
  { id: 2, caption: null, width: 800, height: 1200, url: '/api/companies/7/photos/2/file' },
  { id: 3, caption: 'Kuchnia', width: 1200, height: 800, url: '/api/companies/7/photos/3/file' }
];

describe('CompanyGalleryDialogComponent', () => {
  let fixture: ComponentFixture<CompanyGalleryDialogComponent>;

  const root = () => fixture.nativeElement as HTMLElement;
  const img = () => root().querySelector('img')!;
  const counter = () => root().querySelector('.counter')!;

  function create(data: Partial<CompanyGalleryDialogData> = {}): void {
    TestBed.overrideProvider(MAT_DIALOG_DATA, { useValue: { photos: PHOTOS, companyName: 'Acme', startIndex: 0, ...data } });
    fixture = TestBed.createComponent(CompanyGalleryDialogComponent);
    fixture.detectChanges();
  }

  function key(name: string): void {
    root().dispatchEvent(new KeyboardEvent('keydown', { key: name, bubbles: true }));
    fixture.detectChanges();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CompanyGalleryDialogComponent],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: null },
        { provide: MatDialogRef, useValue: { close: vi.fn() } }
      ]
    });
  });

  it('opens at the chosen photo with its dimensions, alternative text and caption', () => {
    create({ startIndex: 2 });

    expect(root().querySelector('h2')?.textContent).toBe('Zdjęcia firmy Acme');
    expect(img().getAttribute('src')).toBe('/api/companies/7/photos/3/file');
    expect(img().getAttribute('width')).toBe('1200');
    expect(img().getAttribute('height')).toBe('800');
    expect(img().getAttribute('alt')).toBe('Kuchnia');
    expect(root().querySelector('figcaption')?.textContent).toBe('Kuchnia');
    expect(counter().textContent).toBe('Zdjęcie 3 z 3: Kuchnia');
    expect(counter().getAttribute('role')).toBe('status');
  });

  it('uses the numbered fallback as the alternative text and shows no caption without one', () => {
    create({ startIndex: 1 });

    expect(img().getAttribute('alt')).toBe('Zdjęcie firmy Acme 2');
    expect(root().querySelector('figcaption')).toBeNull();
  });

  it('moves with the buttons and wraps around', () => {
    create();

    root().querySelector<HTMLButtonElement>('button.prev')!.click();
    fixture.detectChanges();
    expect(img().getAttribute('src')).toContain('/photos/3/');

    root().querySelector<HTMLButtonElement>('button.next')!.click();
    fixture.detectChanges();
    expect(img().getAttribute('src')).toContain('/photos/1/');
    expect(root().querySelector('button.prev')?.getAttribute('aria-label')).toBe('Poprzednie zdjęcie');
    expect(root().querySelector('button.next')?.getAttribute('aria-label')).toBe('Następne zdjęcie');
  });

  it('moves with the ← and → keys', () => {
    create();

    key('ArrowRight');
    expect(counter().textContent).toContain('Zdjęcie 2 z 3');
    key('ArrowRight');
    key('ArrowRight');
    expect(counter().textContent).toContain('Zdjęcie 1 z 3');
    key('ArrowLeft');
    expect(counter().textContent).toContain('Zdjęcie 3 z 3');
  });

  it('has a close button and no navigation for a single photo', () => {
    create({ photos: [PHOTOS[0]] });

    expect(root().querySelector('button.prev')).toBeNull();
    expect(root().querySelector('button.close')?.textContent?.trim()).toBe('Zamknij');
    key('ArrowRight');
    expect(img().getAttribute('src')).toContain('/photos/1/');
  });

  it('keeps an out-of-range start index inside the list', () => {
    create({ startIndex: 9 });

    expect(img().getAttribute('src')).toContain('/photos/3/');
  });
});
