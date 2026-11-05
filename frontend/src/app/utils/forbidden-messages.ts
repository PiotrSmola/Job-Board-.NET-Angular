// Komunikaty API dla 403 z konkretnym powodem – osobny mały moduł, bo używa go interceptor (paczka startowa)
export const FORBIDDEN_API_MESSAGES: Record<string, string> = {
  'Company must be approved before publishing offers': 'Firma musi zostać zatwierdzona, zanim opublikujesz ofertę'
};

/** Polski komunikat dla znanego powodu odmowy (403) albo null. */
export function translateForbiddenMessage(message: unknown): string | null {
  return typeof message === 'string' ? FORBIDDEN_API_MESSAGES[message] ?? null : null;
}
