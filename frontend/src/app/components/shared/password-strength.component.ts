import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { getPasswordRequirements, getPasswordStrength } from '../../utils/password';

/**
 * Wskaźnik siły hasła – tekstem („Siła hasła: Średnie”) i paskiem, plus lista wymagań API ze stanem
 * „spełnione / niespełnione” podanym słownie (nie tylko kolorem i ikoną). Pole hasła wskazuje go przez
 * `aria-describedby` (id nadane elementowi `<app-password-strength id="…">`), więc czytnik odczyta stan przy fokusie – bez ogłaszania każdego znaku.
 */
@Component({
  selector: 'app-password-strength',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="password-strength">
      <p class="label">
        Siła hasła: <strong>{{ strength().label || 'brak' }}</strong>
      </p>
      <div class="bar" aria-hidden="true">
        @for (segment of segments; track segment) {
          <span class="segment" [class.filled]="segment <= strength().level" [attr.data-level]="strength().level"></span>
        }
      </div>
      <ul class="requirements">
        @for (requirement of requirements(); track requirement.label) {
          <li [class.met]="requirement.met">
            <span class="mark" aria-hidden="true">{{ requirement.met ? '✓' : '✗' }}</span>
            {{ requirement.label }}<span class="sr-only">{{ requirement.met ? ' – spełnione' : ' – niespełnione' }}</span>
          </li>
        }
      </ul>
    </div>
  `,
  styles: [`
    :host { display: block; margin: -8px 0 12px; }
    .label { margin: 0 0 4px; font-size: 0.85rem; color: var(--text-secondary, #b0b0b0); }
    .label strong { color: var(--text-primary, #fff); }
    .bar { display: flex; gap: 4px; margin-bottom: 6px; }
    .segment { flex: 1; height: 4px; border-radius: 2px; background: #444; }
    .segment.filled[data-level="1"] { background: #ff8a80; }
    .segment.filled[data-level="2"] { background: #ffb74d; }
    .segment.filled[data-level="3"], .segment.filled[data-level="4"] { background: #69f0ae; }
    .requirements { list-style: none; margin: 0; padding: 0; display: flex; flex-wrap: wrap; gap: 2px 16px;
      font-size: 0.8rem; color: var(--text-secondary, #b0b0b0); }
    .requirements li.met { color: #69f0ae; }
    .mark { display: inline-block; width: 1em; }
    .sr-only { position: absolute; width: 1px; height: 1px; margin: -1px; padding: 0; overflow: hidden;
      clip-path: inset(50%); white-space: nowrap; border: 0; }
  `]
})
export class PasswordStrengthComponent {
  readonly password = input<string | null>('');

  readonly segments = [1, 2, 3, 4];
  readonly strength = computed(() => getPasswordStrength(this.password()));
  readonly requirements = computed(() => getPasswordRequirements(this.password()));
}
