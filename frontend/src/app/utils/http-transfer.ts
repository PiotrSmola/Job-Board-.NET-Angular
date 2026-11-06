import { HttpClient, HttpErrorResponse, HttpEvent, HttpEventType } from '@angular/common/http';
import { Observable, OperatorFunction, catchError, filter, from, map, switchMap, throwError } from 'rxjs';
import { resolveDownloadFileName } from './content-disposition';
import { saveBlobAsFile } from './file-download';
import { parseBlobErrorBody } from './blob-error';

/** Postęp wysyłania w procentach. */
export interface UploadProgressEvent {
  type: 'progress';
  percent: number;
}

/**
 * Zamienia zdarzenia żądania z `reportProgress` na postęp wysyłania (0–100%) i – po odpowiedzi – wynik `done(body)`.
 * Pozostałe zdarzenia (np. Sent, nagłówki) są pomijane.
 */
export function mapUploadEvents<T, R>(done: (body: T) => R): OperatorFunction<HttpEvent<T>, UploadProgressEvent | R> {
  return source => source.pipe(
    map((event): UploadProgressEvent | R | null => {
      if (event.type === HttpEventType.UploadProgress) {
        const percent = event.total ? Math.round((event.loaded / event.total) * 100) : 0;
        return { type: 'progress', percent: Math.min(percent, 100) };
      }
      if (event.type === HttpEventType.Response && event.body) {
        return done(event.body);
      }
      return null;
    }),
    filter((event): event is UploadProgressEvent | R => event !== null)
  );
}

/**
 * Pobiera plik i zapisuje go na dysku. Nazwa z nagłówka Content-Disposition, w drugiej kolejności `fallbackName`.
 * Błąd ma treść sparsowaną z JSON (dla responseType 'blob' przychodzi jako Blob), więc działa getApiErrorMessage.
 * Rezygnacja z subskrypcji przerywa żądanie.
 */
export function downloadFile(http: HttpClient, url: string, fallbackName: string | null): Observable<void> {
  return http.get(url, { observe: 'response', responseType: 'blob' }).pipe(
    map(response => {
      const fileName = resolveDownloadFileName(response.headers.get('Content-Disposition'), fallbackName);
      saveBlobAsFile(response.body ?? new Blob(), fileName);
    }),
    catchError((error: unknown) => error instanceof HttpErrorResponse
      ? from(parseBlobErrorBody(error)).pipe(switchMap(parsed => throwError(() => parsed)))
      : throwError(() => error))
  );
}
