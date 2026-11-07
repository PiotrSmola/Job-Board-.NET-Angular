import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { providePolishPaginatorIntl } from '../../services/polish-paginator-intl';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Subscription } from 'rxjs';
import { SavedJobsService } from '../../services/saved-jobs.service';
import { SavedJob } from '../../models/account.model';
import { getApiErrorMessage } from '../../utils/http-errors';
import { getJobState, getJobStatePresentation, StatusPresentation } from '../../utils/status-labels';
import { StatusChipComponent } from '../shared/status-chip.component';
import { SaveJobButtonComponent } from '../shared/save-job-button.component';

export const SAVED_JOBS_PAGE_SIZE = 10;

/**
 * Zapisane oferty kandydata (od najnowszych zapisów), także wygasłe i zamknięte – z informacją o stanie.
 * Usunięta z zapisanych pozycja zostaje na liście do przeładowania strony (serduszko w obrysie, „Usunięto z zapisanych”),
 * więc można ją od razu przywrócić, a fokus nie ginie. Zmiana strony przerywa poprzednie żądanie.
 */
@Component({
  selector: 'app-saved-jobs',
  providers: [providePolishPaginatorIntl()],
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, MatButtonModule, MatIconModule, MatPaginatorModule, MatProgressSpinnerModule,
    StatusChipComponent, SaveJobButtonComponent],
  templateUrl: './saved-jobs.component.html',
  styleUrl: './saved-jobs.component.scss'
})
export class SavedJobsComponent {
  readonly savedJobs = inject(SavedJobsService);
  private loadSubscription: Subscription | null = null;

  readonly pageSize = SAVED_JOBS_PAGE_SIZE;
  readonly items = signal<SavedJob[]>([]);
  readonly totalItems = signal(0);
  readonly pageNumber = signal(1);
  readonly loading = signal(true);
  readonly loadError = signal('');

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

  /** Stan oferty, gdy nie przyjmuje już aplikacji (wygasła / zamknięta / szkic); null dla aktywnej. */
  inactiveState(item: SavedJob): StatusPresentation | null {
    return getJobState(item.job) === 'active' ? null : getJobStatePresentation(item.job);
  }
}
