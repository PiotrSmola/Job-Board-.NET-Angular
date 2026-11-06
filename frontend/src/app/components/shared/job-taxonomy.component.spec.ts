import { ComponentFixture, TestBed } from '@angular/core/testing';
import { JobTaxonomyComponent } from './job-taxonomy.component';
import { ContractTypes, JobOffer, SeniorityLevel } from '../../models/job-offer.model';
import { makeJob } from '../../../testing/job-fixtures';

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('JobTaxonomyComponent', () => {
  let fixture: ComponentFixture<JobTaxonomyComponent>;

  const root = () => fixture.nativeElement as HTMLElement;
  const texts = (selector: string) => Array.from(root().querySelectorAll(selector)).map(e => e.textContent?.trim());

  function create(job: JobOffer, inputs: { showTags?: boolean; skillsLimit?: number | null } = {}): void {
    fixture = TestBed.createComponent(JobTaxonomyComponent);
    fixture.componentRef.setInput('job', job);
    if (inputs.showTags !== undefined) fixture.componentRef.setInput('showTags', inputs.showTags);
    if (inputs.skillsLimit !== undefined) fixture.componentRef.setInput('skillsLimit', inputs.skillsLimit);
    fixture.detectChanges();
  }

  beforeEach(() => TestBed.configureTestingModule({ imports: [JobTaxonomyComponent] }));

  it('renders nothing for an offer without taxonomy data', () => {
    create(makeJob());

    expect(root().querySelector('ul')).toBeNull();
  });

  it('lists category, level and contract types as labelled tags', () => {
    create(makeJob(1, {
      categoryName: 'Testowanie', seniority: SeniorityLevel.Intern,
      contractTypes: ContractTypes.Internship | ContractTypes.MandateContract
    }));

    expect(texts('.tags li')).toEqual(['Testowanie', 'Stażysta', 'Umowa zlecenie', 'Staż']);
    expect(root().querySelector('.tags')?.getAttribute('aria-label')).toBe('Kategoria, poziom i typ umowy');
  });

  it('shows at most 5 skills by default with a "+N" counter that has an accessible name', () => {
    create(makeJob(1, { skills: ['A', 'B', 'C', 'D', 'E', 'F', 'G'] }));

    expect(texts('.skills li')).toEqual(['A', 'B', 'C', 'D', 'E', '+2']);
    expect(root().querySelector('.more')?.getAttribute('aria-label')).toBe('oraz 2 więcej');
  });

  it('does not show "+N" when all skills fit', () => {
    create(makeJob(1, { skills: ['A', 'B', 'C', 'D', 'E'] }));

    expect(root().querySelector('.more')).toBeNull();
  });

  it('shows every skill and no tags when the limit is removed and tags are hidden', () => {
    create(makeJob(1, { categoryName: 'X', skills: ['A', 'B', 'C', 'D', 'E', 'F', 'G'] }), { showTags: false, skillsLimit: null });

    expect(texts('.skills li').length).toBe(7);
    expect(root().querySelector('.tags')).toBeNull();
  });
});
