import { TestBed } from '@angular/core/testing';
import { HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import {
  BackgroundUploadEvent,
  EmployerMediaService,
  IMAGE_MAX_FILE_SIZE,
  PhotoUploadEvent,
  selectPhotoFiles,
  validateImageFile
} from './employer-media.service';
import { CompanyMediaService } from './company-media.service';
import { BackgroundFocus, CompanyPhoto } from '../models/company-media.model';

function file(name: string, size = 100, type = name.endsWith('.png') ? 'image/png' : 'image/jpeg'): File {
  const value = new File(['x'], name, { type });
  Object.defineProperty(value, 'size', { value: size });
  return value;
}

const photo = (id: number): CompanyPhoto => ({ id, caption: null, width: 800, height: 600, url: `/api/companies/3/photos/${id}/file` });

describe('EmployerMediaService', () => {
  let service: EmployerMediaService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(EmployerMediaService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('getPhotos requests the public gallery of the company', () => {
    let result: CompanyPhoto[] = [];
    service.getPhotos(3).subscribe(photos => result = photos);
    const req = http.expectOne('/api/companies/3/photos');
    expect(req.request.method).toBe('GET');
    req.flush([photo(1)]);
    expect(result).toEqual([photo(1)]);
  });

  it('getPhotos reuses the public read of CompanyMediaService (one implementation)', () => {
    const companyMedia = TestBed.inject(CompanyMediaService);
    const spy = vi.spyOn(companyMedia, 'getPhotos');
    service.getPhotos(3).subscribe();
    expect(spy).toHaveBeenCalledWith(3);
    http.expectOne('/api/companies/3/photos').flush([]);
  });

  it('loadImage requests the image as a blob', () => {
    service.loadImage('/api/companies/3/photos/1/file').subscribe();
    const req = http.expectOne('/api/companies/3/photos/1/file');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['x']));
  });

  it('uploadPhotos posts all files in the "files" field and reports progress, then the gallery', () => {
    const events: PhotoUploadEvent[] = [];
    service.uploadPhotos([file('a.png'), file('b.jpg')]).subscribe(event => events.push(event));

    const req = http.expectOne('/api/employer/company/photos');
    expect(req.request.method).toBe('POST');
    expect(req.request.reportProgress).toBe(true);
    const body = req.request.body as FormData;
    expect(body.getAll('files').map(f => (f as File).name)).toEqual(['a.png', 'b.jpg']);

    req.event({ type: HttpEventType.UploadProgress, loaded: 50, total: 200 });
    req.flush([photo(1), photo(2)]);

    expect(events).toEqual([{ type: 'progress', percent: 25 }, { type: 'done', photos: [photo(1), photo(2)] }]);
  });

  it('updateCaption patches the caption', () => {
    service.updateCaption(5, 'Biuro').subscribe();
    const req = http.expectOne('/api/employer/company/photos/5');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ caption: 'Biuro' });
    req.flush(photo(5));
  });

  it('reorderPhotos puts the full id list', () => {
    service.reorderPhotos([2, 1]).subscribe();
    const req = http.expectOne('/api/employer/company/photos/order');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ photoIds: [2, 1] });
    req.flush([photo(2), photo(1)]);
  });

  it('deletePhoto sends DELETE', () => {
    service.deletePhoto(5).subscribe();
    const req = http.expectOne('/api/employer/company/photos/5');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('uploadBackground puts one file in the "file" field and ends with the settings', () => {
    const events: BackgroundUploadEvent[] = [];
    service.uploadBackground(file('bg.jpg')).subscribe(event => events.push(event));

    const req = http.expectOne('/api/employer/company/offer-background');
    expect(req.request.method).toBe('PUT');
    expect(((req.request.body as FormData).get('file') as File).name).toBe('bg.jpg');
    const background = { url: '/api/companies/3/offer-background/file?v=abc', dim: 75, focus: BackgroundFocus.Center };
    req.flush(background);

    expect(events).toEqual([{ type: 'done', background }]);
  });

  it('updateBackground patches dim and focus', () => {
    service.updateBackground({ dim: 80, focus: BackgroundFocus.Top }).subscribe();
    const req = http.expectOne('/api/employer/company/offer-background');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ dim: 80, focus: BackgroundFocus.Top });
    req.flush({ url: null, dim: 80, focus: BackgroundFocus.Top });
  });

  it('deleteBackground sends DELETE', () => {
    service.deleteBackground().subscribe();
    const req = http.expectOne('/api/employer/company/offer-background');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});

describe('validateImageFile', () => {
  it('accepts PNG and JPEG up to 5 MB', () => {
    expect(validateImageFile(file('a.png'))).toBeNull();
    expect(validateImageFile(file('a.JPEG', IMAGE_MAX_FILE_SIZE))).toBeNull();
  });

  it('rejects other types, empty and too large files', () => {
    expect(validateImageFile(file('a.gif', 10, 'image/gif'))).toContain('PNG lub JPEG');
    expect(validateImageFile(file('a.png', 10, 'image/svg+xml'))).toContain('PNG lub JPEG');
    expect(validateImageFile(file('a.png', 0))).toContain('pusty');
    expect(validateImageFile(file('a.png', IMAGE_MAX_FILE_SIZE + 1))).toContain('5 MB');
  });
});

describe('selectPhotoFiles', () => {
  it('accepts files until the gallery limit of 20 and reports the rest by name', () => {
    const { accepted, errors } = selectPhotoFiles([file('a.png'), file('b.png'), file('c.png')], 18);

    expect(accepted.map(f => f.name)).toEqual(['a.png', 'b.png']);
    expect(errors).toEqual(['c.png: Firma może mieć maksymalnie 20 zdjęć']);
  });

  it('keeps one upload within 25 MB', () => {
    const big = (name: string) => file(name, 5 * 1024 * 1024);
    const { accepted, errors } = selectPhotoFiles([big('1.jpg'), big('2.jpg'), big('3.jpg'), big('4.jpg'), big('5.jpg'), big('6.jpg')], 0);

    expect(accepted).toHaveLength(5);
    expect(errors[0]).toContain('6.jpg: Jednorazowo można wysłać do 25 MB');
  });

  it('skips invalid files without counting them', () => {
    const { accepted, errors } = selectPhotoFiles([file('doc.pdf', 10, 'application/pdf'), file('a.jpg')], 19);

    expect(accepted.map(f => f.name)).toEqual(['a.jpg']);
    expect(errors[0]).toContain('doc.pdf');
  });
});
