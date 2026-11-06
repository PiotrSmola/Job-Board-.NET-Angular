import { ChangeDetectionStrategy, Component, computed, input, linkedSignal, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxChange, MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioChange, MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { Subject, debounceTime } from 'rxjs';
import { Currency } from '../../models/job-offer.model';
import { JobOrderBy, JobParams } from '../../models/job-params.model';
import { Category, JobFacets } from '../../models/taxonomy.model';
import { getActiveFilters } from '../../utils/active-filters';
import {
  CONTRACT_TYPE_OPTIONS, CURRENCY_OPTIONS, SENIORITY_OPTIONS, WORK_TYPE_OPTIONS, hasFlag, labelWithCount, setFlag
} from '../../utils/job-labels';
import { MAX_SKILLS } from '../../utils/job-query';
import { SkillsInputComponent } from '../shared/skills-input.component';

/** Zmiana filtrów zgłaszana rodzicowi; `replace` – nie dodawaj wpisu do historii (pisanie w polach tekstowych). */
export interface FilterChange {
  patch: Partial<JobParams>;
  replace: boolean;
}

type OptionGroup = 'seniority' | 'contract' | 'work';

interface FilterOption {
  value: number;
  label: string;
  count: number | null;
  checked: boolean;
  ariaLabel: string;
}

interface FilterSection {
  group: OptionGroup;
  legend: string;
  options: FilterOption[];
}

const TEXT_DEBOUNCE_MS = 500;
const SALARY_DEBOUNCE_MS = 800;
const POPULAR_SKILLS = 8;

/** Panel filtrów listy ofert: stan pochodzi z `params` (adres URL), zmiany wracają przez `filterChange`. */
@Component({
  selector: 'app-job-filters',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatRadioModule,
    MatSelectModule,
    SkillsInputComponent
  ],
  templateUrl: './job-filters.component.html',
  styleUrl: './job-filters.component.scss'
})
export class JobFiltersComponent {
  readonly params = input.required<JobParams>();
  readonly facets = input<JobFacets | null>(null);
  readonly categories = input<Category[]>([]);

  readonly filterChange = output<FilterChange>();
  readonly clear = output<void>();

  readonly maxSkills = MAX_SKILLS;
  readonly currencyOptions = CURRENCY_OPTIONS;
  readonly sortOptions: { value: JobOrderBy; label: string }[] = [
    { value: 'newest', label: 'Najnowsze' },
    { value: 'oldest', label: 'Najstarsze' },
    { value: 'salaryHigh', label: 'Najwyższa płaca' },
    { value: 'salaryLow', label: 'Najniższa płaca' }
  ];

  /** Panel na wąskich ekranach jest zwijany (na szerokich zawsze widoczny – patrz style). */
  readonly open = signal(false);

  // Pola tekstowe pokazują to, co wpisuje użytkownik; zmiana wartości w adresie (np. „wstecz”) nadpisuje je
  readonly searchInput = linkedSignal(() => this.params().searchTerm);
  readonly locationInput = linkedSignal(() => this.params().location);
  readonly minInput = linkedSignal<number | null>(() => this.params().minSalary);
  readonly maxInput = linkedSignal<number | null>(() => this.params().maxSalary);

  readonly salaryInvalid = computed(() => {
    const min = this.minInput();
    const max = this.maxInput();
    return !!min && !!max && min > max;
  });

  readonly activeCount = computed(() => getActiveFilters(this.params(), this.categories()).length);

  readonly sections = computed<FilterSection[]>(() => {
    const params = this.params();
    const facets = this.facets();
    const countOf = (list: { value: number; count: number }[] | undefined, value: number) =>
      facets ? list?.find(f => f.value === value)?.count ?? 0 : null;
    const build = (
      group: OptionGroup, legend: string, options: { value: number; label: string }[],
      facetList: { value: number; count: number }[] | undefined, checked: (value: number) => boolean
    ): FilterSection => ({
      group,
      legend,
      options: options.map(o => {
        const count = countOf(facetList, o.value);
        return { ...o, count, checked: checked(o.value), ariaLabel: labelWithCount(o.label, count) };
      })
    });

    return [
      build('seniority', 'Poziom', SENIORITY_OPTIONS, facets?.seniority, v => params.seniority.includes(v)),
      build('contract', 'Typ umowy', CONTRACT_TYPE_OPTIONS, facets?.contractTypes, v => hasFlag(params.contractTypes, v)),
      build('work', 'Tryb pracy', WORK_TYPE_OPTIONS, facets?.workTypes, v => params.workTypes.includes(v))
    ];
  });

  readonly categoryOptions = computed(() => {
    const facets = this.facets();
    return this.categories().map(category => {
      const count = facets ? facets.categories.find(c => c.id === category.id)?.count ?? 0 : null;
      return { ...category, count, ariaLabel: labelWithCount(category.name, count) };
    });
  });

  /** Popularne umiejętności w wynikach (z faset), bez już wybranych. */
  readonly popularSkills = computed(() => {
    const selected = new Set(this.params().skills.map(s => s.toLowerCase()));
    return (this.facets()?.skills ?? [])
      .filter(s => !selected.has(s.name.toLowerCase()))
      .slice(0, POPULAR_SKILLS)
      .map(s => ({ ...s, ariaLabel: labelWithCount(s.name, s.count) }));
  });

  private readonly search$ = new Subject<string>();
  private readonly location$ = new Subject<string>();
  private readonly salary$ = new Subject<void>();

  constructor() {
    this.search$.pipe(debounceTime(TEXT_DEBOUNCE_MS), takeUntilDestroyed())
      .subscribe(searchTerm => this.emit({ searchTerm: searchTerm.trim() }, true));
    this.location$.pipe(debounceTime(TEXT_DEBOUNCE_MS), takeUntilDestroyed())
      .subscribe(location => this.emit({ location: location.trim() }, true));
    this.salary$.pipe(debounceTime(SALARY_DEBOUNCE_MS), takeUntilDestroyed()).subscribe(() => {
      // Odwrócone widełki nie są wysyłane – komunikat pokazuje salaryInvalid
      if (!this.salaryInvalid()) {
        this.emit({ minSalary: this.minInput() || null, maxSalary: this.maxInput() || null }, true);
      }
    });
  }

  onSearchInput(value: string): void {
    this.searchInput.set(value);
    this.search$.next(value);
  }

  /** Enter w polu szukania stosuje frazę od razu, bez czekania na debounce. */
  applySearchNow(): void {
    this.emit({ searchTerm: this.searchInput().trim() }, false);
  }

  onLocationInput(value: string): void {
    this.locationInput.set(value);
    this.location$.next(value);
  }

  onMinInput(value: number | null): void {
    this.minInput.set(value);
    this.salary$.next();
  }

  onMaxInput(value: number | null): void {
    this.maxInput.set(value);
    this.salary$.next();
  }

  onCurrency(value: Currency | null): void {
    this.emit({ currency: value }, false);
  }

  onSort(value: JobOrderBy): void {
    this.emit({ orderBy: value }, false);
  }

  onCategory(event: MatRadioChange): void {
    this.emit({ category: (event.value as string) || null }, false);
  }

  toggleOption(group: OptionGroup, value: number, event: MatCheckboxChange): void {
    const params = this.params();
    const on = event.checked;

    if (group === 'contract') {
      this.emit({ contractTypes: setFlag(params.contractTypes, value, on) }, false);
      return;
    }

    const current = group === 'seniority' ? params.seniority : params.workTypes;
    const next = on ? [...current, value] : current.filter(v => v !== value);
    this.emit(group === 'seniority' ? { seniority: next } : { workTypes: next }, false);
  }

  setSkills(skills: string[]): void {
    this.emit({ skills }, false);
  }

  addSkill(name: string, event: MatCheckboxChange): void {
    if (event.checked && this.params().skills.length < MAX_SKILLS) {
      this.setSkills([...this.params().skills, name]);
    }
  }

  private emit(patch: Partial<JobParams>, replace: boolean): void {
    this.filterChange.emit({ patch, replace });
  }
}
