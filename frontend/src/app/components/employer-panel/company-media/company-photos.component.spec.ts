import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject, of, throwError } from 'rxjs';
import { CompanyPhotosComponent } from './company-photos.component';
import { PhotoCaptionDialogComponent } from './photo-caption-dialog.component';
import { CompanyPhoto } from '../../../models/company-media.model';
import { ConfirmService } from '../../../services/confirm.service';
import { EmployerMediaService, PhotoUploadEvent } from '../../../services/employer-media.service';
import { makeCompany } from '../../../../testing/company-fixtures';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

const photo = (id: number, caption: string | null = `Zdjęcie firmy ${id}`): CompanyPhoto =>
  ({ id, caption, width: 800, height: 600, url: `/api/companies/3/photos/${id}/file` });

function imageFile(name: string, size = 1000): File {
  const file = new File(['x'], name, { type: name.endsWith('.png') ? 'image/png' : 'image/jpeg' });
  Object.defineProperty(file, 'size', { value: size });
  return file;
}

describe('CompanyPhotosComponent', () => {
  let fixture: ComponentFixture<CompanyPhotosComponent>;
  let component: CompanyPhotosComponent;
  let media: Record<string, ReturnType<typeof vi.fn>>;
  let confirm: ReturnType<typeof vi.fn>;
  let dialogOpen: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;

  function create(photos: CompanyPhoto[] = [photo(1), photo(2), photo(3)]): void {
    media['getPhotos'].mockReturnValue(of(photos));
    fixture = TestBed.createComponent(CompanyPhotosComponent);
    fixture.componentRef.setInput('company', makeCompany(3));
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function button(label: string): HTMLButtonElement {
    return root().querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!;
  }

  function selectFiles(files: File[]): void {
    const input = root().querySelector<HTMLInputElement>('input[type="file"]')!;
    Object.defineProperty(input, 'files', { value: files, configurable: true });
    input.dispatchEvent(new Event('change'));
  }

  beforeEach(async () => {
    media = {
      getPhotos: vi.fn(),
      loadImage: vi.fn().mockReturnValue(new Subject<Blob>()),
      uploadPhotos: vi.fn(),
      updateCaption: vi.fn(),
      reorderPhotos: vi.fn().mockImplementation((ids: number[]) => of(ids.map(id => photo(id)))),
      deletePhoto: vi.fn().mockReturnValue(of(undefined))
    };
    confirm = vi.fn().mockReturnValue(of(true));
    dialogOpen = vi.fn();
    snackBarOpen = vi.fn();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [CompanyPhotosComponent],
      providers: [
        { provide: EmployerMediaService, useValue: media },
        { provide: ConfirmService, useValue: { confirm } },
        { provide: MatDialog, useValue: { open: dialogOpen } },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } }
      ]
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  it('loads the gallery of the company and shows thumbnails with captions and dimensions', () => {
    create();

    expect(media['getPhotos']).toHaveBeenCalledWith(3);
    const images = root().querySelectorAll('li.photo img');
    expect(images.length).toBe(3);
    expect(images[0].getAttribute('alt')).toBe('Zdjęcie firmy 1');
    expect(images[0].getAttribute('width')).toBe('800');
    expect(images[0].getAttribute('loading')).toBe('lazy');
    expect(root().querySelector('figcaption')?.textContent).toBe('Zdjęcie firmy 1');
    expect(root().textContent).toContain('3 z 20 zdjęć');
    // obraz pobierany z JWT (firma może być niezatwierdzona)
    expect(media['loadImage']).toHaveBeenCalledWith('/api/companies/3/photos/1/file');
  });

  it('names photos without a caption by position', () => {
    create([photo(1, null)]);

    expect(button('Usuń zdjęcie: Zdjęcie 1')).not.toBeNull();
  });

  it('shows a load error with a retry', () => {
    media['getPhotos'].mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    fixture = TestBed.createComponent(CompanyPhotosComponent);
    fixture.componentRef.setInput('company', makeCompany(3));
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(root().querySelector('[role="alert"]')?.textContent).toContain('Nie udało się wczytać zdjęć');
    media['getPhotos'].mockReturnValue(of([photo(1)]));
    root().querySelector<HTMLButtonElement>('.load-error button')!.click();
    expect(root().querySelectorAll('li.photo').length).toBe(1);
  });

  describe('reordering', () => {
    it('keyboard: "Przesuń w prawo" moves the photo, saves the full order and announces the position', () => {
      create();

      button('Przesuń w prawo: Zdjęcie firmy 1').click();

      expect(component.photos().map(p => p.id)).toEqual([2, 1, 3]);
      expect(media['reorderPhotos']).toHaveBeenCalledWith([2, 1, 3]);
      expect(root().querySelector('[role="status"]')?.textContent).toContain('pozycja 2 z 3');
    });

    it('hides the arrow towards the edge for the first and last photo', () => {
      create();

      expect(button('Przesuń w lewo: Zdjęcie firmy 1').disabled).toBe(true);
      expect(button('Przesuń w prawo: Zdjęcie firmy 3').disabled).toBe(true);
      expect(button('Przesuń w lewo: Zdjęcie firmy 3').disabled).toBe(false);
    });

    it('drag and drop uses the same order request', () => {
      create();

      component.drop({ previousIndex: 2, currentIndex: 0 } as never);

      expect(media['reorderPhotos']).toHaveBeenCalledWith([3, 1, 2]);
    });

    it('restores the last saved order when saving fails and quietly reloads the gallery from the server', () => {
      create();
      media['reorderPhotos'].mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { message: 'Invalid photo order' } })));
      const reloaded = new Subject<CompanyPhoto[]>();
      media['getPhotos'].mockReturnValue(reloaded);

      button('Przesuń w lewo: Zdjęcie firmy 2').click();

      expect(component.photos().map(p => p.id)).toEqual([1, 2, 3]);
      expect(root().querySelector('.errors')?.textContent).toContain('Nieprawidłowa kolejność zdjęć');
      expect(media['getPhotos']).toHaveBeenCalledTimes(2);
      // ciche odświeżenie – siatka zostaje (bez szkieletu)
      expect(component.loading()).toBe(false);
      expect(root().querySelector('.skeleton')).toBeNull();

      // galeria zmieniła się w innej karcie – widać stan z serwera
      reloaded.next([photo(1), photo(3)]);
      expect(component.photos().map(p => p.id)).toEqual([1, 3]);
      expect(root().querySelector('.errors')?.textContent).toContain('Nieprawidłowa kolejność zdjęć');
    });

    it('keeps the current gallery when the quiet reload fails', () => {
      create();
      media['reorderPhotos'].mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { message: 'Invalid photo order' } })));
      media['getPhotos'].mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));

      button('Przesuń w lewo: Zdjęcie firmy 2').click();

      expect(component.loadError()).toBeNull();
      expect(component.photos().map(p => p.id)).toEqual([1, 2, 3]);
    });
  });

  describe('uploading', () => {
    it('validates files on the client and uploads the accepted ones with progress', () => {
      create();
      const upload = new Subject<PhotoUploadEvent>();
      media['uploadPhotos'].mockReturnValue(upload);

      selectFiles([imageFile('biuro.jpg'), new File(['x'], 'cv.pdf', { type: 'application/pdf' })]);

      expect(media['uploadPhotos']).toHaveBeenCalledWith([expect.objectContaining({ name: 'biuro.jpg' })]);
      expect(root().querySelector('.errors')?.textContent).toContain('cv.pdf: Nieobsługiwany typ pliku');

      upload.next({ type: 'progress', percent: 40 });
      const bar = root().querySelector('mat-progress-bar')!;
      expect(bar.getAttribute('role')).toBe('progressbar');
      expect(bar.getAttribute('aria-valuetext')).toBe('Wysyłanie zdjęć: 40%');
      expect(root().querySelector<HTMLButtonElement>('button.add-tile')!.disabled).toBe(true);

      upload.next({ type: 'done', photos: [photo(1), photo(2), photo(3), photo(4)] });
      expect(root().querySelector('mat-progress-bar')).toBeNull();
      expect(component.photos().length).toBe(4);
      expect(snackBarOpen).toHaveBeenCalledWith('Dodano 1 zdjęcie', 'OK', expect.objectContaining({ politeness: 'polite' }));
    });

    it('respects the limit of 20 photos and disables the tile when the gallery is full', () => {
      create(Array.from({ length: 19 }, (_, i) => photo(i + 1)));
      media['uploadPhotos'].mockReturnValue(new Subject());

      selectFiles([imageFile('a.png'), imageFile('b.png')]);

      expect(media['uploadPhotos']).toHaveBeenCalledWith([expect.objectContaining({ name: 'a.png' })]);
      expect(root().querySelector('.errors')?.textContent).toContain('b.png: Firma może mieć maksymalnie 20 zdjęć');

      create(Array.from({ length: 20 }, (_, i) => photo(i + 1)));
      const tile = root().querySelector<HTMLButtonElement>('button.add-tile')!;
      expect(tile.disabled).toBe(true);
      expect(tile.textContent).toContain('Limit 20 zdjęć');
    });

    it('shows the translated API error', () => {
      create();
      media['uploadPhotos'].mockReturnValue(throwError(() => new HttpErrorResponse({
        status: 400, error: { message: 'Unsupported image type - upload a PNG or JPEG file' }
      })));

      selectFiles([imageFile('a.png')]);

      expect(root().querySelector('.errors[role="alert"]')?.textContent).toContain('Nieobsługiwany typ obrazu');
      expect(component.uploadProgress()).toBeNull();
    });

    it('cancelling stops the upload', () => {
      create();
      const upload = new Subject<PhotoUploadEvent>();
      media['uploadPhotos'].mockReturnValue(upload);
      selectFiles([imageFile('a.png')]);

      media['getPhotos'].mockReturnValue(of([photo(1), photo(2), photo(3), photo(4)]));

      component.cancelUpload();

      expect(upload.observed).toBe(false);
      expect(component.uploadProgress()).toBeNull();
      // serwer mógł zapisać zdjęcia przed przerwaniem – lista przeładowana z serwera
      expect(media['getPhotos']).toHaveBeenCalledTimes(2);
      expect(component.photos().length).toBe(4);
    });
  });

  describe('captions and deleting', () => {
    it('edits the caption in a dialog and replaces the photo', () => {
      create();
      dialogOpen.mockReturnValue({ afterClosed: () => of('Nowy opis') });
      media['updateCaption'].mockReturnValue(of(photo(2, 'Nowy opis')));

      button('Edytuj opis zdjęcia: Zdjęcie firmy 2').click();

      expect(dialogOpen).toHaveBeenCalledWith(PhotoCaptionDialogComponent, expect.objectContaining({ data: { caption: 'Zdjęcie firmy 2' }, maxWidth: '95vw' }));
      expect(media['updateCaption']).toHaveBeenCalledWith(2, 'Nowy opis');
      expect(component.photos()[1].caption).toBe('Nowy opis');
    });

    it('does not call the API when the caption dialog is cancelled', () => {
      create();
      dialogOpen.mockReturnValue({ afterClosed: () => of(undefined) });

      button('Edytuj opis zdjęcia: Zdjęcie firmy 2').click();

      expect(media['updateCaption']).not.toHaveBeenCalled();
    });

    it('deletes after confirmation in the dialog (not window.confirm)', () => {
      create();
      const nativeConfirm = vi.spyOn(window, 'confirm');

      button('Usuń zdjęcie: Zdjęcie firmy 1').click();

      expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ destructive: true }));
      expect(nativeConfirm).not.toHaveBeenCalled();
      expect(media['deletePhoto']).toHaveBeenCalledWith(1);
      expect(component.photos().map(p => p.id)).toEqual([2, 3]);
      expect(snackBarOpen).toHaveBeenCalledWith('Usunięto zdjęcie: Zdjęcie firmy 1', 'OK', expect.anything());
    });

    it('keeps the photo when the confirmation is declined', () => {
      create();
      confirm.mockReturnValue(of(false));

      button('Usuń zdjęcie: Zdjęcie firmy 1').click();

      expect(media['deletePhoto']).not.toHaveBeenCalled();
    });
  });
});
