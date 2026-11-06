import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of, throwError } from 'rxjs';
import { CategoryDialogComponent } from './category-dialog.component';
import { AdminService } from '../../../services/admin.service';
import { Category } from '../../../models/taxonomy.model';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

const EXISTING: Category = { id: 4, name: 'Testowanie', slug: 'testowanie', sortOrder: 3, activeJobsCount: 2 };

describe('CategoryDialogComponent', () => {
  let fixture: ComponentFixture<CategoryDialogComponent>;
  let component: CategoryDialogComponent;
  let createCategory: ReturnType<typeof vi.fn>;
  let updateCategory: ReturnType<typeof vi.fn>;
  let close: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;

  async function create(category: Category | null = null): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [CategoryDialogComponent],
      providers: [
        { provide: AdminService, useValue: { createCategory, updateCategory } },
        { provide: MatDialogRef, useValue: { close } },
        { provide: MAT_DIALOG_DATA, useValue: category },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(CategoryDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  const root = () => fixture.nativeElement as HTMLElement;

  beforeEach(() => {
    createCategory = vi.fn().mockReturnValue(of({ ...EXISTING, id: 9 }));
    updateCategory = vi.fn().mockReturnValue(of(EXISTING));
    close = vi.fn();
    snackBarOpen = vi.fn();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  afterEach(() => vi.restoreAllMocks());

  it('suggests the slug from the name until the user edits the slug', async () => {
    await create();

    component.form.controls.name.setValue('DevOps i chmura');
    expect(component.form.controls.slug.value).toBe('devops-i-chmura');

    component.form.controls.slug.setValue('moj-slug');
    component.form.controls.slug.markAsDirty();
    component.form.controls.name.setValue('Inna nazwa');
    expect(component.form.controls.slug.value).toBe('moj-slug');
  });

  it('does not change the slug of an existing category when the name is edited', async () => {
    await create(EXISTING);

    component.form.controls.name.setValue('Zupełnie inna');

    expect(component.form.controls.slug.value).toBe('testowanie');
  });

  it.each([
    ['name', ''],
    ['name', '   '],
    ['name', 'x'.repeat(101)],
    ['slug', ''],
    ['slug', 'Wielkie Litery'],
    ['slug', 'zł'],
    ['sortOrder', -1],
    ['sortOrder', 1.5]
  ])('rejects %s = %j', async (field, value) => {
    await create();
    component.form.patchValue({ name: 'Nazwa', slug: 'slug', sortOrder: 0, [field]: value });

    expect(component.form.valid).toBe(false);
  });

  it('creates a category with trimmed values and closes with the saved one', async () => {
    await create();
    component.form.setValue({ name: '  Analiza danych ', slug: 'analiza-danych', sortOrder: 4 });

    component.onSubmit();

    expect(createCategory).toHaveBeenCalledWith({ name: 'Analiza danych', slug: 'analiza-danych', sortOrder: 4 });
    expect(snackBarOpen.mock.calls[0][0]).toBe('Kategoria dodana');
    expect(close).toHaveBeenCalledWith({ ...EXISTING, id: 9 });
  });

  it('updates an existing category', async () => {
    await create(EXISTING);
    component.form.patchValue({ sortOrder: 7 });

    component.onSubmit();

    expect(updateCategory).toHaveBeenCalledWith(4, { name: 'Testowanie', slug: 'testowanie', sortOrder: 7 });
    expect(snackBarOpen.mock.calls[0][0]).toBe('Kategoria zapisana');
  });

  it('shows the translated message for a taken name or slug and stays open', async () => {
    createCategory.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 400, url: '/api/admin/categories', error: { message: 'Category name or slug is already taken' }
    })));
    await create();
    component.form.setValue({ name: 'Testowanie', slug: 'testowanie', sortOrder: 0 });

    component.onSubmit();

    expect(snackBarOpen.mock.calls[0][0]).toBe('Kategoria o takiej nazwie lub slugu już istnieje');
    expect(close).not.toHaveBeenCalled();
    expect(component.submitting()).toBe(false);
  });

  it('does not duplicate the interceptor notice for 403', async () => {
    createCategory.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 403, url: '/api/admin/categories' })));
    await create();
    component.form.setValue({ name: 'Nowa', slug: 'nowa', sortOrder: 0 });

    component.onSubmit();

    expect(snackBarOpen).not.toHaveBeenCalled();
  });

  it('does not send an invalid form and disables the submit button', async () => {
    await create();

    component.onSubmit();
    fixture.detectChanges();

    expect(createCategory).not.toHaveBeenCalled();
    expect(root().querySelector<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(true);
  });

  it('has an accessible title and labelled fields', async () => {
    await create();

    expect(root().querySelector('h2')?.textContent).toContain('Dodaj kategorię');
    const labels = Array.from(root().querySelectorAll('mat-label')).map(l => l.textContent?.trim());
    expect(labels).toEqual(['Nazwa kategorii', 'Slug (adres w filtrach)', 'Kolejność']);
  });
});
