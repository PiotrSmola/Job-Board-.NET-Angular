import { HttpResponse } from '@angular/common/http';
import { PaginatedResult, PaginationHeader } from '../models/pagination.model';

// Buduje PaginatedResult z odpowiedzi z nagłówkiem "Pagination"
export function toPaginatedResult<T>(response: HttpResponse<T[]>, pageSize: number): PaginatedResult<T> {
  const result = response.body ?? [];
  let pagination: PaginationHeader = {
    CurrentPage: 1,
    ItemsPerPage: pageSize,
    TotalItems: result.length,
    TotalPages: 1
  };

  const header = response.headers.get('Pagination');
  if (header) {
    try {
      pagination = JSON.parse(header);
    } catch {
      // Nieprawidłowy nagłówek – zostajemy przy wartościach domyślnych
    }
  }

  return { result, pagination };
}
