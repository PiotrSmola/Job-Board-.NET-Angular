import { ContractTypes, Currency, SalaryPeriod, SeniorityLevel, WorkType } from '../models/job-offer.model';
import {
  formatFoundOffers, formatJobSalary, formatSalaryRange, getContractTypeLabels, getCurrencyLabel, getSeniorityLabel,
  getWorkTypeLabel, hasFlag, labelWithCount, offersNoun, setFlag, slugify
} from './job-labels';

// pl-PL grupuje tysiące twardą spacją – w oczekiwanych wartościach używamy zwykłej
const plain = (text: string) => text.replace(/ | /g, ' ');

describe('job labels', () => {
  it('has Polish labels for seniority, work type and currency', () => {
    expect([0, 1, 2, 3, 4, 5].map(v => getSeniorityLabel(v as SeniorityLevel)))
      .toEqual(['Stażysta', 'Junior', 'Mid', 'Senior', 'Lead', 'Manager']);
    expect(getSeniorityLabel(null)).toBeNull();
    expect(getWorkTypeLabel(WorkType.Hybrid)).toBe('Hybrydowa');
    expect(getWorkTypeLabel(99 as WorkType)).toBe('Nieznany');
    expect(getCurrencyLabel(Currency.GBP)).toBe('GBP');
    expect(getCurrencyLabel(undefined)).toBe('PLN');
  });

  describe('contract type flags', () => {
    it('lists labels of all set bits in a fixed order', () => {
      expect(getContractTypeLabels(ContractTypes.B2B | ContractTypes.EmploymentContract)).toEqual(['Umowa o pracę', 'B2B']);
      expect(getContractTypeLabels(31)).toEqual(['Umowa o pracę', 'B2B', 'Umowa zlecenie', 'Umowa o dzieło', 'Staż']);
      expect(getContractTypeLabels(0)).toEqual([]);
      expect(getContractTypeLabels(null)).toEqual([]);
    });

    it('sets, clears and tests single bits', () => {
      let mask = setFlag(0, ContractTypes.B2B, true);
      mask = setFlag(mask, ContractTypes.Internship, true);
      expect(mask).toBe(18);
      expect(hasFlag(mask, ContractTypes.B2B)).toBe(true);
      expect(hasFlag(mask, ContractTypes.MandateContract)).toBe(false);
      expect(setFlag(mask, ContractTypes.B2B, false)).toBe(16);
      expect(hasFlag(mask, 0)).toBe(false);
    });
  });

  describe('formatSalaryRange', () => {
    it.each([
      [15000, 20000, Currency.PLN, SalaryPeriod.Month, '15 000–20 000 PLN / mies.'],
      [120, 150, Currency.PLN, SalaryPeriod.Hour, '120–150 PLN / h'],
      [90000, 120000, Currency.EUR, SalaryPeriod.Year, '90 000–120 000 EUR / rok'],
      [8000, null, Currency.USD, SalaryPeriod.Month, 'od 8 000 USD / mies.'],
      [null, 12500, Currency.GBP, SalaryPeriod.Month, 'do 12 500 GBP / mies.'],
      [5000, 5000, Currency.PLN, SalaryPeriod.Month, '5 000 PLN / mies.'],
      [1000, 1999, Currency.PLN, SalaryPeriod.Month, '1 000–1 999 PLN / mies.']
    ])('formats %s-%s %s as "%s"', (min, max, currency, period, expected) => {
      expect(plain(formatSalaryRange(min, max, currency, period))).toBe(expected);
    });

    it('returns "Nieokreślona" without amounts', () => {
      expect(formatSalaryRange(null, null)).toBe('Nieokreślona');
      expect(formatSalaryRange(0, 0)).toBe('Nieokreślona');
    });

    it('defaults to PLN per month and uses a non-breaking space between thousands', () => {
      const text = formatSalaryRange(15000, 20000);
      expect(plain(text)).toBe('15 000–20 000 PLN / mies.');
      expect(text).toMatch(/15[  ]000/);
    });

    it('formats a job offer using its own currency and period', () => {
      expect(plain(formatJobSalary({ salaryMin: 100, salaryMax: 150, salaryCurrency: Currency.EUR, salaryPeriod: SalaryPeriod.Hour })))
        .toBe('100–150 EUR / h');
    });
  });

  describe('Polish plurals', () => {
    it.each([
      [0, 'ofert'], [1, 'oferta'], [2, 'oferty'], [4, 'oferty'], [5, 'ofert'], [12, 'ofert'], [14, 'ofert'],
      [22, 'oferty'], [25, 'ofert'], [112, 'ofert'], [132, 'oferty']
    ])('%s -> %s', (count, noun) => {
      expect(offersNoun(count)).toBe(noun);
    });

    it('builds the announcement and the accessible option label', () => {
      expect(formatFoundOffers(0)).toBe('Znaleziono 0 ofert');
      expect(formatFoundOffers(1)).toBe('Znaleziono 1 ofertę');
      expect(formatFoundOffers(3)).toBe('Znaleziono 3 oferty');
      expect(formatFoundOffers(12)).toBe('Znaleziono 12 ofert');
      expect(labelWithCount('Junior', 12)).toBe('Junior, 12 ofert');
      expect(labelWithCount('Junior', 1)).toBe('Junior, 1 oferta');
      expect(labelWithCount('Junior', null)).toBe('Junior');
    });
  });

  it('slugify strips Polish diacritics and separates words with a dash', () => {
    expect(slugify('DevOps i chmura')).toBe('devops-i-chmura');
    expect(slugify('Zarządzanie projektami')).toBe('zarzadzanie-projektami');
    expect(slugify('Design UX/UI')).toBe('design-ux-ui');
    expect(slugify('  Łódź & Co.  ')).toBe('lodz-co');
  });
});
