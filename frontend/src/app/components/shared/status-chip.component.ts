import { Component, input, ChangeDetectionStrategy } from '@angular/core';
import { StatusTone } from '../../utils/status-labels';

/** Kolorowa etykieta statusu oferty lub aplikacji. */
@Component({
  selector: 'app-status-chip',
  standalone: true,
  template: `<span [class]="'status-chip tone-' + tone()">{{ label() }}</span>`,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: [`
    :host { display: inline-flex; }

    .status-chip {
      display: inline-flex;
      align-items: center;
      padding: 2px 10px;
      border-radius: 12px;
      border: 1px solid currentColor;
      font-size: 0.8rem;
      font-weight: 600;
      line-height: 1.6;
      white-space: nowrap;
      background-color: color-mix(in srgb, currentColor 12%, transparent);
    }

    .tone-neutral { color: #b0bec5; }
    .tone-info { color: #64b5f6; }
    .tone-accent { color: var(--secondary-color, #e040fb); }
    .tone-warning { color: #ffb74d; }
    .tone-success { color: var(--success-color, #00e676); }
    .tone-danger { color: var(--error-color, #ff1744); }
    .tone-muted { color: #8a8a8a; }
  `]
})
export class StatusChipComponent {
  readonly label = input.required<string>();
  readonly tone = input<StatusTone>('neutral');
}
