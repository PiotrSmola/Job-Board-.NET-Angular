import { Currency, SeniorityLevel, WorkType } from '../models/job-offer.model';
import { JobOrderBy, JobParams } from '../models/job-params.model';
import { ALL_CONTRACT_FLAGS } from './job-labels';

export const PAGE_SIZE_OPTIONS = [5, 10, 20, 50];
export const MAX_SKILLS = 15;
export const MAX_SKILL_LENGTH = 50;

const DEFAULT_PAGE_SIZE = 10;
const MAX_TEXT_LENGTH = 100;
const MAX_PAGE = 100000;
const SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

const ORDER_BY_VALUES: readonly JobOrderBy[] = [
  'newest', 'oldest', 'salaryHigh', 'salaryLow', 'titleAsc', 'titleDesc',
  'companyAsc', 'companyDesc', 'locationAsc', 'locationDesc'
];

const WORK_TYPES = [WorkType.Remote, WorkType.Hybrid, WorkType.Onsite];
const SENIORITIES = [
  SeniorityLevel.Intern, SeniorityLevel.Junior, SeniorityLevel.Mid,
  SeniorityLevel.Senior, SeniorityLevel.Lead, SeniorityLevel.Manager
];
const CURRENCIES = [Currency.PLN, Currency.EUR, Currency.USD, Currency.GBP];

/** Nazwy umiejętności rozdzielane są przecinkiem w API i URL – przecinek w nazwie jest niedozwolony. */
export function isValidSkillName(name: string): boolean {
  return name.length >= 1 && name.length <= MAX_SKILL_LENGTH && !name.includes(',');
}

/** Dodaje nazwę do listy bez duplikatów (bez wielkości liter) i z limitem; zwraca tę samą listę, gdy nic się nie zmieniło. */
export function addSkill(skills: readonly string[], name: string, max = MAX_SKILLS): string[] {
  const trimmed = name.trim();
  const exists = skills.some(s => s.toLowerCase() === trimmed.toLowerCase());
  if (!isValidSkillName(trimmed) || exists || skills.length >= max) {
    return [...skills];
  }
  return [...skills, trimmed];
}

/**
 * Filtry jako parametry zapytania (nazwy jak w API GET /api/jobs); puste filtry są pomijane.
 * Używane zarówno do żądań API, jak i do query params adresu URL.
 */
export function filterQuery(params: JobParams): Record<string, string> {
  const query: Record<string, string> = {};

  if (params.searchTerm) query['searchTerm'] = params.searchTerm;
  if (params.location) query['location'] = params.location;
  if (params.category) query['category'] = params.category;
  if (params.companyId !== null) query['companyId'] = params.companyId.toString();
  if (params.seniority.length) query['seniority'] = sortedNumbers(params.seniority);
  if (params.contractTypes > 0) query['contractTypes'] = params.contractTypes.toString();
  if (params.workTypes.length) query['workType'] = sortedNumbers(params.workTypes);
  if (params.skills.length) query['skills'] = params.skills.join(',');
  if (params.currency !== null) query['currency'] = params.currency.toString();
  if (params.minSalary) query['minSalary'] = params.minSalary.toString();
  if (params.maxSalary) query['maxSalary'] = params.maxSalary.toString();

  return query;
}

/** Parametry adresu URL: filtry + strona / rozmiar / sortowanie, gdy różnią się od domyślnych (krótkie, udostępnialne linki). */
export function urlQuery(params: JobParams): Record<string, string> {
  const query = filterQuery(params);
  const defaults = new JobParams();

  if (params.pageNumber > 1) query['page'] = params.pageNumber.toString();
  if (params.pageSize !== defaults.pageSize) query['pageSize'] = params.pageSize.toString();
  if (params.orderBy !== defaults.orderBy) query['orderBy'] = params.orderBy;

  return query;
}

/** Klucz porównawczy samych filtrów (bez paginacji i sortowania) – facety zależą tylko od niego. */
export function filtersKey(params: JobParams): string {
  return JSON.stringify(filterQuery(params));
}

/**
 * Odczyt parametrów z adresu URL. Adres pochodzi od użytkownika (udostępnialny link), więc
 * każda wartość jest walidowana i obcinana – nieznane wartości są ignorowane.
 */
export function paramsFromQuery(get: (name: string) => string | null): JobParams {
  const params = new JobParams();

  params.searchTerm = text(get('searchTerm'));
  params.location = text(get('location'));
  params.category = slug(get('category'));
  params.companyId = positiveInt(get('companyId'));
  params.seniority = enumList(get('seniority'), SENIORITIES);
  params.contractTypes = (integer(get('contractTypes')) ?? 0) & ALL_CONTRACT_FLAGS;
  params.workTypes = enumList(get('workType'), WORK_TYPES);
  params.skills = skillList(get('skills'));
  params.currency = enumValue(get('currency'), CURRENCIES);
  params.minSalary = positiveInt(get('minSalary'));
  params.maxSalary = positiveInt(get('maxSalary'));

  params.pageNumber = Math.min(positiveInt(get('page')) ?? 1, MAX_PAGE);
  const pageSize = positiveInt(get('pageSize'));
  params.pageSize = pageSize !== null && PAGE_SIZE_OPTIONS.includes(pageSize) ? pageSize : DEFAULT_PAGE_SIZE;
  const orderBy = get('orderBy') as JobOrderBy | null;
  params.orderBy = orderBy && ORDER_BY_VALUES.includes(orderBy) ? orderBy : 'newest';

  return params;
}

export function cloneParams(params: JobParams): JobParams {
  return Object.assign(new JobParams(), params, {
    workTypes: [...params.workTypes],
    seniority: [...params.seniority],
    skills: [...params.skills]
  });
}

function sortedNumbers(values: readonly number[]): string {
  return [...new Set(values)].sort((a, b) => a - b).join(',');
}

function text(raw: string | null): string {
  return (raw ?? '').trim().slice(0, MAX_TEXT_LENGTH);
}

function slug(raw: string | null): string | null {
  const value = (raw ?? '').trim();
  return value.length <= MAX_TEXT_LENGTH && SLUG_PATTERN.test(value) ? value : null;
}

function integer(raw: string | null): number | null {
  if (raw === null || !/^-?\d{1,9}$/.test(raw.trim())) return null;
  return Number(raw);
}

function positiveInt(raw: string | null): number | null {
  const value = integer(raw);
  return value !== null && value > 0 ? value : null;
}

function enumValue<T extends number>(raw: string | null, allowed: readonly T[]): T | null {
  const value = integer(raw);
  return value !== null && (allowed as readonly number[]).includes(value) ? (value as T) : null;
}

function enumList<T extends number>(raw: string | null, allowed: readonly T[]): T[] {
  const values = (raw ?? '').split(',').map(part => enumValue(part, allowed)).filter((v): v is T => v !== null);
  return [...new Set(values)].sort((a, b) => a - b);
}

function skillList(raw: string | null): string[] {
  let skills: string[] = [];
  for (const part of (raw ?? '').split(',')) {
    skills = addSkill(skills, part);
  }
  return skills;
}
