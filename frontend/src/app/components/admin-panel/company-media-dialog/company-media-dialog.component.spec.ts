import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject, of, throwError } from 'rxjs';
import { CompanyMediaDialogComponent } from './company-media-dialog.component';
import { AdminService } from '../../../services/admin.service';
import { ConfirmService } from '../../../services/confirm.service';
import { EmployerMediaService } from '../../../services/employer-media.service';
import { Company } from '../../../models/company.model';
import { CompanyPhoto } from '../../../models/company-media.model';
import { makeCompany } from '../../../../testing/company-fixtures';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

const photo = (id: number, caption: string | null = null): CompanyPhoto =>
  ({ id, caption, width: 400, height: 300, url: `/api/companies/5/photos/${id}/file` });

describe('CompanyMediaDialogComponent', () => {
  let fixture: ComponentFixture<CompanyMediaDialogComponent>;
  let getPhotos: ReturnType<typeof vi.fn>;
  let deleteCompanyPhoto: ReturnType<typeof vi.fn>;
  let deleteCompanyBackground: ReturnType<typeof vi.fn>;
  let confirm: ReturnType<typeof vi.fn>;
  let close: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;

  async function create(company: Company = makeCompany(5, { name: 'Acme', offerBackground: { url: '/bg?v=1', dim: 70, focus: 0 } })) {
    await TestBed.configureTestingModule({
      imports: [CompanyMediaDialogComponent],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: company },
        { provide: MatDialogRef, useValue: { close } },
        { provide: EmployerMediaService, useValue: { getPhotos, loadImage: () => new Subject<Blob>() } },
        { provide: AdminService, useValue: { deleteCompanyPhoto, deleteCompanyBackground } },
        { provide: ConfirmService, useValue: { confirm } },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(CompanyMediaDialogComponent);
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    getPhotos = vi.fn().mockReturnValue(of([photo(1, 'Biuro'), photo(2)]));
    deleteCompanyPhoto = vi.fn().mockReturnValue(of(undefined));
    deleteCompanyBackground = vi.fn().mockReturnValue(of(undefined));
    confirm = vi.fn().mockReturnValue(of(true));
    close = vi.fn();
    snackBarOpen = vi.fn();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  afterEach(() => vi.restoreAllMocks());

  it('lists the photos and the background of the company', async () => {
    await create();

    expect(getPhotos).toHaveBeenCalledWith(5);
    expect(root().querySelector('h2')?.textContent).toContain('Acme');
    expect(root().querySelectorAll('li.tile').length).toBe(2);
    expect(root().querySelector('button[aria-label="Usuń zdjęcie: Biuro"]')).not.toBeNull();
    expect(root().querySelector('button[aria-label="Usuń zdjęcie: Zdjęcie 2"]')).not.toBeNull();
    expect(root().querySelector('.background img')).not.toBeNull();
  });

  it('deletes a photo through the admin endpoint after confirmation and closes with true', async () => {
    await create();

    root().querySelector<HTMLButtonElement>('button[aria-label="Usuń zdjęcie: Biuro"]')!.click();

    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ destructive: true }));
    expect(deleteCompanyPhoto).toHaveBeenCalledWith(5, 1);
    expect(root().querySelectorAll('li.tile').length).toBe(1);
    fixture.componentInstance.close();
    expect(close).toHaveBeenCalledWith(true);
  });

  it('deletes the background', async () => {
    await create();
    const button = Array.from(root().querySelectorAll<HTMLButtonElement>('button')).find(b => b.textContent?.trim() === 'Usuń tło')!;

    button.click();

    expect(deleteCompanyBackground).toHaveBeenCalledWith(5);
    expect(root().textContent).toContain('Firma nie ma obrazu tła');
  });

  it('closes with false without changes and does nothing when the confirmation is declined', async () => {
    await create();
    confirm.mockReturnValue(of(false));

    root().querySelector<HTMLButtonElement>('button[aria-label="Usuń zdjęcie: Biuro"]')!.click();
    fixture.componentInstance.close();

    expect(deleteCompanyPhoto).not.toHaveBeenCalled();
    expect(close).toHaveBeenCalledWith(false);
  });

  it('shows the translated error when deleting fails', async () => {
    await create();
    deleteCompanyPhoto.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404, error: { message: 'Photo not found' } })));

    root().querySelector<HTMLButtonElement>('button[aria-label="Usuń zdjęcie: Biuro"]')!.click();

    expect(root().querySelector('[role="alert"]')?.textContent).toContain('Nie znaleziono zdjęcia');
    expect(fixture.componentInstance.busy()).toBe(false);
  });

  it('handles a company without media', async () => {
    getPhotos.mockReturnValue(of([]));
    await create(makeCompany(5, { offerBackground: null }));

    expect(root().textContent).toContain('Firma nie ma zdjęć');
    expect(root().textContent).toContain('Firma nie ma obrazu tła');
  });
});
