import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Category } from '../../../models/taxonomy.model';
import { AdminService } from '../../../services/admin.service';
import { getApiErrorMessage, isHandledByInterceptor } from '../../../utils/http-errors';
import { slugify } from '../../../utils/job-labels';

export const CATEGORY_NAME_MAX = 100;
export const CATEGORY_SLUG_PATTERN = /^[a-z0-9-]+$/;

/** Dodawanie i edycja kategorii ofert przez admina. Zamyka się zapisaną kategorią. */
@Component({
  selector: 'app-category-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule
  ],
  template: `
    <h2 mat-dialog-title>{{ category ? 'Edytuj kategorię' : 'Dodaj kategorię' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="category-form" (ngSubmit)="onSubmit()" id="category-form">
        <mat-form-field appearance="outline">
          <mat-label>Nazwa kategorii</mat-label>
          <input matInput formControlName="name" [maxlength]="nameMax" required>
          @if (form.controls.name.invalid && form.controls.name.touched) {
            <mat-error>Nazwa jest wymagana (maks. {{ nameMax }} znaków)</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Slug (adres w filtrach)</mat-label>
          <input matInput formControlName="slug" [maxlength]="nameMax" required autocapitalize="off" spellcheck="false">
          <mat-hint>Małe litery, cyfry i myślniki, np. devops-i-chmura</mat-hint>
          @if (form.controls.slug.invalid && form.controls.slug.touched) {
            <mat-error>Slug jest wymagany i może zawierać tylko a-z, 0-9 oraz „-”</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Kolejność</mat-label>
          <input matInput type="number" min="0" step="1" formControlName="sortOrder">
          <mat-hint>Mniejsza liczba = wyżej na liście</mat-hint>
          @if (form.controls.sortOrder.invalid) {
            <mat-error>Podaj liczbę całkowitą nie mniejszą niż 0</mat-error>
          }
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close [disabled]="submitting()">Anuluj</button>
      <button mat-raised-button color="primary" type="submit" form="category-form" [disabled]="form.invalid || submitting()">
        @if (submitting()) {
          <mat-spinner diameter="20" aria-label="Zapisywanie"></mat-spinner>
        } @else {
          {{ category ? 'Zapisz' : 'Dodaj' }}
        }
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    mat-dialog-content { min-width: min(460px, 80vw); }
    .category-form { display: flex; flex-direction: column; gap: 8px; }
    .category-form mat-form-field { width: 100%; }
  `]
})
export class CategoryDialogComponent {
  private fb = inject(FormBuilder);
  private adminService = inject(AdminService);
  private dialogRef = inject(MatDialogRef<CategoryDialogComponent, Category>);
  private snackBar = inject(MatSnackBar);

  // null = dodawanie, kategoria = edycja
  readonly category: Category | null = inject<Category | null>(MAT_DIALOG_DATA, { optional: true }) ?? null;
  readonly nameMax = CATEGORY_NAME_MAX;
  readonly submitting = signal(false);

  readonly form = this.fb.nonNullable.group({
    name: [this.category?.name ?? '', [Validators.required, Validators.maxLength(CATEGORY_NAME_MAX), notBlank]],
    slug: [this.category?.slug ?? '', [Validators.required, Validators.maxLength(CATEGORY_NAME_MAX), Validators.pattern(CATEGORY_SLUG_PATTERN)]],
    sortOrder: [this.category?.sortOrder ?? 0, [Validators.required, Validators.min(0), Validators.pattern(/^\d+$/)]]
  });

  constructor() {
    // Slug podpowiadany z nazwy, dopóki użytkownik nie edytuje go ręcznie (przy edycji istniejącej kategorii się nie zmienia)
    if (!this.category) {
      this.form.controls.name.valueChanges.pipe(takeUntilDestroyed()).subscribe(name => {
        if (!this.form.controls.slug.dirty) {
          this.form.controls.slug.setValue(slugify(name).slice(0, CATEGORY_NAME_MAX));
        }
      });
    }
  }

  onSubmit(): void {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    const { name, slug, sortOrder } = this.form.getRawValue();
    const body = { name: name.trim(), slug: slug.trim(), sortOrder: Number(sortOrder) };
    const request = this.category
      ? this.adminService.updateCategory(this.category.id, body)
      : this.adminService.createCategory(body);

    request.subscribe({
      next: saved => {
        this.submitting.set(false);
        this.notify(this.category ? 'Kategoria zapisana' : 'Kategoria dodana');
        this.dialogRef.close(saved);
      },
      error: error => {
        console.error('Error saving category:', error);
        this.submitting.set(false);
        if (!isHandledByInterceptor(error)) {
          this.notify(getApiErrorMessage(error, 'Błąd podczas zapisywania kategorii'));
        }
      }
    });
  }

  private notify(message: string): void {
    this.snackBar.open(message, 'OK', { duration: 5000 });
  }
}

function notBlank(control: { value: string }): { blank: true } | null {
  return control.value.trim() ? null : { blank: true };
}
