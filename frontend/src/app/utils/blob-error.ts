import { HttpErrorResponse } from '@angular/common/http';

/**
 * Dla żądań z `responseType: 'blob'` treść błędu (np. `{ message }` z API) przychodzi jako Blob.
 * Zwraca kopię błędu z treścią sparsowaną z JSON; gdy to nie JSON – błąd bez zmian.
 */
export async function parseBlobErrorBody(error: HttpErrorResponse): Promise<HttpErrorResponse> {
  if (!(error.error instanceof Blob)) return error;

  try {
    const text = await readBlobAsText(error.error);
    return new HttpErrorResponse({
      error: text ? JSON.parse(text) : null,
      headers: error.headers,
      status: error.status,
      statusText: error.statusText,
      url: error.url ?? undefined
    });
  } catch {
    return error;
  }
}

// FileReader zamiast Blob.text() – działa także w jsdom (testy)
function readBlobAsText(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(typeof reader.result === 'string' ? reader.result : '');
    reader.onerror = () => reject(reader.error);
    reader.readAsText(blob);
  });
}
