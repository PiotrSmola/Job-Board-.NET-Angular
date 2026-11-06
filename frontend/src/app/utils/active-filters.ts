import { JobParams } from '../models/job-params.model';
import { Category } from '../models/taxonomy.model';
import {
  CONTRACT_TYPE_OPTIONS, SENIORITY_OPTIONS, WORK_TYPE_OPTIONS, formatNumber, getCurrencyLabel, hasFlag, setFlag
} from './job-labels';

/** Aktywny filtr listy ofert: etykieta chipa i zmiana parametrów, która go usuwa. */
export interface ActiveFilter {
  key: string;
  label: string;
  /** Etykieta dla czytników ekranu („Usuń filtr: Junior”). */
  removeLabel: string;
  remove: Partial<JobParams>;
}

function salaryLabel(params: JobParams): string {
  const { minSalary: min, maxSalary: max } = params;
  if (min && max) return `Płaca: ${formatNumber(min)}–${formatNumber(max)}`;
  return min ? `Płaca od ${formatNumber(min)}` : `Płaca do ${formatNumber(max ?? 0)}`;
}

/** Lista aktywnych filtrów w stałej kolejności (do chipów nad listą i licznika w panelu). */
export function getActiveFilters(params: JobParams, categories: readonly Category[]): ActiveFilter[] {
  const filters: ActiveFilter[] = [];
  const add = (key: string, label: string, remove: Partial<JobParams>) =>
    filters.push({ key, label, removeLabel: `Usuń filtr: ${label}`, remove });

  if (params.searchTerm) add('search', `Fraza: ${params.searchTerm}`, { searchTerm: '' });
  if (params.location) add('location', `Lokalizacja: ${params.location}`, { location: '' });

  if (params.category) {
    const name = categories.find(c => c.slug === params.category)?.name ?? params.category;
    add('category', `Kategoria: ${name}`, { category: null });
  }

  for (const option of SENIORITY_OPTIONS.filter(o => params.seniority.includes(o.value))) {
    add(`seniority-${option.value}`, option.label, { seniority: params.seniority.filter(v => v !== option.value) });
  }

  for (const option of CONTRACT_TYPE_OPTIONS.filter(o => hasFlag(params.contractTypes, o.value))) {
    add(`contract-${option.value}`, option.label, { contractTypes: setFlag(params.contractTypes, option.value, false) });
  }

  for (const option of WORK_TYPE_OPTIONS.filter(o => params.workTypes.includes(o.value))) {
    add(`work-${option.value}`, `Praca ${option.label.toLowerCase()}`, { workTypes: params.workTypes.filter(v => v !== option.value) });
  }

  for (const skill of params.skills) {
    add(`skill-${skill}`, skill, { skills: params.skills.filter(s => s !== skill) });
  }

  if (params.currency !== null) add('currency', `Waluta: ${getCurrencyLabel(params.currency)}`, { currency: null });
  if (params.minSalary || params.maxSalary) add('salary', salaryLabel(params), { minSalary: null, maxSalary: null });

  return filters;
}
