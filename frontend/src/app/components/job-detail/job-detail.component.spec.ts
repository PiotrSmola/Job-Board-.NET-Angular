import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Observable, of, throwError } from 'rxjs';
import { JobDetailComponent, heroStyle, isSafeApiUrl } from './job-detail.component';
import { JobApplyComponent } from './job-apply.component';
import { JobService } from '../../services/job.service';
import { CompanyService } from '../../services/company.service';
import { JobApplicationService } from '../../services/job-application.service';
import { AuthService } from '../../services/auth.service';
import { AccountService } from '../../services/account.service';
import { SavedJobsService } from '../../services/saved-jobs.service';
import { PageTitleService } from '../../services/page-title.service';
import { ContractTypes, Currency, JobOffer, JobOfferStatus, SalaryPeriod, SeniorityLevel, WorkType } from '../../models/job-offer.model';
import { BackgroundFocus } from '../../models/company-media.model';
import { offerHeroMask } from '../../utils/offer-hero';
import { makeJob, jobPage } from '../../../testing/job-fixtures';
import { makeCompany } from '../../../testing/company-fixtures';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

const JOB: JobOffer = makeJob(5, {
  title: 'Angular Developer', company: 'Acme', companyId: 1, description: 'Opis\n- punkt', location: 'Rzeszów',
  salaryMin: 18000, salaryMax: 24000, salaryCurrency: Currency.PLN, salaryPeriod: SalaryPeriod.Month,
  createdAt: new Date(2026, 10, 4)
});

const squash = (text: string | null | undefined) => (text ?? '').replace(/[\s  ]+/g, ' ').trim();

describe('heroStyle', () => {
  it('uses the API image with the focal point and the dim of the company', () => {
    expect(heroStyle({ url: '/api/companies/1/offer-background/file?v=abc123', dim: 70, focus: BackgroundFocus.Top })).toEqual({
      image: 'url("/api/companies/1/offer-background/file?v=abc123")', position: 'center top', mask: offerHeroMask(70)
    });
    expect(heroStyle({ url: '/api/x', dim: 75, focus: BackgroundFocus.Bottom })?.position).toBe('center bottom');
    expect(heroStyle({ url: '/api/x', dim: 75, focus: BackgroundFocus.Center })?.position).toBe('center');
  });

  it('uses the shared mask with the dim clamped to 55–95', () => {
    expect(heroStyle({ url: '/api/x', dim: 10, focus: BackgroundFocus.Center })?.mask).toBe(offerHeroMask(55));
    expect(heroStyle({ url: '/api/x', dim: 120, focus: BackgroundFocus.Center })?.mask).toBe(offerHeroMask(95));
  });

  it('falls back to the plain hero without a background or for a URL outside /api/', () => {
    expect(heroStyle(null)).toBeNull();
    expect(heroStyle(undefined)).toBeNull();
    expect(heroStyle({ url: 'https://evil.example/x.png', dim: 70, focus: BackgroundFocus.Center })).toBeNull();
  });

  it.each([
    '/api/x") , url("https://evil.example/a.png',
    '/api/x\'',
    '/api/x)',
    '/api/x y',
    '/api/../../etc',
    '//evil.example/api/x',
    'javascript:alert(1)',
    '/api/x\\'
  ])('rejects the unsafe URL %s', url => {
    expect(isSafeApiUrl(url)).toBe(false);
  });
});

describe('JobDetailComponent', () => {
  let fixture: ComponentFixture<JobDetailComponent>;
  let component: JobDetailComponent;
  let getJob: ReturnType<typeof vi.fn>;
  let getJobs: ReturnType<typeof vi.fn>;
  let getCompany: ReturnType<typeof vi.fn>;
  let checkIfApplied: ReturnType<typeof vi.fn>;
  let setTitle: ReturnType<typeof vi.fn>;
  let savedJobsEnabled: boolean;
  let authenticated: boolean;
  let admin: boolean;
  let employer: boolean;

  const root = () => fixture.nativeElement as HTMLElement;
  const text = () => {
    fixture.detectChanges();
    return squash(root().textContent);
  };
  const hero = () => root().querySelector<HTMLElement>('section.hero')!;
  const facts = () => Array.from(root().querySelectorAll('.facts .fact'))
    .map(f => squash(`${f.querySelector('dt')?.textContent} ${f.querySelector('dd')?.textContent}`));

  function create(job: Observable<JobOffer> = of(JOB)): void {
    getJob.mockReturnValue(job);
    fixture = TestBed.createComponent(JobDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function httpError(status: number, error: unknown = null): HttpErrorResponse {
    return new HttpErrorResponse({ status, statusText: 'Error', url: '/api/x', error });
  }

  beforeEach(async () => {
    getJob = vi.fn();
    getJobs = vi.fn().mockReturnValue(of(jobPage(0, 0)));
    getCompany = vi.fn().mockReturnValue(of(makeCompany(1, { name: 'Acme', description: 'Software house', activeJobsCount: 3 })));
    checkIfApplied = vi.fn().mockReturnValue(of({ hasApplied: false }));
    setTitle = vi.fn();
    savedJobsEnabled = false;
    authenticated = true;
    admin = false;
    employer = false;
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [JobDetailComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id: '5' })) } },
        { provide: JobService, useValue: { getJob, getJobs } },
        { provide: CompanyService, useValue: { getCompany } },
        { provide: JobApplicationService, useValue: { checkIfApplied, applyToJob: vi.fn(), getApplication: vi.fn() } },
        { provide: AccountService, useValue: { getDocuments: () => of([]) } },
        {
          provide: SavedJobsService,
          useValue: { enabled: () => savedJobsEnabled, isSaved: (id: number) => id === 5, isPending: () => false, ensureLoaded: vi.fn(), toggle: vi.fn() }
        },
        { provide: AuthService, useValue: { isAuthenticated: () => authenticated, isAdmin: () => admin, isEmployer: () => employer } },
        { provide: PageTitleService, useValue: { set: setTitle } },
        { provide: MatSnackBar, useValue: { open: vi.fn() } }
      ]
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  it('loads the job, sets the page title and renders the inline application section (no dialog)', () => {
    create();

    expect(getJob).toHaveBeenCalledWith(5);
    expect(setTitle).toHaveBeenCalledWith('Angular Developer');
    expect(root().querySelector('h1')?.textContent).toBe('Angular Developer');
    expect(root().querySelector('app-job-apply section#apply')).not.toBeNull();
    expect(checkIfApplied).toHaveBeenCalledWith(5);
  });

  describe('hero', () => {
    it('shows the company background behind a mask with the dim and focal point from the company settings', () => {
      create(of({ ...JOB, companyBackground: { url: '/api/companies/1/offer-background/file?v=ff00', dim: 80, focus: BackgroundFocus.Bottom } }));

      const section = hero();
      expect(section.classList.contains('has-image')).toBe(true);
      expect(section.style.backgroundImage).toContain('/api/companies/1/offer-background/file?v=ff00');
      expect(section.style.backgroundPosition).toBe('center bottom');
      expect(component.hero()?.mask).toBe(offerHeroMask(80));
    });

    it('is plain (no image, no mask) without a company background', () => {
      create();

      expect(hero().classList.contains('has-image')).toBe(false);
      expect(hero().style.backgroundImage).toBe('');
    });

    it('ignores a background URL that does not point to the API', () => {
      create(of({ ...JOB, companyBackground: { url: 'https://evil.example/x.png', dim: 80, focus: BackgroundFocus.Center } }));

      expect(hero().classList.contains('has-image')).toBe(false);
      expect(hero().style.backgroundImage).toBe('');
    });

    it('shows the company link, the city and the posting date', () => {
      create();

      const link = root().querySelector<HTMLAnchorElement>('.hero a.company-link')!;
      expect(link.textContent?.trim()).toBe('Acme');
      expect(link.getAttribute('href')).toBe('/companies/1');
      expect(squash(root().querySelector('.hero-meta')?.textContent)).toBe('Acme · Rzeszów · dodano 4 listopada');
    });

    it('links back to the list with the filters the user came from', () => {
      const router = TestBed.inject(Router);
      vi.spyOn(router, 'currentNavigation').mockReturnValue({
        extras: { state: { jobsQuery: { seniority: '1', page: '2', bogus: 'x', orderBy: 'hax' } } }
      } as never);
      create();

      expect(root().querySelector('a.back')?.getAttribute('href')).toBe('/jobs?seniority=1&page=2');
      expect(root().querySelector('a.back')?.textContent).toContain('Wróć do wyników');
    });

    it('links back to the plain list without navigation state', () => {
      create();

      expect(root().querySelector('a.back')?.getAttribute('href')).toBe('/jobs');
    });
  });

  describe('facts', () => {
    it('shows the salary in a full row with the period, then contract, level, work mode, category and expiry', () => {
      create(of({
        ...JOB, contractTypes: ContractTypes.B2B, seniority: SeniorityLevel.Senior, workType: WorkType.Remote,
        categoryId: 1, categoryName: 'Programowanie', categorySlug: 'programowanie', expiresAt: new Date(2026, 10, 30, 12).toISOString()
      }));

      expect(facts()).toEqual([
        'Wynagrodzenie 18 000–24 000 PLN miesięcznie',
        'Umowa B2B',
        'Poziom Senior',
        'Tryb pracy Zdalnie',
        'Kategoria Programowanie',
        'Aktualna do 30 listopada'
      ]);
      expect(root().querySelector('.facts .salary')).not.toBeNull();
      const category = root().querySelector<HTMLAnchorElement>('.category-link')!;
      expect(category.getAttribute('href')).toBe('/jobs?category=programowanie');
    });

    it('shows "Nieokreślona" without a period and skips missing facts', () => {
      create(of({ ...JOB, salaryMin: null, salaryMax: null, workType: WorkType.Onsite }));

      expect(facts()).toEqual(['Wynagrodzenie Nieokreślona', 'Tryb pracy Stacjonarnie']);
    });
  });

  describe('sections', () => {
    it('renders the sections as rows with h2 titles and the description as plain text', () => {
      create(of({ ...JOB, skills: ['Angular', 'RxJS'] }));

      const titles = Array.from(root().querySelectorAll('.row h2')).map(h => h.textContent?.trim());
      expect(titles).toEqual(['Opis stanowiska', 'Technologie', 'O firmie', 'Twoja aplikacja']);
      expect(root().querySelector('.description')?.textContent).toBe('Opis\n- punkt');
      expect(root().querySelector('.skills')?.textContent).toBe('Angular, RxJS');
      expect(root().querySelectorAll('h1').length).toBe(1);
    });

    it('describes the company and links to its profile with the number of offers', () => {
      create();

      expect(getCompany).toHaveBeenCalledWith(1);
      expect(root().querySelector('.about')?.textContent).toBe('Software house');
      const link = root().querySelector<HTMLAnchorElement>('.profile-link')!;
      expect(link.textContent?.trim()).toBe('Profil firmy i 3 oferty');
      expect(link.getAttribute('href')).toBe('/companies/1');
    });

    it('still links to the company profile when the company cannot be loaded', () => {
      getCompany.mockReturnValue(throwError(() => httpError(500)));
      create();

      expect(root().querySelector('.about')).toBeNull();
      expect(root().querySelector('.profile-link')?.textContent?.trim()).toBe('Profil firmy Acme');
    });
  });

  describe('similar offers', () => {
    it('lists up to 3 other offers from the same category', () => {
      getJobs.mockReturnValue(of({
        result: [makeJob(5), makeJob(6, { title: 'Inna 6' }), makeJob(7, { title: 'Inna 7' }), makeJob(8, { title: 'Inna 8' })],
        pagination: { CurrentPage: 1, ItemsPerPage: 4, TotalItems: 10, TotalPages: 3 }
      }));
      create(of({ ...JOB, categorySlug: 'programowanie', categoryName: 'Programowanie', categoryId: 1 }));

      const params = getJobs.mock.calls[0][0];
      expect(params.category).toBe('programowanie');
      expect(params.pageSize).toBe(4);
      const links = Array.from(root().querySelectorAll<HTMLAnchorElement>('.similar a'));
      expect(links.map(l => l.textContent?.trim())).toEqual(['Inna 6', 'Inna 7', 'Inna 8']);
      expect(links[0].getAttribute('href')).toBe('/jobs/6');
      expect(root().querySelector('.similar h2')?.textContent).toBe('Podobne oferty');
    });

    it('is not requested or shown for an offer without a category', () => {
      create();

      expect(getJobs).not.toHaveBeenCalled();
      expect(root().querySelector('.similar')).toBeNull();
    });

    it('is hidden when loading fails', () => {
      getJobs.mockReturnValue(throwError(() => httpError(500)));
      create(of({ ...JOB, categorySlug: 'programowanie' }));

      expect(root().querySelector('.similar')).toBeNull();
    });
  });

  describe('offer lifecycle', () => {
    it.each([
      [{ status: JobOfferStatus.Closed, isExpired: false }, 'Rekrutacja na to stanowisko została zakończona'],
      [{ status: JobOfferStatus.Active, isExpired: true }, 'Ta oferta wygasła'],
      [{ status: JobOfferStatus.Draft, isExpired: false }, 'Szkic – oferta nie jest widoczna publicznie']
    ])('shows a banner and no apply button for %o', (state, bannerText) => {
      create(of({ ...JOB, ...state }));

      const banner = root().querySelector('.offer-banner')!;
      expect(banner.getAttribute('role')).toBe('status');
      expect(banner.textContent).toContain(bannerText);
      expect(component.acceptsApplications()).toBe(false);
      expect(root().querySelector('.hero-actions button')).toBeNull();
    });

    it('shows no banner for an active offer', () => {
      create();

      expect(root().querySelector('.offer-banner')).toBeNull();
    });

    it('reloads the offer without the skeleton when the application section reports it stale', () => {
      create();
      getJob.mockReturnValue(of({ ...JOB, status: JobOfferStatus.Closed }));

      fixture.debugElement.query(el => el.componentInstance instanceof JobApplyComponent)
        .componentInstance.jobStale.emit();
      fixture.detectChanges();

      expect(getJob).toHaveBeenCalledTimes(2);
      expect(root().querySelector('.offer-banner')?.textContent).toContain('Rekrutacja na to stanowisko została zakończona');
    });
  });

  describe('"Aplikuj" in the hero', () => {
    function applyButton(): HTMLButtonElement | null {
      fixture.detectChanges();
      return root().querySelector<HTMLButtonElement>('.hero-actions button[mat-flat-button]');
    }

    it('moves to the inline application section and focuses its heading', () => {
      create();
      document.body.appendChild(root());

      applyButton()!.click();

      expect(document.activeElement).toBe(root().querySelector('#apply-title'));
      root().remove();
    });

    it('is shown to a logged-out visitor (the section asks to log in)', () => {
      authenticated = false;
      create();

      expect(applyButton()?.textContent?.trim()).toBe('Aplikuj');
      expect(text()).toContain('Zaloguj się, aby aplikować');
    });

    it('becomes "Twoja aplikacja" after applying', () => {
      checkIfApplied.mockReturnValue(of({ hasApplied: true, applicationId: 3, status: 0 }));
      create();

      expect(applyButton()?.textContent?.trim()).toBe('Twoja aplikacja');
    });

    it.each([['admin', () => admin = true], ['employer', () => employer = true]])('is hidden for an %s', (_role, setRole) => {
      setRole();
      create();

      expect(applyButton()).toBeNull();
    });
  });

  describe('loading and errors', () => {
    it('reserves the hero and facts space while loading', () => {
      create(new Observable<JobOffer>());

      expect(root().querySelector('.hero-skeleton')).not.toBeNull();
      expect(root().querySelector('.facts-skeleton')).not.toBeNull();
      expect(root().querySelector('h1')).toBeNull();
    });

    it('shows the translated message for a missing offer and reports an unavailable page title', () => {
      create(throwError(() => httpError(404, { message: 'Job offer not found' })));

      expect(root().querySelector('h1')?.textContent).toBe('Oferta niedostępna');
      expect(root().querySelector('[role="alert"]')?.textContent?.trim()).toBeTruthy();
      expect(setTitle).toHaveBeenCalledWith('Oferta niedostępna');
      expect(root().querySelector('a[href="/jobs"]')).not.toBeNull();
      // brak oferty to oczekiwany stan – nie jest logowany jako błąd
      expect(console.error).not.toHaveBeenCalled();
    });

    it('shows a generic message for server errors', () => {
      create(throwError(() => httpError(500)));
      expect(console.error).toHaveBeenCalledWith('Error loading job:', expect.anything());

      expect(root().querySelector('[role="alert"]')?.textContent).toBeTruthy();
      expect(root().querySelector('app-job-apply')).toBeNull();
    });
  });

  describe('saving the offer', () => {
    it('shows the save toggle in the hero for a candidate', () => {
      savedJobsEnabled = true;
      create();

      const button = root().querySelector('.hero-actions app-save-job-button button');
      expect(button?.getAttribute('aria-label')).toBe('Usuń z zapisanych: Angular Developer');
      expect(button?.getAttribute('aria-pressed')).toBe('true');
    });

    it('does not show it when saving is not available (e.g. employer)', () => {
      create();
      expect(root().querySelector('app-save-job-button button')).toBeNull();
    });
  });
});
