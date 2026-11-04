// Akceptujemy tylko ścieżki wewnątrz aplikacji – ochrona przed open redirect
export function getSafeReturnUrl(returnUrl: string | null | undefined): string | null {
  if (!returnUrl || !returnUrl.startsWith('/')) return null;

  // "//host" i "/\host" przeglądarka potraktuje jako adres innej domeny
  if (returnUrl.startsWith('//') || returnUrl.startsWith('/\\')) return null;

  // Znaki sterujące (np. tabulator/nowa linia) bywają usuwane przez przeglądarkę z URL-a
  if (/[\u0000-\u001f\u007f]/.test(returnUrl)) return null;

  // Powrót na stronę logowania/rejestracji nie ma sensu
  if (/^\/(login|register)(?=[/?#]|$)/.test(returnUrl)) return null;

  return returnUrl;
}
