import { ContractTypes, Currency, SeniorityLevel, WorkType } from '../models/job-offer.model';
import { JobParams } from '../models/job-params.model';
import { Category } from '../models/taxonomy.model';
import { getActiveFilters } from './active-filters';

const CATEGORIES: Category[] = [{ id: 1, name: 'DevOps i chmura', slug: 'devops-i-chmura', sortOrder: 1, activeJobsCount: 3 }];

describe('getActiveFilters', () => {
  it('returns nothing for default params', () => {
    expect(getActiveFilters(new JobParams(), CATEGORIES)).toEqual([]);
  });

  it('builds a labelled chip per active filter value', () => {
    const params = new JobParams();
    params.searchTerm = 'dev';
    params.category = 'devops-i-chmura';
    params.seniority = [SeniorityLevel.Junior, SeniorityLevel.Senior];
    params.contractTypes = ContractTypes.B2B;
    params.workTypes = [WorkType.Remote];
    params.skills = ['Docker'];
    params.currency = Currency.EUR;
    params.minSalary = 8000;
    params.maxSalary = 12000;

    const filters = getActiveFilters(params, CATEGORIES);

    expect(filters.map(f => f.label.replace(/ | /g, ' '))).toEqual([
      'Fraza: dev', 'Kategoria: DevOps i chmura', 'Junior', 'Senior', 'B2B', 'Praca zdalna', 'Docker', 'Waluta: EUR', 'Płaca: 8 000–12 000'
    ]);
    expect(filters.every(f => f.removeLabel === `Usuń filtr: ${f.label}`)).toBe(true);
  });

  it('removing a chip changes only its own value', () => {
    const params = new JobParams();
    params.seniority = [SeniorityLevel.Junior, SeniorityLevel.Mid];
    params.contractTypes = ContractTypes.B2B | ContractTypes.Internship;
    params.skills = ['A', 'B'];

    const byKey = (key: string) => getActiveFilters(params, CATEGORIES).find(f => f.key === key)!;

    expect(byKey('seniority-1').remove).toEqual({ seniority: [SeniorityLevel.Mid] });
    expect(byKey(`contract-${ContractTypes.B2B}`).remove).toEqual({ contractTypes: ContractTypes.Internship });
    expect(byKey('skill-A').remove).toEqual({ skills: ['B'] });
  });

  it('falls back to the slug when the category is unknown and clears both salary bounds with one chip', () => {
    const params = new JobParams();
    params.category = 'nieznana';
    params.minSalary = 5000;

    const filters = getActiveFilters(params, CATEGORIES);

    expect(filters[0].label).toBe('Kategoria: nieznana');
    expect(filters[1].label.replace(/ | /g, ' ')).toBe('Płaca od 5 000');
    expect(filters[1].remove).toEqual({ minSalary: null, maxSalary: null });
  });
});
