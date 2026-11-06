import { HttpContextToken, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { translateForbiddenMessage } from '../utils/forbidden-messages';
import { parseBlobErrorBody } from '../utils/blob-error';

const API_PREFIX = '/api/';
// Na tych endpointach 401 oznacza złe dane logowania, a nie wygasłą sesję
const AUTH_ENDPOINTS = ['/api/auth/login', '/api/auth/register', '/api/auth/register-employer'];

/** Ustaw na true, gdy komponent sam obsługuje 403 i nie chce ogólnego komunikatu. */
export const SKIP_FORBIDDEN_NOTICE = new HttpContextToken<boolean>(() => false);

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const snackBar = inject(MatSnackBar);

  const isApiRequest = req.url.startsWith(API_PREFIX);
  if (!isApiRequest) {
    return next(req);
  }

  // getToken() zwraca null dla wygasłego tokena – nie wysyłamy go
  const token = authService.getToken();
  if (token) {
    req = req.clone({
      setHeaders: {
        Authorization: `Bearer ${token}`
      }
    });
  }

  const path = req.url.split('?')[0];
  const isAuthEndpoint = AUTH_ENDPOINTS.includes(path);

  const handleError = (error: unknown) => {
    if (error instanceof HttpErrorResponse && !isAuthEndpoint) {
      if (error.status === 401) {
        // Przekazujemy token, z którym poszło żądanie – serwis zignoruje 401 dla starej sesji
        authService.handleUnauthorized(token);
      } else if (error.status === 403 && !req.context.get(SKIP_FORBIDDEN_NOTICE)) {
        // Znany powód odmowy (np. firma niezatwierdzona) zamiast ogólnego komunikatu
        snackBar.open(translateForbiddenMessage(error.error?.message) ?? 'Brak uprawnień', 'OK', { duration: 5000 });
      }
    }
    return throwError(() => error);
  };

  return next(req).pipe(
    catchError((error: unknown) => {
      // Pobieranie plików: treść błędu przychodzi jako Blob – zamieniamy JSON z API na obiekt ({ message })
      if (req.responseType === 'blob' && error instanceof HttpErrorResponse && error.error instanceof Blob) {
        return from(parseBlobErrorBody(error)).pipe(switchMap(handleError));
      }
      return handleError(error);
    })
  );
};
