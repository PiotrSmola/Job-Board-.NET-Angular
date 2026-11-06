// Ograniczenia pliku CV zgodne z API (typ i tak weryfikuje serwer po zawartości pliku)
export const CV_MAX_SIZE_BYTES = 5 * 1024 * 1024;
export const CV_ALLOWED_EXTENSIONS: readonly string[] = ['.pdf', '.docx'];
/** Wartość atrybutu `accept` pola wyboru pliku. */
export const CV_ACCEPT = CV_ALLOWED_EXTENSIONS.join(',');

/** Komunikat błędu walidacji pliku CV po stronie klienta albo null, gdy plik jest poprawny. */
export function validateCvFile(file: Pick<File, 'name' | 'size'>): string | null {
  const name = file.name.toLowerCase();
  if (!CV_ALLOWED_EXTENSIONS.some(ext => name.endsWith(ext))) {
    return 'Nieobsługiwany typ pliku – wybierz plik PDF lub DOCX';
  }
  if (file.size === 0) {
    return 'Wybrany plik jest pusty';
  }
  if (file.size > CV_MAX_SIZE_BYTES) {
    return 'Plik jest za duży – maksymalny rozmiar to 5 MB';
  }
  return null;
}

/** Czytelny rozmiar pliku po polsku, np. „850 KB”, „1,2 MB”. */
export function formatFileSize(bytes: number | null | undefined): string {
  if (bytes === null || bytes === undefined || !Number.isFinite(bytes) || bytes < 0) return '';
  if (bytes < 1024) return `${bytes} B`;

  const kb = bytes / 1024;
  if (kb < 1024) return `${Math.round(kb)} KB`;

  const mb = kb / 1024;
  return `${mb.toLocaleString('pl-PL', { maximumFractionDigits: 1 })} MB`;
}
