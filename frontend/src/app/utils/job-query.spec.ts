import { ContractTypes, Currency, SeniorityLevel, WorkType } from '../models/job-offer.model';
import { JobParams } from '../models/job-params.model';
import { addSkill, cloneParams, filterQuery, filtersKey, isValidSkillName, paramsFromQuery, urlQuery } from './job-query';

function fromUrl(search: string): JobParams {
  const query = new URLSearchParams(search);
  return paramsFromQuery(name => query.get(name));
}

describe('job query helpers', () => {
  describe('filterQuery', () => {
    it('is empty for default params', () => {
      expect(filterQuery(new JobParams())).toEqual({});
    });

    it('serializes lists as sorted comma-separated values and contract types as a mask', () => {
      const params = new JobParams();
      params.seniority = [SeniorityLevel.Senior, SeniorityLevel.Junior, SeniorityLevel.Junior];
      params.workTypes = [WorkType.Onsite, WorkType.Remote];
      params.contractTypes = ContractTypes.B2B | ContractTypes.Internship;
      params.skills = ['Angular', 'C#'];
      params.currency = Currency.PLN;
      params.category = 'testowanie';

      expect(filterQuery(params)).toEqual({
        category: 'testowanie',
        seniority: '1,3',
        contractTypes: '18',
        workType: '0,2',
        skills: 'Angular,C#',
        currency: '0'
      });
    });

    it('keeps the key stable for the same filters regardless of paging and sorting', () => {
      const a = new JobParams();
      a.skills = ['Angular'];
      const b = cloneParams(a);
      b.pageNumber = 5;
      b.orderBy = 'oldest';

      expect(filtersKey(a)).toBe(filtersKey(b));
    });
  });

  describe('urlQuery', () => {
    it('adds paging and sorting only when they differ from the defaults', () => {
      const params = new JobParams();
      expect(urlQuery(params)).toEqual({});

      params.pageNumber = 3;
      params.pageSize = 20;
      params.orderBy = 'salaryHigh';
      expect(urlQuery(params)).toEqual({ page: '3', pageSize: '20', orderBy: 'salaryHigh' });
    });
  });

  describe('paramsFromQuery', () => {
    it('round-trips a full set of params through the URL', () => {
      const params = new JobParams();
      params.searchTerm = 'C# & .NET';
      params.location = 'Rzeszów';
      params.category = 'devops-i-chmura';
      params.seniority = [SeniorityLevel.Junior, SeniorityLevel.Mid];
      params.contractTypes = 3;
      params.workTypes = [WorkType.Remote, WorkType.Hybrid];
      params.skills = ['Angular', 'Node.js'];
      params.currency = Currency.EUR;
      params.minSalary = 5000;
      params.maxSalary = 9000;
      params.pageNumber = 2;
      params.pageSize = 50;
      params.orderBy = 'titleAsc';
      params.companyId = 7;

      const search = new URLSearchParams(urlQuery(params)).toString();

      expect(fromUrl(search)).toEqual(params);
    });

    it('returns defaults for an empty query', () => {
      expect(fromUrl('')).toEqual(new JobParams());
    });

    it('ignores invalid and unknown values instead of failing', () => {
      const params = fromUrl([
        'seniority=1,abc,99,-1,2,1', 'workType=7,1', 'contractTypes=999', 'currency=9', 'page=-3', 'pageSize=13',
        'orderBy=drop%20table', 'minSalary=abc', 'maxSalary=-5', 'category=Nie%20Slug!', 'companyId=1.5'
      ].join('&'));

      expect(params.seniority).toEqual([SeniorityLevel.Junior, SeniorityLevel.Mid]);
      expect(params.workTypes).toEqual([WorkType.Hybrid]);
      expect(params.contractTypes).toBe(999 & 31);
      expect(params.currency).toBeNull();
      expect(params.pageNumber).toBe(1);
      expect(params.pageSize).toBe(10);
      expect(params.orderBy).toBe('newest');
      expect(params.minSalary).toBeNull();
      expect(params.maxSalary).toBeNull();
      expect(params.category).toBeNull();
      expect(params.companyId).toBeNull();
    });

    it('limits the skills filter to 10 unique names of 1-50 characters', () => {
      const many = Array.from({ length: 20 }, (_, i) => `S${i}`).join(',');
      expect(fromUrl(`skills=${many}`).skills.length).toBe(10);
      expect(fromUrl('skills=Angular,angular,%20Angular%20').skills).toEqual(['Angular']);
      expect(fromUrl(`skills=${'x'.repeat(51)},ok`).skills).toEqual(['ok']);
    });

    it('trims and truncates free text', () => {
      const params = fromUrl(`searchTerm=%20%20dev%20%20&location=${'k'.repeat(300)}`);
      expect(params.searchTerm).toBe('dev');
      expect(params.location.length).toBe(100);
    });

    it('does not keep a reference to a shared array when cloning', () => {
      const params = new JobParams();
      params.skills = ['A'];
      const copy = cloneParams(params);
      copy.skills.push('B');
      expect(params.skills).toEqual(['A']);
    });
  });

  describe('skills', () => {
    it('validates names: 1-50 characters without a comma', () => {
      expect(isValidSkillName('C#')).toBe(true);
      expect(isValidSkillName('')).toBe(false);
      expect(isValidSkillName('a,b')).toBe(false);
      expect(isValidSkillName('x'.repeat(51))).toBe(false);
    });

    it('addSkill trims, ignores duplicates (case-insensitive) and respects the limit', () => {
      expect(addSkill(['Angular'], '  Rust ')).toEqual(['Angular', 'Rust']);
      expect(addSkill(['Angular'], 'angular')).toEqual(['Angular']);
      expect(addSkill(['A', 'B'], 'C', 2)).toEqual(['A', 'B']);
      expect(addSkill([], 'a,b')).toEqual([]);
    });
  });
});
