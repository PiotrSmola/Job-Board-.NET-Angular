import { ChangeDetectionStrategy, Component, computed, input, linkedSignal, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Subject, debounceTime } from 'rxjs';
import { Currency } from '../../models/job-offer.model';
import { JobParams } from '../../models/job-params.model';
import { Category, JobFacets } from '../../models/taxonomy.model';
import { getActiveFilters } from '../../utils/active-filters';
import {
  CONTRACT_TYPE_OPTIONS, CURRENCY_OPTIONS, SENIORITY_OPTIONS, WORK_TYPE_OPTIONS, hasFlag, labelWithCount, setFlag
} from '../../utils/job-labels';
import { MAX_FILTER_SKILLS } from '../../utils/job-query';
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

const SALARY_DEBOUNCE_MS = 800;
const POPULAR_SKILLS = 8;

/**
 * Panel filtrów listy ofert (kategoria, poziom, umowa, tryb pracy, umiejętności, wynagrodzenie): stan pochodzi
 * z `params` (adres URL), zmiany wracają przez `filterChange`. Natywne pola wyboru w fieldsetach z legendą –
 * licznik opcji jest w nazwie dostępnej, a widoczny licznik ukryty przed czytnikami (bez powtórzeń).
 */
@Component({
  selector: 'app-job-filters',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, SkillsInputComponent],
  templateUrl: './job-filters.component.html',
  styleUrl: './job-filters.component.scss'
})
export class JobFiltersComponent {
  readonly params = input.required<JobParams>();
  readonly facets = input<JobFacets | null>(null);
  readonly categories = input<Category[]>([]);

  readonly filterChange = output<FilterChange>();

  readonly maxSkills = MAX_FILTER_SKILLS;
  readonly currencyOptions = CURRENCY_OPTIONS;

  /** Panel na wąskich ekranach jest zwijany (na szerokich zawsze widoczny – patrz style). */
  readonly open = signal(false);

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

  private readonly salary$ = new Subject<void>();

  constructor() {
    this.salary$.pipe(debounceTime(SALARY_DEBOUNCE_MS), takeUntilDestroyed()).subscribe(() => {
      // Odwrócone widełki nie są wysyłane – komunikat pokazuje salaryInvalid
      if (!this.salaryInvalid()) {
        this.emit({ minSalary: this.minInput() || null, maxSalary: this.maxInput() || null }, true);
      }
    });
  }

  onMinInput(event: Event): void {
    this.minInput.set(this.toNumber(event));
    this.salary$.next();
  }

  onMaxInput(event: Event): void {
    this.maxInput.set(this.toNumber(event));
    this.salary$.next();
  }

  onCurrency(value: Currency | null): void {
    this.emit({ currency: value }, false);
  }

  onCategory(slug: string): void {
    this.emit({ category: slug || null }, false);
  }

  toggleOption(group: OptionGroup, value: number, event: Event): void {
    const params = this.params();
    const on = (event.target as HTMLInputElement).checked;

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

  addSkill(name: string, event: Event): void {
    const box = event.target as HTMLInputElement;
    if (box.checked && this.params().skills.length < MAX_FILTER_SKILLS) {
      this.setSkills([...this.params().skills, name]);
    } else {
      // limit umiejętności – pole wraca do stanu niezaznaczonego (wiązanie [checked]="false" się nie zmienia)
      box.checked = false;
    }
  }

  private toNumber(event: Event): number | null {
    const raw = (event.target as HTMLInputElement).value;
    const value = Number(raw);
    return raw === '' || !Number.isFinite(value) ? null : value;
  }

  private emit(patch: Partial<JobParams>, replace: boolean): void {
    this.filterChange.emit({ patch, replace });
  }
}
