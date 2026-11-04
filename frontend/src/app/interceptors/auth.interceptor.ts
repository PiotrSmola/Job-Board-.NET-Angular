import { HttpContextToken, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

const API_PREFIX = '/api/';
// Na tych endpointach 401 oznacza złe dane logowania, a nie wygasłą sesję
const AUTH_ENDPOINTS = ['/api/auth/login', '/api/auth/register'];

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

  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && !isAuthEndpoint) {
        if (error.status === 401) {
          // Przekazujemy token, z którym poszło żądanie – serwis zignoruje 401 dla starej sesji
          authService.handleUnauthorized(token);
        } else if (error.status === 403 && !req.context.get(SKIP_FORBIDDEN_NOTICE)) {
          snackBar.open('Brak uprawnień', 'OK', { duration: 5000 });
        }
      }
      return throwError(() => error);
    })
  );
};
