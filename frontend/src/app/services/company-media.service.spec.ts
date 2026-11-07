import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CompanyMediaService } from './company-media.service';
import { CompanyPhoto } from '../models/company-media.model';

describe('CompanyMediaService', () => {
  let service: CompanyMediaService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(CompanyMediaService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reads the public gallery of a company', () => {
    const photos: CompanyPhoto[] = [{ id: 3, caption: 'Biuro', width: 1200, height: 800, url: '/api/companies/7/photos/3/file' }];
    let result: CompanyPhoto[] | undefined;

    service.getPhotos(7).subscribe(list => result = list);
    const req = http.expectOne('/api/companies/7/photos');
    expect(req.request.method).toBe('GET');
    req.flush(photos);

    expect(result).toEqual(photos);
  });
});
