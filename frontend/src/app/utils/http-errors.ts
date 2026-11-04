import { HttpErrorResponse } from '@angular/common/http';

/** 401 i 403 obsługuje globalnie authInterceptor (wylogowanie / komunikat) – komponenty nie dublują komunikatu. */
export function isHandledByInterceptor(error: unknown): boolean {
  return error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403);
}
