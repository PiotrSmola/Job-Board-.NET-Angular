import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { CompanyGalleryComponent } from './company-gallery.component';
import { CompanyGalleryDialogComponent, GALLERY_DIALOG_CONFIG } from './company-gallery-dialog.component';
import { photoAlt, photosNoun } from './company-gallery';
import { CompanyPhoto } from '../../models/company-media.model';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

function photos(count: number): CompanyPhoto[] {
  return Array.from({ length: count }, (_, i) => ({
    id: i + 1,
    caption: i === 1 ? 'Sala spotkań' : null,
    width: 1200 + i,
    height: 800,
    url: `/api/companies/7/photos/${i + 1}/file`
  }));
}

describe('company gallery helpers', () => {
  it('uses the caption or a numbered fallback as the alternative text', () => {
    expect(photoAlt({ caption: 'Kuchnia' }, 'Acme', 0)).toBe('Kuchnia');
    expect(photoAlt({ caption: '   ' }, 'Acme', 2)).toBe('Zdjęcie firmy Acme 3');
    expect(photoAlt({ caption: null }, 'Acme', 0)).toBe('Zdjęcie firmy Acme 1');
  });

  it.each([[1, 'zdjęcie'], [2, 'zdjęcia'], [4, 'zdjęcia'], [5, 'zdjęć'], [12, 'zdjęć'], [22, 'zdjęcia']])(
    'photosNoun(%i) = %s', (count, noun) => expect(photosNoun(count)).toBe(noun));
});

describe('CompanyGalleryComponent', () => {
  let fixture: ComponentFixture<CompanyGalleryComponent>;
  let open: ReturnType<typeof vi.spyOn>;

  const root = () => fixture.nativeElement as HTMLElement;
  const buttons = () => Array.from(root().querySelectorAll<HTMLButtonElement>('button.photo'));

  function create(list: CompanyPhoto[]): void {
    fixture = TestBed.createComponent(CompanyGalleryComponent);
    fixture.componentRef.setInput('photos', list);
    fixture.componentRef.setInput('companyName', 'Acme');
    fixture.detectChanges();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [CompanyGalleryComponent] });
    open = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({} as never);
  });

  afterEach(() => vi.restoreAllMocks());

  it('shows one large and four small tiles; the last one opens the whole gallery with "+N zdjęć"', () => {
    create(photos(12));

    expect(buttons().length).toBe(5);
    expect(root().querySelector('li.large')).toBe(buttons()[0].parentElement);
    expect(buttons()[4].getAttribute('aria-label')).toBe('+8 zdjęć, pokaż wszystkie (12)');
    expect(buttons()[4].querySelector('.more')?.textContent).toBe('+8 zdjęć');
    expect(buttons()[4].querySelector('img')?.getAttribute('alt')).toBe('');
  });

  it('labels each tile with the alternative text of its photo', () => {
    create(photos(3));

    expect(buttons().map(b => b.getAttribute('aria-label'))).toEqual([
      'Powiększ zdjęcie: Zdjęcie firmy Acme 1', 'Powiększ zdjęcie: Sala spotkań', 'Powiększ zdjęcie: Zdjęcie firmy Acme 3'
    ]);
    expect(root().querySelector('.more')).toBeNull();
    expect(Array.from(root().querySelectorAll('img')).map(i => i.getAttribute('alt')))
      .toEqual(['Zdjęcie firmy Acme 1', 'Sala spotkań', 'Zdjęcie firmy Acme 3']);
  });

  it('gives every image its API dimensions and loads all but the first lazily', () => {
    create(photos(5));

    const images = Array.from(root().querySelectorAll('img'));
    expect(images.map(i => i.getAttribute('width'))).toEqual(['1200', '1201', '1202', '1203', '1204']);
    expect(images.every(i => i.getAttribute('height') === '800')).toBe(true);
    expect(images.map(i => i.getAttribute('loading'))).toEqual(['eager', 'lazy', 'lazy', 'lazy', 'lazy']);
    expect(images[0].getAttribute('src')).toBe('/api/companies/7/photos/1/file');
    expect(root().querySelector('.more')).toBeNull();
  });

  it('opens the gallery dialog at the clicked photo', () => {
    const list = photos(12);
    create(list);

    buttons()[2].click();
    expect(open).toHaveBeenCalledWith(CompanyGalleryDialogComponent, {
      ...GALLERY_DIALOG_CONFIG, data: { photos: list, companyName: 'Acme', startIndex: 2 }
    });

    buttons()[4].click();
    expect(open.mock.calls[1][1].data.startIndex).toBe(4);
    expect(GALLERY_DIALOG_CONFIG.ariaModal).toBe(true);
    expect(GALLERY_DIALOG_CONFIG.restoreFocus).toBe(true);
  });
});
