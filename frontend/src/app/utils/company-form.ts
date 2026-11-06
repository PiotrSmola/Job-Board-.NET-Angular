import { AbstractControl, FormBuilder, FormGroup, ValidationErrors, Validators } from '@angular/forms';
import { Company, SaveCompany } from '../models/company.model';

/** Adres strony firmy – tylko http/https (jak walidacja API). */
export const WEBSITE_PATTERN = /^https?:\/\/[^\s/$.?#].\S*$/i;

/**
 * Walidator strony www na przyciętej wartości (jak API, które obcina odstępy przed walidacją i zapisem):
 * „  https://firma.pl  ” jest poprawne, puste pole – też (pole opcjonalne). Błąd ma klucz `pattern`.
 */
export function websiteValidator(control: AbstractControl): ValidationErrors | null {
  const value = typeof control.value === 'string' ? control.value.trim() : '';
  if (!value || WEBSITE_PATTERN.test(value)) return null;
  return { pattern: { requiredPattern: String(WEBSITE_PATTERN), actualValue: control.value } };
}

// Limity zgodne z encją Company w API
export const COMPANY_NAME_MAX = 200;
export const COMPANY_DESCRIPTION_MAX = 5000;
export const COMPANY_WEBSITE_MAX = 300;
export const COMPANY_LOCATION_MAX = 200;

/** Pola danych firmy wspólne dla dialogu admina i profilu pracodawcy. */
export function buildCompanyForm(fb: FormBuilder, company: Company | null = null): FormGroup {
  return fb.group({
    name: [company?.name ?? '', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(COMPANY_NAME_MAX)]],
    description: [company?.description ?? '', [Validators.maxLength(COMPANY_DESCRIPTION_MAX)]],
    website: [company?.website ?? '', [Validators.maxLength(COMPANY_WEBSITE_MAX), websiteValidator]],
    location: [company?.location ?? '', [Validators.maxLength(COMPANY_LOCATION_MAX)]]
  });
}

function optional(value: string | null | undefined): string | null {
  const trimmed = (value ?? '').trim();
  return trimmed ? trimmed : null;
}

/** Wartość formularza → body API (przycięte, puste pola jako null). */
export function toSaveCompany(value: { name?: string | null; description?: string | null; website?: string | null; location?: string | null }): SaveCompany {
  return {
    name: (value.name ?? '').trim(),
    description: optional(value.description),
    website: optional(value.website),
    location: optional(value.location)
  };
}

/** Link do strony firmy tylko dla bezpiecznego schematu http/https. */
export function safeWebsiteUrl(website: string | null | undefined): string | null {
  return website && /^https?:\/\//i.test(website.trim()) ? website.trim() : null;
}
