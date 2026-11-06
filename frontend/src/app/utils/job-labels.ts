import { ContractTypes, Currency, JobOffer, SalaryPeriod, SeniorityLevel, WorkType } from '../models/job-offer.model';

export interface LabeledOption<T> {
  value: T;
  label: string;
}

export const WORK_TYPE_OPTIONS: LabeledOption<WorkType>[] = [
  { value: WorkType.Remote, label: 'Zdalna' },
  { value: WorkType.Hybrid, label: 'Hybrydowa' },
  { value: WorkType.Onsite, label: 'Stacjonarna' }
];

export const SENIORITY_OPTIONS: LabeledOption<SeniorityLevel>[] = [
  { value: SeniorityLevel.Intern, label: 'Stażysta' },
  { value: SeniorityLevel.Junior, label: 'Junior' },
  { value: SeniorityLevel.Mid, label: 'Mid' },
  { value: SeniorityLevel.Senior, label: 'Senior' },
  { value: SeniorityLevel.Lead, label: 'Lead' },
  { value: SeniorityLevel.Manager, label: 'Manager' }
];

export const CONTRACT_TYPE_OPTIONS: LabeledOption<ContractTypes>[] = [
  { value: ContractTypes.EmploymentContract, label: 'Umowa o pracę' },
  { value: ContractTypes.B2B, label: 'B2B' },
  { value: ContractTypes.MandateContract, label: 'Umowa zlecenie' },
  { value: ContractTypes.SpecificTaskContract, label: 'Umowa o dzieło' },
  { value: ContractTypes.Internship, label: 'Staż' }
];

export const CURRENCY_OPTIONS: LabeledOption<Currency>[] = [
  { value: Currency.PLN, label: 'PLN' },
  { value: Currency.EUR, label: 'EUR' },
  { value: Currency.USD, label: 'USD' },
  { value: Currency.GBP, label: 'GBP' }
];

export const SALARY_PERIOD_OPTIONS: LabeledOption<SalaryPeriod>[] = [
  { value: SalaryPeriod.Month, label: 'miesięcznie' },
  { value: SalaryPeriod.Hour, label: 'godzinowo' },
  { value: SalaryPeriod.Year, label: 'rocznie' }
];

/** Skrót okresu w widełkach („15 000–20 000 PLN / mies.”). */
const PERIOD_SHORT: Record<SalaryPeriod, string> = {
  [SalaryPeriod.Month]: 'mies.',
  [SalaryPeriod.Hour]: 'h',
  [SalaryPeriod.Year]: 'rok'
};

export const ALL_CONTRACT_FLAGS = CONTRACT_TYPE_OPTIONS.reduce((mask, o) => mask | o.value, 0);

function labelOf<T>(options: LabeledOption<T>[], value: T | null | undefined): string | null {
  return options.find(o => o.value === value)?.label ?? null;
}

export function getWorkTypeLabel(value: WorkType | null | undefined): string {
  return labelOf(WORK_TYPE_OPTIONS, value) ?? 'Nieznany';
}

export function getSeniorityLabel(value: SeniorityLevel | null | undefined): string | null {
  return labelOf(SENIORITY_OPTIONS, value);
}

export function getCurrencyLabel(value: Currency | null | undefined): string {
  return labelOf(CURRENCY_OPTIONS, value ?? Currency.PLN) ?? 'PLN';
}

/** Etykiety zaznaczonych typów umów w stałej kolejności. */
export function getContractTypeLabels(mask: number | null | undefined): string[] {
  const bits = mask ?? 0;
  return CONTRACT_TYPE_OPTIONS.filter(o => (bits & o.value) !== 0).map(o => o.label);
}

export function hasFlag(mask: number, flag: number): boolean {
  return (mask & flag) === flag && flag !== 0;
}

export function setFlag(mask: number, flag: number, on: boolean): number {
  return on ? mask | flag : mask & ~flag;
}

// useGrouping 'always' – pl-PL domyślnie nie grupuje liczb czterocyfrowych („8000”), a widełki mają być spójne („8 000”)
const numberFormat = new Intl.NumberFormat('pl-PL', { maximumFractionDigits: 2, useGrouping: 'always' } as unknown as Intl.NumberFormatOptions);

export function formatNumber(value: number): string {
  return numberFormat.format(value);
}

/** Widełki z walutą i okresem: „15 000–20 000 PLN / mies.”, „od 8 000 EUR / mies.”; bez kwot „Nieokreślona”. */
export function formatSalaryRange(
  min: number | null | undefined,
  max: number | null | undefined,
  currency: Currency | null | undefined = Currency.PLN,
  period: SalaryPeriod | null | undefined = SalaryPeriod.Month
): string {
  const hasMin = typeof min === 'number' && min > 0;
  const hasMax = typeof max === 'number' && max > 0;
  if (!hasMin && !hasMax) return 'Nieokreślona';

  let amount: string;
  if (hasMin && hasMax) {
    amount = min === max ? formatNumber(min) : `${formatNumber(min)}–${formatNumber(max)}`;
  } else if (hasMin) {
    amount = `od ${formatNumber(min)}`;
  } else {
    amount = `do ${formatNumber(max as number)}`;
  }

  const unit = PERIOD_SHORT[period ?? SalaryPeriod.Month] ?? PERIOD_SHORT[SalaryPeriod.Month];
  return `${amount} ${getCurrencyLabel(currency)} / ${unit}`;
}

export function formatJobSalary(job: Pick<JobOffer, 'salaryMin' | 'salaryMax' | 'salaryCurrency' | 'salaryPeriod'>): string {
  return formatSalaryRange(job.salaryMin, job.salaryMax, job.salaryCurrency, job.salaryPeriod);
}

/** „oferta / oferty / ofert” w mianowniku (po liczbie: „12 ofert”). */
export function offersNoun(count: number): string {
  if (count === 1) return 'oferta';
  const lastTwo = count % 100;
  const last = count % 10;
  return last >= 2 && last <= 4 && !(lastTwo >= 12 && lastTwo <= 14) ? 'oferty' : 'ofert';
}

/** „Znaleziono 1 ofertę / 2 oferty / 5 ofert” – komunikat dla czytników ekranu. */
export function formatFoundOffers(count: number): string {
  const noun = count === 1 ? 'ofertę' : offersNoun(count);
  return `Znaleziono ${count} ${noun}`;
}

/** Etykieta dostępna opcji z licznikiem: „Junior, 12 ofert”. */
export function labelWithCount(label: string, count: number | null): string {
  return count === null ? label : `${label}, ${count} ${offersNoun(count)}`;
}

/** Slug z nazwy: małe litery, polskie znaki → ASCII, separator „-”. */
export function slugify(name: string): string {
  const map: Record<string, string> = { ł: 'l', Ł: 'l' };
  return name
    .replace(/[łŁ]/g, ch => map[ch])
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}
