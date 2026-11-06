import { Routes } from '@angular/router';
import { adminGuard } from './guards/admin.guard';
import { employerGuard } from './guards/employer.guard';
import { candidateGuard } from './guards/candidate.guard';
import { authGuard } from './guards/auth.guard';

export const routes: Routes = [
  {
    path: '',
    redirectTo: '/jobs',
    pathMatch: 'full'
  },
  {
    path: 'jobs',
    loadComponent: () => import('./components/job-list/job-list.component').then(m => m.JobListComponent)
  },
  {
    path: 'jobs/:id',
    loadComponent: () => import('./components/job-detail/job-detail.component').then(m => m.JobDetailComponent)
  },
  {
    path: 'companies',
    loadComponent: () => import('./components/company-list/company-list.component').then(m => m.CompanyListComponent)
  },
  {
    path: 'companies/:id',
    loadComponent: () => import('./components/company-detail/company-detail.component').then(m => m.CompanyDetailComponent)
  },
  {
    path: 'login',
    loadComponent: () => import('./components/login/login.component').then(m => m.LoginComponent)
  },
  {
    path: 'register',
    loadComponent: () => import('./components/register/register.component').then(m => m.RegisterComponent)
  },
  {
    path: 'forgot-password',
    loadComponent: () => import('./components/forgot-password/forgot-password.component').then(m => m.ForgotPasswordComponent)
  },
  {
    path: 'reset-password',
    loadComponent: () => import('./components/reset-password/reset-password.component').then(m => m.ResetPasswordComponent)
  },
  {
    path: 'confirm-email',
    loadComponent: () => import('./components/confirm-email/confirm-email.component').then(m => m.ConfirmEmailComponent)
  },
  {
    path: 'account',
    loadComponent: () => import('./components/account/account.component').then(m => m.AccountComponent),
    canActivate: [authGuard]
  },
  {
    path: 'saved-jobs',
    loadComponent: () => import('./components/saved-jobs/saved-jobs.component').then(m => m.SavedJobsComponent),
    canActivate: [candidateGuard]
  },
  {
    path: 'my-applications',
    loadComponent: () => import('./components/my-applications/my-applications.component').then(m => m.MyApplicationsComponent),
    canActivate: [candidateGuard]
  },
  {
    path: 'employer',
    loadComponent: () => import('./components/employer-panel/employer-panel.component').then(m => m.EmployerPanelComponent),
    canActivate: [employerGuard]
  },
  {
    path: 'admin',
    loadComponent: () => import('./components/admin-panel/admin-panel.component').then(m => m.AdminPanelComponent),
    canActivate: [adminGuard]
  },
  {
    path: '**',
    redirectTo: '/jobs'
  }
];
