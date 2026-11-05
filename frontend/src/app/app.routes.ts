import { Routes } from '@angular/router';
import { authGuard } from './guards/auth.guard';
import { adminGuard } from './guards/admin.guard';
import { employerGuard } from './guards/employer.guard';

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
    path: 'my-applications',
    loadComponent: () => import('./components/my-applications/my-applications.component').then(m => m.MyApplicationsComponent),
    canActivate: [authGuard]
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
