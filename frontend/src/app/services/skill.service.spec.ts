import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SkillService } from './skill.service';

describe('SkillService', () => {
  let service: SkillService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(SkillService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends the search phrase and the default limit', () => {
    service.suggest('ang').subscribe();

    const req = http.expectOne(r => r.url === '/api/skills');
    expect(req.request.params.get('search')).toBe('ang');
    expect(req.request.params.get('limit')).toBe('10');
    req.flush([{ name: 'Angular', jobsCount: 4 }]);
  });

  it('omits an empty phrase (most popular skills)', () => {
    service.suggest('').subscribe();

    const req = http.expectOne(r => r.url === '/api/skills');
    expect(req.request.params.has('search')).toBe(false);
    req.flush([]);
  });

  it('clamps the limit to the 1-20 range accepted by the API', () => {
    service.suggest('a', 100).subscribe();
    service.suggest('b', 0).subscribe();

    const limits = http.match(r => r.url === '/api/skills').map(r => {
      r.flush([]);
      return r.request.params.get('limit');
    });
    expect(limits).toEqual(['20', '1']);
  });

  it('encodes special characters in the phrase', () => {
    service.suggest('C# & .NET').subscribe();

    const req = http.expectOne(r => r.url === '/api/skills');
    expect(req.request.urlWithParams).toContain('search=C%23%20%26%20.NET');
    req.flush([]);
  });
});
