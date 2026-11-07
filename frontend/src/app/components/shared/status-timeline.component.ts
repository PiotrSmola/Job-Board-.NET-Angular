import { Component, computed, input, ChangeDetectionStrategy } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ApplicationStatusHistoryEntry } from '../../models/job-application.model';
import { getApplicationStatusPresentation } from '../../utils/status-labels';
import { StatusChipComponent } from './status-chip.component';

/**
 * Oś czasu historii aplikacji (chronologicznie, najstarszy wpis na górze): data w lewej kolumnie,
 * po prawej status (kropka + etykieta), komentarz i autor zmiany.
 */
@Component({
  selector: 'app-status-timeline',
  standalone: true,
  imports: [DatePipe, StatusChipComponent],
  template: `
    @if (entries().length === 0) {
      <p class="empty">Brak historii</p>
    } @else {
      <ol class="timeline">
        @for (entry of entries(); track $index) {
          <li class="entry">
            <time class="date" [attr.datetime]="entry.changedAt">{{ entry.changedAt | date: 'dd.MM.yyyy HH:mm' }}</time>
            <div class="body">
              <div class="header">
                <app-status-chip [label]="entry.presentation.label" [tone]="entry.presentation.tone" />
                @if (entry.isNote) {
                  <span class="kind">notatka</span>
                }
              </div>
              @if (entry.comment) {
                <p class="comment">{{ entry.comment }}</p>
              }
              @if (entry.changedBy) {
                <p class="author">{{ entry.changedBy }}</p>
              }
            </div>
          </li>
        }
      </ol>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [`
    .timeline {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 14px;
    }

    .entry {
      display: grid;
      grid-template-columns: minmax(0, 120px) minmax(0, 1fr);
      gap: 4px 12px;
    }

    .date {
      color: var(--color-text-tertiary, #8b929b);
      font-variant-numeric: tabular-nums;
      line-height: 1.4;
    }

    .header {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: 4px 8px;
    }

    .kind, .author {
      color: var(--color-text-tertiary, #8b929b);
      font-size: var(--font-size-sm, 13px);
    }

    .comment {
      margin: 4px 0 0;
      white-space: pre-wrap;
      overflow-wrap: anywhere;
      color: var(--color-text-prose, #d4d8dd);
    }

    .author { margin: 2px 0 0; }

    .empty {
      margin: 0;
      color: var(--color-text-secondary, #a7adb5);
    }

    /* wąsko: data nad wpisem */
    @media (max-width: 480px) {
      .entry { grid-template-columns: minmax(0, 1fr); }
    }
  `]
})
export class StatusTimelineComponent {
  readonly history = input<ApplicationStatusHistoryEntry[] | null | undefined>([]);

  readonly entries = computed(() => {
    const history = this.history() ?? [];
    return history.map((entry, index) => ({
      ...entry,
      presentation: getApplicationStatusPresentation(entry.status),
      // ten sam status co poprzedni wpis = notatka rekrutera bez zmiany statusu
      isNote: index > 0 && history[index - 1].status === entry.status
    }));
  });
}
