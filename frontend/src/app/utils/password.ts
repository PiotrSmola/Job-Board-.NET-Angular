import { HttpErrorResponse } from '@angular/common/http';
import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

// Reguły haseł zgodne z ASP.NET Identity w API (Program.cs): min. 6 znaków, mała i wielka litera, cyfra
export const PASSWORD_MIN_LENGTH = 6;
/** Górny limit (ochrona przed bardzo długimi danymi – API stosuje ten sam). */
export const PASSWORD_MAX_LENGTH = 100;

export interface PasswordRequirement {
  label: string;
  met: boolean;
}

/** Lista wymagań z informacją, czy hasło je spełnia (pokazywana tekstowo przy polu). */
export function getPasswordRequirements(password: string | null | undefined): PasswordRequirement[] {
  const value = password ?? '';
  return [
    { label: `Co najmniej ${PASSWORD_MIN_LENGTH} znaków`, met: value.length >= PASSWORD_MIN_LENGTH },
    { label: 'Mała litera', met: /[a-z]/.test(value) },
    { label: 'Wielka litera', met: /[A-Z]/.test(value) },
    { label: 'Cyfra', met: /\d/.test(value) }
  ];
}

/** Walidator reguł składu hasła (długość sprawdzają Validators.minLength/maxLength). */
export const passwordPolicyValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const value: string = control.value ?? '';
  if (!value) return null;

  const missing = getPasswordRequirements(value).slice(1).filter(r => !r.met).map(r => r.label.toLowerCase());
  return missing.length ? { passwordPolicy: { missing } } : null;
};

/** Walidator grupy: pole `confirmKey` musi być równe `passwordKey` (błąd `passwordMismatch` na grupie). */
export function passwordsMatchValidator(passwordKey: string, confirmKey: string): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null => {
    const password = group.get(passwordKey)?.value ?? '';
    const confirm = group.get(confirmKey)?.value ?? '';
    return confirm && password !== confirm ? { passwordMismatch: true } : null;
  };
}

export type PasswordStrengthLevel = 0 | 1 | 2 | 3 | 4;

export interface PasswordStrength {
  /** 0 = brak hasła, 1 = słabe (nie spełnia wymagań), 2 = średnie, 3 = dobre, 4 = silne. */
  level: PasswordStrengthLevel;
  label: string;
}

const STRENGTH_LABELS: Record<PasswordStrengthLevel, string> = {
  0: '',
  1: 'Słabe',
  2: 'Średnie',
  3: 'Dobre',
  4: 'Silne'
};

/** Prosta ocena siły hasła – wymagania API plus długość i znaki specjalne. */
export function getPasswordStrength(password: string | null | undefined): PasswordStrength {
  const value = password ?? '';
  if (!value) return { level: 0, label: STRENGTH_LABELS[0] };

  const meetsPolicy = getPasswordRequirements(value).every(r => r.met);
  if (!meetsPolicy) return { level: 1, label: STRENGTH_LABELS[1] };

  const bonus = (value.length >= 10 ? 1 : 0) + (value.length >= 14 ? 1 : 0) + (/[^A-Za-z0-9]/.test(value) ? 1 : 0);
  const level: PasswordStrengthLevel = bonus === 0 ? 2 : bonus === 1 ? 3 : 4;
  return { level, label: STRENGTH_LABELS[level] };
}

// Domyślne komunikaty ASP.NET Identity (IdentityErrorDescriber) przetłumaczone na polski
const IDENTITY_PASSWORD_MESSAGES: { pattern: RegExp; message: string }[] = [
  { pattern: /^Passwords must be at least \d+ characters/i, message: `Hasło musi mieć co najmniej ${PASSWORD_MIN_LENGTH} znaków` },
  { pattern: /^Passwords must have at least one digit/i, message: 'Hasło musi zawierać cyfrę' },
  { pattern: /^Passwords must have at least one lowercase/i, message: 'Hasło musi zawierać małą literę' },
  { pattern: /^Passwords must have at least one uppercase/i, message: 'Hasło musi zawierać wielką literę' },
  { pattern: /^Passwords must have at least one non alphanumeric/i, message: 'Hasło musi zawierać znak specjalny' },
  { pattern: /^Passwords must use at least \d+ different characters/i, message: 'Hasło musi zawierać więcej różnych znaków' }
];

/**
 * Przetłumaczone błędy hasła z ValidationProblem (400 z `errors`) – np. słabe nowe hasło odrzucone przez Identity.
 * Zwraca pustą listę, gdy odpowiedź nie zawiera rozpoznanych błędów hasła.
 */
export function getPasswordErrorMessages(error: unknown): string[] {
  if (!(error instanceof HttpErrorResponse) || error.status !== 400) return [];

  const errors = error.error?.errors;
  if (!errors || typeof errors !== 'object') return [];

  const messages = new Set<string>();
  for (const value of Object.values(errors as Record<string, unknown>)) {
    for (const text of Array.isArray(value) ? value : [value]) {
      if (typeof text !== 'string') continue;
      const known = IDENTITY_PASSWORD_MESSAGES.find(m => m.pattern.test(text));
      if (known) messages.add(known.message);
    }
  }
  return [...messages];
}
