import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Subject, catchError, of, switchMap, tap } from 'rxjs';
import { Category } from '../../../models/taxonomy.model';
import { AdminService } from '../../../services/admin.service';
import { CategoryService } from '../../../services/category.service';
import { getApiErrorMessage, isHandledByInterceptor } from '../../../utils/http-errors';
import { CategoryDialogComponent } from '../category-dialog/category-dialog.component';

/** Zarządzanie kategoriami ofert w panelu admina: lista, dodawanie, edycja, usuwanie. */
@Component({
  selector: 'app-admin-categories-tab',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatTableModule, MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule],
  templateUrl: './admin-categories-tab.component.html',
  styleUrl: './admin-categories-tab.component.scss'
})
export class AdminCategoriesTabComponent {
  private categoryService = inject(CategoryService);
  private adminService = inject(AdminService);
  private dialog = inject(MatDialog);
  private snackBar = inject(MatSnackBar);

  readonly displayedColumns = ['name', 'slug', 'sortOrder', 'activeJobsCount', 'actions'];

  readonly categories = signal<Category[]>([]);
  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  /** Id kategorii, na której trwa usuwanie. */
  readonly busyId = signal<number | null>(null);

  private readonly load$ = new Subject<void>();

  constructor() {
    // switchMap anuluje poprzednie żądanie przy szybkich kolejnych odświeżeniach
    this.load$.pipe(
      tap(() => {
        this.loading.set(true);
        this.loadError.set(null);
      }),
      switchMap(() => this.categoryService.getCategories().pipe(
        catchError(error => {
          console.error('Error loading categories:', error);
          this.loadError.set(getApiErrorMessage(error, 'Błąd podczas ładowania kategorii'));
          return of(null);
        })
      )),
      takeUntilDestroyed()
    ).subscribe(categories => {
      if (categories) this.categories.set(categories);
      this.loading.set(false);
    });

    this.reload();
  }

  /** Przeładowuje listę (np. po powrocie do zakładki). */
  reload(): void {
    this.load$.next();
  }

  openAddDialog(): void {
    this.openDialog(null);
  }

  openEditDialog(category: Category): void {
    this.openDialog(category);
  }

  deleteCategory(category: Category): void {
    if (!confirm(`Czy na pewno chcesz usunąć kategorię „${category.name}”? Oferty z tej kategorii zostaną bez kategorii.`)) {
      return;
    }

    this.busyId.set(category.id);
    this.adminService.deleteCategory(category.id).subscribe({
      next: () => {
        this.busyId.set(null);
        this.notify('Kategoria została usunięta');
        this.reload();
      },
      error: error => {
        console.error('Error deleting category:', error);
        this.busyId.set(null);
        if (!isHandledByInterceptor(error)) {
          this.notify(getApiErrorMessage(error, 'Błąd podczas usuwania kategorii'));
        }
      }
    });
  }

  private openDialog(category: Category | null): void {
    this.dialog.open(CategoryDialogComponent, { width: '520px', maxWidth: '95vw', disableClose: true, data: category })
      .afterClosed().subscribe(saved => {
        if (saved) this.reload();
      });
  }

  private notify(message: string): void {
    this.snackBar.open(message, 'OK', { duration: 5000 });
  }
}
