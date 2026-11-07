import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Observable, Subject, catchError, distinctUntilChanged, filter, map, of, switchMap, tap } from 'rxjs';
import { JobService } from '../../services/job.service';
import { CompanyService } from '../../services/company.service';
import { AuthService } from '../../services/auth.service';
import { PageTitleService } from '../../services/page-title.service';
import { JobOffer } from '../../models/job-offer.model';
import { JobParams } from '../../models/job-params.model';
import { Company } from '../../models/company.model';
import { BackgroundFocus, OfferBackground } from '../../models/company-media.model';
import { offerHeroMask } from '../../utils/offer-hero';
import { getJobState } from '../../utils/status-labels';
import {
  SALARY_PERIOD_OPTIONS, formatJobSalary, formatSalaryRange, getContractTypeLabels, getSeniorityLabel, offersNoun
} from '../../utils/job-labels';
import { paramsFromQuery, urlQuery } from '../../utils/job-query';
import { getApiErrorMessage } from '../../utils/http-errors';
import { formatDayMonth, formatSkills, getWorkModeLabel, isoDate } from '../shared/job-taxonomy.component';
import { SaveJobButtonComponent } from '../shared/save-job-button.component';
import { JOBS_QUERY_STATE } from '../job-list/jobs-query-state';
import { JobApplyComponent } from './job-apply.component';

export { ATTACHMENTS_UPLOAD_FAILED_MESSAGE } from './job-apply.component';

/** Liczba „podobnych ofert” pod ofertą. */
const SIMILAR_COUNT = 3;

/**
 * Adres obrazu z API wstawiany do CSS `url("…")`: tylko względny `/api/...` bez cudzysłowów, nawiasów,
 * odstępów i ukośników wstecznych (wersja w query stringu `?v=` jest dozwolona) – nic nie może wyjść poza url().
 */
const SAFE_API_URL = /^\/api\/[A-Za-z0-9\-._~/?=&%]+$/;

export function isSafeApiUrl(url: string | null | undefined): url is string {
  return typeof url === 'string' && url.length <= 500 && SAFE_API_URL.test(url) && !url.includes('..');
}

const FOCUS_POSITION: Record<BackgroundFocus, string> = {
  [BackgroundFocus.Center]: 'center',
  [BackgroundFocus.Top]: 'center top',
  [BackgroundFocus.Bottom]: 'center bottom'
};

/** Styl nagłówka z tłem firmy: obraz, punkt ostrości i maska z przyciemnienia (wspólna z podglądem w panelu); null = jednolite tło. */
export function heroStyle(background: OfferBackground | null | undefined): {
  image: string; position: string; mask: string;
} | null {
  if (!background || !isSafeApiUrl(background.url)) return null;
  return {
    image: `url("${background.url}")`,
    position: FOCUS_POSITION[background.focus] ?? FOCUS_POSITION[BackgroundFocus.Center],
    mask: offerHeroMask(background.dim)
  };
}

/** Szczegóły oferty (makieta OfertaB): nagłówek z tłem firmy, blok faktów, sekcje-wiersze, aplikowanie na stronie. */
@Component({
  selector: 'app-job-detail',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatButtonModule, MatIconModule, SaveJobButtonComponent, JobApplyComponent],
  templateUrl: './job-detail.component.html',
  styleUrl: './job-detail.component.scss'
})
export class JobDetailComponent {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private jobService = inject(JobService);
  private companyService = inject(CompanyService);
  private authService = inject(AuthService);
  private pageTitle = inject(PageTitleService);

  private readonly applySection = viewChild(JobApplyComponent);

  readonly job = signal<JobOffer | null>(null);
  readonly loading = signal(true);
  /** Komunikat, gdy oferty nie udało się wczytać (null = brak błędu). */
  readonly loadError = signal<string | null>(null);
  readonly company = signal<Company | null>(null);
  readonly similar = signal<JobOffer[]>([]);
  readonly applied = signal(false);

  /** Filtry listy, z której przyszedł użytkownik (link „Wróć do wyników”); walidowane jak adres URL. */
  readonly backQuery = this.readBackQuery();

  readonly hero = computed(() => heroStyle(this.job()?.companyBackground));
  readonly acceptsApplications = computed(() => {
    const job = this.job();
    return !!job && getJobState(job) === 'active';
  });
  /** Przycisk „Aplikuj” w nagłówku: oferta przyjmuje aplikacje, a użytkownik nie jest pracodawcą ani adminem. */
  readonly showApplyButton = computed(() =>
    this.acceptsApplications() && !this.authService.isAdmin() && !this.authService.isEmployer());

  readonly banner = computed<{ icon: string; text: string } | null>(() => {
    const job = this.job();
    if (!job) return null;
    switch (getJobState(job)) {
      case 'closed': return { icon: 'lock', text: 'Rekrutacja na to stanowisko została zakończona – oferta nie przyjmuje już aplikacji.' };
      case 'expired': return { icon: 'event_busy', text: 'Ta oferta wygasła i nie przyjmuje już aplikacji.' };
      case 'draft': return { icon: 'edit_note', text: 'Szkic – oferta nie jest widoczna publicznie.' };
      default: return null;
    }
  });

  /** Teksty strony liczone raz na wczytanie oferty. */
  readonly view = computed(() => {
    const job = this.job();
    if (!job) return null;
    const salary = formatSalaryRange(job.salaryMin, job.salaryMax, job.salaryCurrency, job.salaryPeriod);
    const specified = salary !== 'Nieokreślona';
    return {
      // kwota bez skrótu okresu („/ mies.”) – okres jest pełnym słowem obok
      salaryAmount: specified ? salary.replace(/ \/ [^/]+$/, '') : salary,
      salaryPeriod: specified ? SALARY_PERIOD_OPTIONS.find(o => o.value === job.salaryPeriod)?.label ?? null : null,
      contracts: getContractTypeLabels(job.contractTypes).join(', '),
      seniority: getSeniorityLabel(job.seniority),
      workMode: getWorkModeLabel(job.workType),
      posted: formatDayMonth(job.createdAt),
      postedIso: isoDate(job.createdAt),
      expires: job.expiresAt ? formatDayMonth(job.expiresAt) : null,
      expiresIso: isoDate(job.expiresAt),
      skills: formatSkills(job.skills)
    };
  });

  readonly companyLinkText = computed(() => {
    const company = this.company();
    if (!company) return `Profil firmy ${this.job()?.company ?? ''}`.trim();
    const count = company.activeJobsCount;
    return count > 0 ? `Profil firmy i ${count} ${offersNoun(count)}` : 'Profil firmy';
  });

  readonly similarCards = computed(() => this.similar().map(job => ({ job, salary: formatJobSalary(job) })));

  private readonly load$ = new Subject<{ id: number; silent: boolean }>();
  private readonly related$ = new Subject<JobOffer>();

  constructor() {
    this.load$.pipe(
      tap(({ silent }) => {
        if (!silent) {
          this.loading.set(true);
          this.loadError.set(null);
        }
      }),
      // switchMap – szybka zmiana :id nie pozwoli starszej odpowiedzi nadpisać nowszej
      switchMap(({ id, silent }) => this.jobService.getJob(id).pipe(
        map(job => ({ job, error: null as unknown, silent })),
        catchError(error => of({ job: null, error, silent }))
      )),
      takeUntilDestroyed()
    ).subscribe(({ job, error, silent }) => {
      if (job) {
        this.job.set(job);
        this.loadError.set(null);
        this.pageTitle.set(job.title);
        this.related$.next(job);
      } else if (!silent) {
        console.error('Error loading job:', error);
        this.job.set(null);
        this.loadError.set(getApiErrorMessage(error, 'Nie udało się wczytać oferty'));
        this.pageTitle.set('Oferta niedostępna');
      }
      this.loading.set(false);
    });

    // Firma i podobne oferty – tylko przy zmianie oferty, błędy nie psują strony (sekcje się nie pokazują)
    const changed$ = this.related$.pipe(distinctUntilChanged((a, b) => a.id === b.id));
    changed$.pipe(
      tap(() => this.company.set(null)),
      switchMap(job => this.companyService.getCompany(job.companyId).pipe(catchError(() => of(null)))),
      takeUntilDestroyed()
    ).subscribe(company => this.company.set(company));

    changed$.pipe(
      tap(() => this.similar.set([])),
      filter(job => !!job.categorySlug),
      switchMap(job => this.fetchSimilar(job)),
      takeUntilDestroyed()
    ).subscribe(jobs => this.similar.set(jobs));

    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe(params => {
      const id = Number(params.get('id'));
      this.applied.set(false);
      this.load$.next({ id, silent: false });
    });
  }

  /** Oferta zmieniła stan w trakcie aplikowania – odświeżenie bez szkieletu pokazuje baner. */
  reloadJob(): void {
    const job = this.job();
    if (job) this.load$.next({ id: job.id, silent: true });
  }

  goToApply(): void {
    this.applySection()?.focusSection();
  }

  private fetchSimilar(job: JobOffer): Observable<JobOffer[]> {
    const params = new JobParams();
    params.category = job.categorySlug;
    // o jedną więcej – bieżąca oferta może być w wynikach
    params.pageSize = SIMILAR_COUNT + 1;
    return this.jobService.getJobs(params).pipe(
      map(page => page.result.filter(other => other.id !== job.id).slice(0, SIMILAR_COUNT)),
      catchError(() => of([]))
    );
  }

  private readBackQuery(): Record<string, string> {
    const state = this.router.currentNavigation()?.extras.state ?? (typeof history !== 'undefined' ? history.state : null);
    const raw = state && typeof state === 'object' ? (state as Record<string, unknown>)[JOBS_QUERY_STATE] : null;
    if (!raw || typeof raw !== 'object') return {};
    const values = raw as Record<string, unknown>;
    return urlQuery(paramsFromQuery(name => typeof values[name] === 'string' ? values[name] as string : null));
  }
}
