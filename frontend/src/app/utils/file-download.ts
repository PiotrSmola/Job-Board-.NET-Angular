/**
 * Zapisuje pobrany plik na dysku użytkownika (tymczasowy link z `download`).
 * Adres obiektu jest zwalniany po rozpoczęciu pobierania.
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
    // niektóre przeglądarki zaczynają pobieranie asynchronicznie – zwalniamy adres w kolejnym takcie
    setTimeout(() => URL.revokeObjectURL(url));
  }
}
