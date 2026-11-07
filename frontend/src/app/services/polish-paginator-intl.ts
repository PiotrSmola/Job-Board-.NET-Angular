import { Injectable, Provider } from '@angular/core';
import { MatPaginatorIntl } from '@angular/material/paginator';

/** Polskie etykiety paginatora Material (podpowiedzi przycisków, „Na stronie”, zakres „1–10 z 42”). */
@Injectable()
export class PolishPaginatorIntl extends MatPaginatorIntl {
  override itemsPerPageLabel = 'Na stronie:';
  override nextPageLabel = 'Następna strona';
  override previousPageLabel = 'Poprzednia strona';
  override firstPageLabel = 'Pierwsza strona';
  override lastPageLabel = 'Ostatnia strona';

  override getRangeLabel = (page: number, pageSize: number, length: number): string => {
    if (length === 0 || pageSize === 0) {
      return `0 z ${length}`;
    }
    const total = Math.max(length, 0);
    const startIndex = page * pageSize;
    // strona poza zakresem (np. po usunięciu elementów) – jak w domyślnej implementacji Material
    const endIndex = startIndex < total ? Math.min(startIndex + pageSize, total) : startIndex + pageSize;
    return `${startIndex + 1}–${endIndex} z ${total}`;
  };
}

/**
 * Provider do `providers` komponentu z `<mat-paginator>`. Celowo nie w app.config: import z
 * `@angular/material/paginator` w paczce startowej wciąga cały paginator (z select i form-field) do initial bundle.
 */
export function providePolishPaginatorIntl(): Provider {
  return { provide: MatPaginatorIntl, useClass: PolishPaginatorIntl };
}
