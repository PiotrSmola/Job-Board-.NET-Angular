import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  JobTaxonomyComponent, formatDayMonth, formatFullDate, formatJobMeta, formatPosted, formatSkills, getWorkModeLabel, isoDate
} from './job-taxonomy.component';
import { ContractTypes, JobOffer, SeniorityLevel, WorkType } from '../../models/job-offer.model';
import { makeJob } from '../../../testing/job-fixtures';

describe('job taxonomy text', () => {
  it('builds the meta line from the level, the contract types and the work mode', () => {
    expect(formatJobMeta(makeJob(1, {
      seniority: SeniorityLevel.Senior, contractTypes: ContractTypes.B2B | ContractTypes.EmploymentContract, workType: WorkType.Remote
    }))).toBe('Senior · Umowa o pracę, B2B · Zdalnie');
  });

  it('skips the missing parts of the meta line', () => {
    expect(formatJobMeta(makeJob(1, { seniority: null, contractTypes: 0, workType: WorkType.Hybrid }))).toBe('Hybrydowo');
  });

  it('names the work modes as adverbs', () => {
    expect([WorkType.Remote, WorkType.Hybrid, WorkType.Onsite].map(getWorkModeLabel)).toEqual(['Zdalnie', 'Hybrydowo', 'Stacjonarnie']);
    expect(getWorkModeLabel(null)).toBeNull();
  });

  it('formats the posting date relative to today and then as a Polish date', () => {
    const now = new Date(2026, 10, 7, 12, 0, 0);
    expect(formatPosted(new Date(2026, 10, 7, 8), now)).toBe('dziś');
    expect(formatPosted(new Date(2026, 10, 6, 23), now)).toBe('wczoraj');
    expect(formatPosted(new Date(2026, 10, 4), now)).toBe('3 dni temu');
    expect(formatPosted(new Date(2026, 9, 30), now)).toBe('tydzień temu');
    expect(formatPosted(new Date(2026, 9, 4), now)).toBe('4 października');
    expect(formatDayMonth(new Date(2025, 2, 1), now)).toBe('1 marca 2025');
    expect(formatFullDate(new Date(2026, 2, 5))).toBe('5 marca 2026');
    expect(isoDate('nie-data')).toBeNull();
  });

  it('lists the skills as comma text with the number of the hidden ones', () => {
    expect(formatSkills(['A', 'B', 'C'])).toBe('A, B, C');
    expect(formatSkills(['A', 'B', 'C', 'D'], 2)).toBe('A, B i 2 więcej');
    expect(formatSkills(['A', 'B'], 2)).toBe('A, B');
    expect(formatSkills(null)).toBe('');
  });
});

describe('JobTaxonomyComponent', () => {
  let fixture: ComponentFixture<JobTaxonomyComponent>;

  const root = () => fixture.nativeElement as HTMLElement;

  function create(job: JobOffer, inputs: { showMeta?: boolean; skillsLimit?: number | null } = {}): void {
    fixture = TestBed.createComponent(JobTaxonomyComponent);
    fixture.componentRef.setInput('job', job);
    if (inputs.showMeta !== undefined) fixture.componentRef.setInput('showMeta', inputs.showMeta);
    if (inputs.skillsLimit !== undefined) fixture.componentRef.setInput('skillsLimit', inputs.skillsLimit);
    fixture.detectChanges();
  }

  beforeEach(() => TestBed.configureTestingModule({ imports: [JobTaxonomyComponent] }));

  it('renders plain text lines, no lists or tags', () => {
    create(makeJob(1, { seniority: SeniorityLevel.Junior, skills: ['Angular', 'RxJS'] }));

    expect(root().querySelector('ul')).toBeNull();
    expect(root().querySelector('.meta')?.textContent?.trim()).toBe('Junior · Zdalnie');
    expect(root().querySelector('.skills')?.textContent?.trim()).toBe('Umiejętności: Angular, RxJS');
    expect(root().querySelector('.skills .visually-hidden')?.textContent).toBe('Umiejętności: ');
  });

  it('shows at most 5 skills by default', () => {
    create(makeJob(1, { skills: ['A', 'B', 'C', 'D', 'E', 'F', 'G'] }));

    expect(root().querySelector('.skills')?.textContent).toContain('A, B, C, D, E i 2 więcej');
  });

  it('shows every skill and no meta line when the limit is removed and the meta is hidden', () => {
    create(makeJob(1, { skills: ['A', 'B', 'C', 'D', 'E', 'F', 'G'] }), { showMeta: false, skillsLimit: null });

    expect(root().querySelector('.skills')?.textContent).toContain('A, B, C, D, E, F, G');
    expect(root().querySelector('.meta')).toBeNull();
  });

  it('renders no skills line for an offer without skills', () => {
    create(makeJob());

    expect(root().querySelector('.skills')).toBeNull();
  });
});
