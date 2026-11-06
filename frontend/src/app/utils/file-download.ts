/**
 * Czas, po którym zwalniamy adres `blob:`. Część przeglądarek (np. Firefox, Safari) zaczyna czytać plik
 * dopiero po kliknięciu, a przy dużych plikach i wolnym dysku może to potrwać – 0 ms potrafi przerwać pobieranie.
 */
export const OBJECT_URL_REVOKE_DELAY_MS = 30_000;

/**
 * Zapisuje pobrany plik na dysku użytkownika (tymczasowy link z `download`).
 * Adres obiektu jest zwalniany po ~30 s (zawsze – także gdy kliknięcie rzuci wyjątek), więc Blob nie zostaje w pamięci.
 */
export function saveBlobAsFile(blob: Blob, fileName: string, doc: Document = document): void {
  const url = URL.createObjectURL(blob);
  const link = doc.createElement('a');
  link.href = url;
  link.download = fileName;
  link.rel = 'noopener';
  link.style.display = 'none';
  doc.body.appendChild(link);

  try {
    link.click();
  } finally {
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), OBJECT_URL_REVOKE_DELAY_MS);
  }
}
