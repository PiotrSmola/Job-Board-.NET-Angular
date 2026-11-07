import { ChangeDetectionStrategy, Component, ElementRef, inject, input, output } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

export interface ChipItem {
  key: string;
  label: string;
  /** Etykieta dostępna przycisku usuwania (domyślnie „Usuń: {label}”). */
  removeLabel?: string;
}

/** Lista usuwalnych „chipów” (aktywne filtry, wybrane umiejętności) z zachowaniem fokusu po usunięciu. */
@Component({
  selector: 'app-removable-chips',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule],
  template: `
    @if (items().length > 0) {
      <ul class="chips" [attr.aria-label]="ariaLabel()">
        @for (item of items(); track item.key) {
          <li class="chip">
            <span>{{ item.label }}</span>
            <button type="button" (click)="onRemove(item.key, $event)" [attr.aria-label]="item.removeLabel ?? 'Usuń: ' + item.label">
              <mat-icon aria-hidden="true">close</mat-icon>
            </button>
          </li>
        }
      </ul>
    }
  `,
  styles: [`
    :host { display: block; }
    .chips { display: flex; flex-wrap: wrap; gap: 8px; margin: 0; padding: 0; list-style: none; }
    /* wybrana wartość = wypełnienie --color-selected (jak aktywna zakładka), bez ramki i akcentu */
    .chip {
      display: inline-flex; align-items: center; gap: 2px; padding: 2px 4px 2px 12px; border-radius: var(--radius-input);
      background: var(--color-selected); color: var(--color-text); font-size: var(--font-size-sm);
    }
    button {
      display: inline-flex; align-items: center; justify-content: center; width: 28px; height: 28px; padding: 0;
      border: 0; border-radius: 50%; background: transparent; color: var(--color-text-secondary); cursor: pointer;
    }
    button:hover { color: var(--color-text); background: var(--color-raised); }
    mat-icon { font-size: 18px; width: 18px; height: 18px; }
  `]
})
export class RemovableChipsComponent {
  private host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly items = input.required<ChipItem[]>();
  readonly ariaLabel = input<string | null>(null);
  /** Element, który dostaje fokus po usunięciu ostatniego chipa (inaczej fokus przepadłby w <body>). */
  readonly focusOnEmpty = input<HTMLElement | null>(null);

  readonly remove = output<string>();

  onRemove(key: string, event: Event): void {
    const buttons = Array.from(this.host.nativeElement.querySelectorAll<HTMLButtonElement>('button'));
    const index = buttons.indexOf(event.currentTarget as HTMLButtonElement);
    const neighbour = buttons[index + 1] ?? buttons[index - 1] ?? this.focusOnEmpty();
    // Sąsiedni przycisk zostaje w DOM po usunięciu – fokus przenosimy przed emisją zmiany
    neighbour?.focus();
    this.remove.emit(key);
  }
}
