import { Component, computed, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ApplicationStatusHistoryEntry } from '../../models/job-application.model';
import { getApplicationStatusPresentation } from '../../utils/status-labels';
import { StatusChipComponent } from './status-chip.component';

/** Oś czasu historii aplikacji (chronologicznie, najstarszy wpis na górze). */
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
            <div class="header">
              <app-status-chip [label]="entry.presentation.label" [tone]="entry.presentation.tone" />
              <time class="date">{{ entry.changedAt | date: 'dd.MM.yyyy HH:mm' }}</time>
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
          </li>
        }
      </ol>
    }
  `,
  styles: [`
    .timeline {
      list-style: none;
      margin: 0;
      padding: 0 0 0 16px;
      border-left: 2px solid var(--border-color, #333);
    }

    .entry {
      position: relative;
      padding: 0 0 16px 16px;

      &::before {
        content: '';
        position: absolute;
        left: -23px;
        top: 6px;
        width: 12px;
        height: 12px;
        border-radius: 50%;
        background-color: var(--primary-color, #00e5ff);
      }

      &:last-child { padding-bottom: 0; }
    }

    .header {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: 8px;
    }

    .date, .kind, .author {
      color: var(--text-secondary, #b0b0b0);
      font-size: 0.85rem;
    }

    .kind { font-style: italic; }

    .comment {
      margin: 6px 0 0;
      white-space: pre-wrap;
      color: var(--text-primary, #fff);
    }

    .author { margin: 4px 0 0; }

    .empty {
      margin: 0;
      color: var(--text-secondary, #b0b0b0);
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
