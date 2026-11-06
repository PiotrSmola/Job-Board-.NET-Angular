import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Observable, catchError, from, map, switchMap, throwError } from 'rxjs';
import { AdminStats, EmployerStats, StatsMode, StatsRange } from '../models/stats.model';
import { parseBlobErrorBody } from '../utils/blob-error';
import { resolveDownloadFileName } from '../utils/content-disposition';
import { saveBlobAsFile } from '../utils/file-download';

@Injectable({
  providedIn: 'root'
})
export class StatsService {
  private http = inject(HttpClient);

  getAdminStats(range: StatsRange): Observable<AdminStats> {
    return this.http.get<AdminStats>('/api/admin/stats', { params: this.toParams(range) });
  }

  getEmployerStats(range: StatsRange): Observable<EmployerStats> {
    return this.http.get<EmployerStats>('/api/employer/stats', { params: this.toParams(range) });
  }

  /**
   * Pobiera CSV z szeregiem czasowym i zapisuje go na dysku (nazwa z Content-Disposition, w drugiej kolejności
   * `statystyki-<od>_<do>.csv`). Błąd ma treść sparsowaną z JSON (dla `blob` przychodzi jako Blob),
   * więc działa getApiErrorMessage.
   */
  exportCsv(mode: StatsMode, range: StatsRange): Observable<void> {
    const url = mode === 'admin' ? '/api/admin/stats/export' : '/api/employer/stats/export';
    return this.http.get(url, { params: this.toParams(range), observe: 'response', responseType: 'blob' }).pipe(
      map(response => {
        const fileName = resolveDownloadFileName(
          response.headers.get('Content-Disposition'),
          `statystyki-${range.from}_${range.to}.csv`
        );
        saveBlobAsFile(response.body ?? new Blob(), fileName);
      }),
      catchError((error: unknown) => error instanceof HttpErrorResponse
        ? from(parseBlobErrorBody(error)).pipe(switchMap(parsed => throwError(() => parsed)))
        : throwError(() => error))
    );
  }

  private toParams(range: StatsRange): HttpParams {
    return new HttpParams().set('from', range.from).set('to', range.to);
  }
}
