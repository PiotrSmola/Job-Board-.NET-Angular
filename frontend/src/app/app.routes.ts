import { Routes } from '@angular/router';
import { adminGuard } from './guards/admin.guard';
import { employerGuard } from './guards/employer.guard';
import { candidateGuard } from './guards/candidate.guard';
import { authGuard } from './guards/auth.guard';
import { DYNAMIC_TITLE } from './utils/page-title';

// title – tytuł strony (AppTitleStrategy dopisuje „– Job Board”); DYNAMIC_TITLE – widok ustawia tytuł z danych
export const routes: Routes = [
  {
    path: '',
    redirectTo: '/jobs',
    pathMatch: 'full'
  },
  {
    path: 'jobs',
    title: 'Oferty pracy',
    loadComponent: () => import('./components/job-list/job-list.component').then(m => m.JobListComponent)
  },
  {
    path: 'jobs/:id',
    title: 'Oferta pracy',
    data: { [DYNAMIC_TITLE]: true },
    loadComponent: () => import('./components/job-detail/job-detail.component').then(m => m.JobDetailComponent)
  },
  {
    path: 'companies',
    title: 'Firmy',
    loadComponent: () => import('./components/company-list/company-list.component').then(m => m.CompanyListComponent)
  },
  {
    path: 'companies/:id',
    title: 'Profil firmy',
    data: { [DYNAMIC_TITLE]: true },
    loadComponent: () => import('./components/company-detail/company-detail.component').then(m => m.CompanyDetailComponent)
  },
  {
    path: 'login',
    title: 'Logowanie',
    loadComponent: () => import('./components/login/login.component').then(m => m.LoginComponent)
  },
  {
    path: 'register',
    title: 'Rejestracja',
    loadComponent: () => import('./components/register/register.component').then(m => m.RegisterComponent)
  },
  {
    path: 'forgot-password',
    title: 'Nie pamiętasz hasła?',
    loadComponent: () => import('./components/forgot-password/forgot-password.component').then(m => m.ForgotPasswordComponent)
  },
  {
    path: 'reset-password',
    title: 'Ustaw nowe hasło',
    loadComponent: () => import('./components/reset-password/reset-password.component').then(m => m.ResetPasswordComponent)
  },
  {
    path: 'confirm-email',
    title: 'Potwierdzenie adresu e-mail',
    loadComponent: () => import('./components/confirm-email/confirm-email.component').then(m => m.ConfirmEmailComponent)
  },
  {
    path: 'account',
    title: 'Moje konto',
    loadComponent: () => import('./components/account/account.component').then(m => m.AccountComponent),
    canActivate: [authGuard]
  },
  {
    path: 'saved-jobs',
    title: 'Zapisane oferty',
    loadComponent: () => import('./components/saved-jobs/saved-jobs.component').then(m => m.SavedJobsComponent),
    canActivate: [candidateGuard]
  },
  {
    path: 'my-applications',
    title: 'Moje aplikacje',
    loadComponent: () => import('./components/my-applications/my-applications.component').then(m => m.MyApplicationsComponent),
    canActivate: [candidateGuard]
  },
  {
    path: 'employer',
    title: 'Panel pracodawcy',
    loadComponent: () => import('./components/employer-panel/employer-panel.component').then(m => m.EmployerPanelComponent),
    canActivate: [employerGuard]
  },
  {
    path: 'admin',
    title: 'Panel admina',
    loadComponent: () => import('./components/admin-panel/admin-panel.component').then(m => m.AdminPanelComponent),
    canActivate: [adminGuard]
  },
  {
    path: '**',
    redirectTo: '/jobs'
  }
];
