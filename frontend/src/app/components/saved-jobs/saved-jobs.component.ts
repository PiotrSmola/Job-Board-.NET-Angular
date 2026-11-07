import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { providePolishPaginatorIntl } from '../../services/polish-paginator-intl';
import { Subscription } from 'rxjs';
import { SavedJobsService } from '../../services/saved-jobs.service';
import { SavedJob } from '../../models/account.model';
import { getApiErrorMessage } from '../../utils/http-errors';
import { formatJobSalary, offersNoun } from '../../utils/job-labels';
import { getJobState, getJobStatePresentation, StatusPresentation } from '../../utils/status-labels';
import { StatusChipComponent } from '../shared/status-chip.component';
import { SaveJobButtonComponent } from '../shared/save-job-button.component';
import { JobTaxonomyComponent } from '../shared/job-taxonomy.component';

export const SAVED_JOBS_PAGE_SIZE = 10;

/** „3 lis” (z rokiem, gdy inny niż bieżący) – data zapisania oferty. */
export function formatSavedAt(date: string, now: Date = new Date()): string {
  const value = new Date(date);
  if (Number.isNaN(value.getTime())) return '';
  return value.toLocaleDateString('pl-PL', {
    day: 'numeric',
    month: 'short',
    ...(value.getFullYear() === now.getFullYear() ? {} : { year: 'numeric' })
  });
}

/**
 * Zapisane oferty kandydata (od najnowszych zapisów), także wygasłe i zamknięte – z informacją o stanie.
 * Karty jak na liście ofert (prosty widok, bez filtrów). Usunięta z zapisanych pozycja zostaje na liście do przeładowania
 * strony (serduszko w obrysie, „Usunięto z zapisanych”), więc można ją od razu przywrócić, a fokus nie ginie.
 * Zmiana strony przerywa poprzednie żądanie, opuszczenie strony – bieżące.
 */
@Component({
  selector: 'app-saved-jobs',
  providers: [providePolishPaginatorIntl()],
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatButtonModule, MatPaginatorModule, StatusChipComponent, SaveJobButtonComponent, JobTaxonomyComponent],
  templateUrl: './saved-jobs.component.html',
  styleUrl: './saved-jobs.component.scss'
})
export class SavedJobsComponent {
  readonly savedJobs = inject(SavedJobsService);
  private loadSubscription: Subscription | null = null;

  readonly pageSize = SAVED_JOBS_PAGE_SIZE;
  readonly skeletons = [1, 2, 3];
  readonly items = signal<SavedJob[]>([]);
  readonly totalItems = signal(0);
  readonly pageNumber = signal(1);
  readonly loading = signal(true);
  readonly loadError = signal('');
  readonly countLabel = computed(() => `${this.totalItems()} ${offersNoun(this.totalItems())}`);

  constructor() {
    this.load(1);
    inject(DestroyRef).onDestroy(() => this.loadSubscription?.unsubscribe());
  }

  load(pageNumber = this.pageNumber()): void {
    this.loadSubscription?.unsubscribe();
    this.loading.set(true);
    this.loadError.set('');
    this.pageNumber.set(pageNumber);

    this.loadSubscription = this.savedJobs.getSavedJobs(pageNumber, this.pageSize).subscribe({
      next: page => {
        this.items.set(page.result);
        this.totalItems.set(page.pagination.TotalItems);
        this.loading.set(false);
      },
      error: error => {
        console.error('Error loading saved jobs:', error);
        this.loadError.set(getApiErrorMessage(error, 'Nie udało się wczytać zapisanych ofert'));
        this.loading.set(false);
      }
    });
  }

  onPageChange(event: PageEvent): void {
    this.load(event.pageIndex + 1);
  }

  /** Widełki; null, gdy oferta ich nie podaje (nie pokazujemy „Nieokreślona” w karcie). */
  salary(item: SavedJob): string | null {
    const { salaryMin, salaryMax } = item.job;
    return (salaryMin ?? 0) > 0 || (salaryMax ?? 0) > 0 ? formatJobSalary(item.job) : null;
  }

  savedAt(item: SavedJob): string {
    return formatSavedAt(item.savedAt);
  }

  /** Stan oferty, gdy nie przyjmuje już aplikacji (wygasła / zamknięta / szkic); null dla aktywnej. */
  inactiveState(item: SavedJob): StatusPresentation | null {
    return getJobState(item.job) === 'active' ? null : getJobStatePresentation(item.job);
  }
}
