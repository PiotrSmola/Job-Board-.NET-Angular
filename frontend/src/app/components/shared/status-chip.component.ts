import { Component, input, ChangeDetectionStrategy } from '@angular/core';
import { StatusTone } from '../../utils/status-labels';

/**
 * Status oferty, aplikacji lub firmy: kolorowa kropka + etykieta tekstowa (informacja nie tylko kolorem).
 * Kolor kropki wynika z tonu (`StatusTone` → token `--status-*`), tekst ma kolor zwykłego tekstu.
 * Nazwa `status-chip` została dla zgodności API – wizualnie to już nie „pigułka”.
 */
@Component({
  selector: 'app-status-chip',
  standalone: true,
  template: `<span [class]="'status-chip tone-' + tone()"><span class="dot" aria-hidden="true"></span>{{ label() }}</span>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [`
    :host { display: inline-flex; max-width: 100%; }

    .status-chip {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      min-width: 0;
      color: var(--color-text, #e9ebee);
      line-height: 1.4;
      white-space: nowrap;
    }

    .dot {
      flex: none;
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background-color: var(--status-color);
      /* tryb wymuszonych kolorów: kropka nadal widoczna */
      forced-color-adjust: none;
    }

    .tone-info { --status-color: var(--status-submitted, #8fb3ff); }
    .tone-warning { --status-color: var(--status-review, #e3b15a); }
    .tone-accent { --status-color: var(--status-interview, #5fd4e6); }
    .tone-positive { --status-color: var(--status-offered, #8fd18f); }
    .tone-success { --status-color: var(--status-hired, #6fcf97); }
    .tone-danger { --status-color: var(--status-rejected, #f28b82); }
    .tone-neutral, .tone-muted { --status-color: var(--status-neutral, #8b929b); }
  `]
})
export class StatusChipComponent {
  readonly label = input.required<string>();
  readonly tone = input<StatusTone>('neutral');
}
