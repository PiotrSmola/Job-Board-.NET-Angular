import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CategoryService } from './category.service';
import { Category } from '../models/taxonomy.model';

describe('CategoryService', () => {
  let service: CategoryService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(CategoryService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('getCategories requests the public /api/categories list', () => {
    const categories: Category[] = [{ id: 1, name: 'Programowanie', slug: 'programowanie', sortOrder: 0, activeJobsCount: 12 }];
    let result: Category[] | undefined;

    service.getCategories().subscribe(c => (result = c));

    const req = http.expectOne('/api/categories');
    expect(req.request.method).toBe('GET');
    req.flush(categories);
    expect(result).toEqual(categories);
  });
});
