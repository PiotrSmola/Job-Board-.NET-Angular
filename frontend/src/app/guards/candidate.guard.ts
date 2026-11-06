import { inject } from '@angular/core';
import { Router, CanActivateFn } from '@angular/router';
import { AuthService } from '../services/auth.service';

/** Strony kandydata (np. „Moje aplikacje”): pracodawca trafia do swojego panelu, admin – do panelu admina. */
export const candidateGuard: CanActivateFn = (_route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
  }

  // kolejność jak w navbarze: konto z rolą Admin trafia do panelu admina
  if (authService.isAdmin()) {
    return router.createUrlTree(['/admin']);
  }

  if (authService.isEmployer()) {
    return router.createUrlTree(['/employer']);
  }

  return true;
};
