import { HttpHeaders, HttpResponse } from '@angular/common/http';
import { toPaginatedResult } from './pagination';

function response<T>(body: T[] | null, headers?: Record<string, string>): HttpResponse<T[]> {
  return new HttpResponse<T[]>({ body, headers: new HttpHeaders(headers) });
}

describe('toPaginatedResult', () => {
  it('parses the Pagination header', () => {
    const header = JSON.stringify({ CurrentPage: 2, ItemsPerPage: 5, TotalItems: 12, TotalPages: 3 });

    const result = toPaginatedResult(response([1, 2, 3, 4, 5], { Pagination: header }), 5);

    expect(result.result).toEqual([1, 2, 3, 4, 5]);
    expect(result.pagination).toEqual({ CurrentPage: 2, ItemsPerPage: 5, TotalItems: 12, TotalPages: 3 });
  });

  it('falls back to defaults built from the body when the header is missing', () => {
    const result = toPaginatedResult(response(['a', 'b']), 10);

    expect(result.pagination).toEqual({ CurrentPage: 1, ItemsPerPage: 10, TotalItems: 2, TotalPages: 1 });
  });

  it('falls back to defaults when the header is not valid JSON', () => {
    const result = toPaginatedResult(response(['a'], { Pagination: '{not json' }), 25);

    expect(result.result).toEqual(['a']);
    expect(result.pagination).toEqual({ CurrentPage: 1, ItemsPerPage: 25, TotalItems: 1, TotalPages: 1 });
  });

  it('treats a null body as an empty list', () => {
    const result = toPaginatedResult(response<number>(null), 10);

    expect(result.result).toEqual([]);
    expect(result.pagination.TotalItems).toBe(0);
  });
});
