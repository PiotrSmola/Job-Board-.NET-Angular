import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { JobOffer, WorkType } from '../../models/job-offer.model';
import { getContractTypeLabels, getSeniorityLabel } from '../../utils/job-labels';

/** Tryb pracy jako przysłówek do linii meta („Senior · B2B · Zdalnie”). */
const WORK_MODE_LABELS: Record<WorkType, string> = {
  [WorkType.Remote]: 'Zdalnie',
  [WorkType.Hybrid]: 'Hybrydowo',
  [WorkType.Onsite]: 'Stacjonarnie'
};

export function getWorkModeLabel(workType: WorkType | null | undefined): string | null {
  return workType === null || workType === undefined ? null : WORK_MODE_LABELS[workType] ?? null;
}

/** Linia meta oferty: poziom · typy umów · tryb pracy (puste części są pomijane). */
export function formatJobMeta(job: Pick<JobOffer, 'seniority' | 'contractTypes' | 'workType'>): string {
  const contracts = getContractTypeLabels(job.contractTypes).join(', ');
  return [getSeniorityLabel(job.seniority), contracts, getWorkModeLabel(job.workType)]
    .filter((part): part is string => !!part)
    .join(' · ');
}

/** Umiejętności jako zwykły tekst: „Angular, RxJS, Docker i 2 więcej”; `limit` = null pokazuje wszystkie. */
export function formatSkills(skills: readonly string[] | null | undefined, limit: number | null = null): string {
  const list = skills ?? [];
  const visible = limit === null ? list : list.slice(0, limit);
  const hidden = list.length - visible.length;
  return hidden > 0 ? `${visible.join(', ')} i ${hidden} więcej` : visible.join(', ');
}

const dayMonth = new Intl.DateTimeFormat('pl-PL', { day: 'numeric', month: 'long' });
const dayMonthYear = new Intl.DateTimeFormat('pl-PL', { day: 'numeric', month: 'long', year: 'numeric' });
const DAY_MS = 24 * 60 * 60 * 1000;

/** Data po polsku: „4 listopada” (bieżący rok) albo „4 listopada 2025”. */
export function formatDayMonth(value: Date | string, now: Date = new Date()): string {
  const date = new Date(value);
  return (date.getFullYear() === now.getFullYear() ? dayMonth : dayMonthYear).format(date);
}

/** Data dodania względnie: „dziś”, „wczoraj”, „3 dni temu”, „tydzień temu”, potem „4 listopada”. */
export function formatPosted(value: Date | string, now: Date = new Date()): string {
  const date = new Date(value);
  const startOf = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  const days = Math.round((startOf(now) - startOf(date)) / DAY_MS);
  if (days <= 0) return 'dziś';
  if (days === 1) return 'wczoraj';
  if (days < 7) return `${days} dni temu`;
  if (days < 14) return 'tydzień temu';
  return formatDayMonth(date, now);
}

/** Wartość atrybutu `datetime` (ISO) albo null dla niepoprawnej daty. */
export function isoDate(value: Date | string | null | undefined): string | null {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

/**
 * Linia meta (poziom · umowa · tryb pracy) i umiejętności oferty jako zwykły tekst – bez tagów i „pigułek”
 * (wzorzec karty z docs/CONTINUE-ui-refresh.md). `skillsLimit` = null pokazuje wszystkie umiejętności.
 */
@Component({
  selector: 'app-job-taxonomy',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (showMeta() && meta()) {
      <p class="meta">{{ meta() }}</p>
    }
    @if (skills()) {
      <p class="skills"><span class="visually-hidden">Umiejętności: </span>{{ skills() }}</p>
    }
  `,
  styles: [`
    :host { display: block; }
    p { margin: 0; }
    .meta { color: var(--color-text-secondary); }
    .skills { margin-top: 4px; color: var(--color-text-tertiary); font-size: var(--font-size-sm); }
  `]
})
export class JobTaxonomyComponent {
  readonly job = input.required<JobOffer>();
  readonly showMeta = input(true);
  readonly skillsLimit = input<number | null>(5);

  readonly meta = computed(() => formatJobMeta(this.job()));
  readonly skills = computed(() => formatSkills(this.job().skills, this.skillsLimit()));
}
