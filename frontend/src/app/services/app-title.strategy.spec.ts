import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { Router, TitleStrategy, provideRouter } from '@angular/router';
import { AppTitleStrategy } from './app-title.strategy';

@Component({ template: '' })
class PageComponent {}

describe('AppTitleStrategy', () => {
  let router: Router;
  let title: Title;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'jobs', title: 'Oferty pracy', component: PageComponent },
          {
            path: 'admin',
            title: 'Panel admina',
            children: [
              { path: '', component: PageComponent },
              { path: 'stats', title: 'Statystyki', component: PageComponent }
            ]
          },
          { path: 'untitled', component: PageComponent }
        ]),
        { provide: TitleStrategy, useClass: AppTitleStrategy }
      ]
    });
    router = TestBed.inject(Router);
    title = TestBed.inject(Title);
  });

  it('formats the route title as "<title> – Job Board"', async () => {
    await router.navigateByUrl('/jobs');

    expect(title.getTitle()).toBe('Oferty pracy – Job Board');
  });

  it('uses the deepest route with a title', async () => {
    await router.navigateByUrl('/admin/stats');
    expect(title.getTitle()).toBe('Statystyki – Job Board');

    await router.navigateByUrl('/admin');
    expect(title.getTitle()).toBe('Panel admina – Job Board');
  });

  it('falls back to the application name for a route without a title', async () => {
    await router.navigateByUrl('/jobs');
    await router.navigateByUrl('/untitled');

    expect(title.getTitle()).toBe('Job Board');
  });
});
