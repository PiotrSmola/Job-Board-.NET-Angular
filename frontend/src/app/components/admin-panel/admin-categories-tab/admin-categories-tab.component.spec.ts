import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Observable, Subject, of, throwError } from 'rxjs';
import { AdminCategoriesTabComponent } from './admin-categories-tab.component';
import { AdminService } from '../../../services/admin.service';
import { CategoryService } from '../../../services/category.service';
import { Category } from '../../../models/taxonomy.model';
import { CategoryDialogComponent } from '../category-dialog/category-dialog.component';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

const CATEGORIES: Category[] = [
  { id: 1, name: 'Programowanie', slug: 'programowanie', sortOrder: 1, activeJobsCount: 12 },
  { id: 2, name: 'DevOps i chmura', slug: 'devops-i-chmura', sortOrder: 2, activeJobsCount: 0 }
];

describe('AdminCategoriesTabComponent', () => {
  let fixture: ComponentFixture<AdminCategoriesTabComponent>;
  let component: AdminCategoriesTabComponent;
  let getCategories: ReturnType<typeof vi.fn>;
  let deleteCategory: ReturnType<typeof vi.fn>;
  let dialogOpen: ReturnType<typeof vi.fn>;
  let snackBarOpen: ReturnType<typeof vi.fn>;
  let confirmSpy: ReturnType<typeof vi.spyOn>;

  const root = () => fixture.nativeElement as HTMLElement;

  async function create(categories: Observable<Category[]> = of(CATEGORIES)): Promise<void> {
    getCategories.mockReturnValue(categories);
    fixture = TestBed.createComponent(AdminCategoriesTabComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(async () => {
    getCategories = vi.fn();
    deleteCategory = vi.fn().mockReturnValue(of(undefined));
    dialogOpen = vi.fn().mockReturnValue({ afterClosed: () => of(undefined) });
    snackBarOpen = vi.fn();
    confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [AdminCategoriesTabComponent],
      providers: [
        { provide: CategoryService, useValue: { getCategories } },
        { provide: AdminService, useValue: { deleteCategory } },
        { provide: MatDialog, useValue: { open: dialogOpen } },
        { provide: MatSnackBar, useValue: { open: snackBarOpen } }
      ]
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  it('lists categories with slug, order and the number of active offers', async () => {
    await create();

    const rows = Array.from(root().querySelectorAll('tr.table-row')).map(r => r.textContent?.replace(/\s+/g, ' ').trim());
    expect(rows.length).toBe(2);
    expect(rows[0]).toContain('Programowanie');
    expect(rows[0]).toContain('programowanie');
    expect(rows[0]).toContain('12');
    expect(root().querySelector('table')?.getAttribute('aria-label')).toBe('Kategorie ofert');
  });

  it('shows an empty state without categories', async () => {
    await create(of([]));

    expect(root().querySelector('.empty-state h3')?.textContent).toContain('Brak kategorii');
    expect(root().querySelector('table')).toBeNull();
  });

  it('shows an error with a retry button when loading fails', async () => {
    await create(throwError(() => new HttpErrorResponse({ status: 500, url: '/api/categories' })));

    expect(root().querySelector('.empty-state h3')?.textContent).toContain('Błąd podczas ładowania kategorii');
    expect(root().querySelector('.empty-state')?.getAttribute('role')).toBe('alert');

    getCategories.mockReturnValue(of(CATEGORIES));
    root().querySelector<HTMLButtonElement>('.empty-state button')!.click();
    fixture.detectChanges();

    expect(root().querySelectorAll('tr.table-row').length).toBe(2);
  });

  it('cancels a pending load when it is reloaded (switchMap) and ignores the stale response', async () => {
    const first = new Subject<Category[]>();
    await create(first);

    getCategories.mockReturnValue(of([CATEGORIES[1]]));
    component.reload();
    first.next(CATEGORIES);
    fixture.detectChanges();

    expect(component.categories()).toEqual([CATEGORIES[1]]);
    expect(first.observed).toBe(false);
  });

  it('unsubscribes from a pending load when destroyed', async () => {
    const pending = new Subject<Category[]>();
    await create(pending);
    expect(pending.observed).toBe(true);

    fixture.destroy();

    expect(pending.observed).toBe(false);
  });

  describe('dialog', () => {
    it('opens the add dialog and reloads after a save', async () => {
      await create();
      dialogOpen.mockReturnValue({ afterClosed: () => of(CATEGORIES[0]) });
      const calls = getCategories.mock.calls.length;

      component.openAddDialog();

      expect(dialogOpen).toHaveBeenCalledWith(CategoryDialogComponent, expect.objectContaining({ data: null }));
      expect(getCategories.mock.calls.length).toBe(calls + 1);
    });

    it('opens the edit dialog with the category and does not reload after cancel', async () => {
      await create();
      const calls = getCategories.mock.calls.length;

      component.openEditDialog(CATEGORIES[0]);

      expect(dialogOpen).toHaveBeenCalledWith(CategoryDialogComponent, expect.objectContaining({ data: CATEGORIES[0] }));
      expect(getCategories.mock.calls.length).toBe(calls);
    });
  });

  describe('delete', () => {
    it('asks for confirmation mentioning that offers lose the category, deletes and reloads', async () => {
      await create();
      const calls = getCategories.mock.calls.length;

      component.deleteCategory(CATEGORIES[0]);

      expect(confirmSpy.mock.calls[0][0]).toContain('Programowanie');
      expect(confirmSpy.mock.calls[0][0]).toContain('bez kategorii');
      expect(deleteCategory).toHaveBeenCalledWith(1);
      expect(snackBarOpen.mock.calls[0][0]).toBe('Kategoria została usunięta');
      expect(getCategories.mock.calls.length).toBe(calls + 1);
    });

    it('does nothing when the confirmation is declined', async () => {
      await create();
      confirmSpy.mockReturnValue(false);

      component.deleteCategory(CATEGORIES[0]);

      expect(deleteCategory).not.toHaveBeenCalled();
    });

    it('shows a message and clears the busy state when deleting fails', async () => {
      await create();
      deleteCategory.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404, url: '/x', error: { message: 'Category not found' } })));

      component.deleteCategory(CATEGORIES[0]);

      expect(snackBarOpen.mock.calls[0][0]).toBe('Kategoria nie została znaleziona');
      expect(component.busyId()).toBeNull();
    });

    it('gives the icon buttons accessible names with the category', async () => {
      await create();

      const labels = Array.from(root().querySelectorAll('td button')).map(b => b.getAttribute('aria-label'));
      expect(labels).toEqual([
        'Edytuj kategorię Programowanie', 'Usuń kategorię Programowanie',
        'Edytuj kategorię DevOps i chmura', 'Usuń kategorię DevOps i chmura'
      ]);
    });
  });
});
