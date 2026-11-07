import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { PasswordStrengthLevel, getPasswordRequirements, getPasswordStrength } from '../../utils/password';

/** Ocena w zdaniu („Siła hasła: średnia”) – rodzaj żeński, bo odnosi się do „siły”. */
const LEVEL_WORDS: Record<PasswordStrengthLevel, string> = {
  0: '',
  1: 'słaba',
  2: 'średnia',
  3: 'dobra',
  4: 'silna'
};

/** Opis siły hasła tekstem, np. „Siła hasła: średnia. Dodaj znak specjalny lub wydłuż hasło.” */
export function describePasswordStrength(password: string | null | undefined): string {
  const value = password ?? '';
  const { level } = getPasswordStrength(value);
  const requirements = getPasswordRequirements(value);

  if (level === 0) {
    return 'Hasło: co najmniej 6 znaków, mała i wielka litera oraz cyfra.';
  }
  const prefix = `Siła hasła: ${LEVEL_WORDS[level]}.`;
  if (level === 1) {
    const missing = requirements.filter(r => !r.met).map(r => r.label.toLowerCase());
    return `${prefix} Brakuje: ${missing.join(', ')}.`;
  }
  if (level === 2) return `${prefix} Dodaj znak specjalny lub wydłuż hasło.`;
  if (level === 3) return `${prefix} Dłuższe hasło będzie jeszcze bezpieczniejsze.`;
  return prefix;
}

/**
 * Wskaźnik siły hasła (makieta Logowanie.dc.html): pasek (tylko wizualnie) i zdanie z oceną oraz wskazówką,
 * czego brakuje – informacja nie tylko kolorem. Pole hasła wskazuje go przez `aria-describedby`
 * (id nadane elementowi `<app-password-strength id="…">`), więc czytnik odczyta opis przy fokusie – bez ogłaszania każdego znaku.
 */
@Component({
  selector: 'app-password-strength',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="bar" aria-hidden="true">
      <span class="fill" [attr.data-level]="level()" [style.width.%]="level() * 25"></span>
    </span>
    <span class="text">{{ description() }}</span>
  `,
  styles: [`
    :host {
      display: flex;
      align-items: center;
      gap: 8px;
      margin: 4px 0 16px;
      color: var(--color-text-tertiary);
      font-size: var(--font-size-sm);
    }
    .bar {
      flex: 0 0 72px;
      height: 4px;
      border-radius: 2px;
      background: var(--color-selected);
      overflow: hidden;
    }
    .fill { display: block; height: 100%; background: var(--status-rejected); }
    .fill[data-level="2"] { background: var(--status-review); }
    .fill[data-level="3"], .fill[data-level="4"] { background: var(--status-hired); }
    .text { flex: 1; min-width: 0; }
  `]
})
export class PasswordStrengthComponent {
  readonly password = input<string | null>('');

  readonly level = computed(() => getPasswordStrength(this.password()).level);
  readonly description = computed(() => describePasswordStrength(this.password()));
}
