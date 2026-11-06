import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { JobOffer } from '../../models/job-offer.model';
import { getContractTypeLabels, getSeniorityLabel } from '../../utils/job-labels';

/** Kategoria, poziom, typy umów (tagi) oraz umiejętności oferty; `skillsLimit` = null pokazuje wszystkie. */
@Component({
  selector: 'app-job-taxonomy',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (showTags() && tags().length > 0) {
      <ul class="tags" aria-label="Kategoria, poziom i typ umowy">
        @for (tag of tags(); track tag) {
          <li>{{ tag }}</li>
        }
      </ul>
    }
    @if (skills().length > 0) {
      <ul class="skills" aria-label="Umiejętności">
        @for (skill of visibleSkills(); track skill) {
          <li>{{ skill }}</li>
        }
        @if (hiddenCount() > 0) {
          <li class="more" [attr.aria-label]="'oraz ' + hiddenCount() + ' więcej'">+{{ hiddenCount() }}</li>
        }
      </ul>
    }
  `,
  styles: [`
    :host { display: block; }
    ul { display: flex; flex-wrap: wrap; gap: 6px; margin: 0 0 12px; padding: 0; list-style: none; }
    li { padding: 2px 10px; border-radius: 12px; font-size: 0.8rem; line-height: 1.6; color: var(--text-primary); }
    .tags li { border: 1px solid var(--primary-color); background: rgba(0, 229, 255, 0.08); }
    .skills li { border: 1px solid var(--border-color); background: rgba(255, 255, 255, 0.06); }
    .skills .more { color: var(--text-secondary); }
  `]
})
export class JobTaxonomyComponent {
  readonly job = input.required<JobOffer>();
  readonly showTags = input(true);
  readonly skillsLimit = input<number | null>(5);

  readonly tags = computed(() => {
    const job = this.job();
    const tags: string[] = [];
    if (job.categoryName) tags.push(job.categoryName);
    const seniority = getSeniorityLabel(job.seniority);
    if (seniority) tags.push(seniority);
    return [...tags, ...getContractTypeLabels(job.contractTypes)];
  });

  readonly skills = computed(() => this.job().skills ?? []);

  readonly visibleSkills = computed(() => {
    const limit = this.skillsLimit();
    return limit === null ? this.skills() : this.skills().slice(0, limit);
  });

  readonly hiddenCount = computed(() => this.skills().length - this.visibleSkills().length);
}
