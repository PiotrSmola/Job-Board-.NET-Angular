import { AttachmentKind } from '../models/job-application.model';

// Ograniczenia załączników zgodne z API (typ i tak weryfikuje serwer po zawartości pliku)
export const ATTACHMENT_MAX_FILE_SIZE = 5 * 1024 * 1024;
export const ATTACHMENTS_MAX_TOTAL_SIZE = 25 * 1024 * 1024;
export const ATTACHMENTS_MAX_COUNT = 10;
export const ATTACHMENT_ALLOWED_EXTENSIONS: readonly string[] = ['.pdf', '.docx', '.png', '.jpg', '.jpeg'];
/** Wartość atrybutu `accept` pola wyboru plików. */
export const ATTACHMENT_ACCEPT = ATTACHMENT_ALLOWED_EXTENSIONS.join(',');

/** Komunikat przekroczenia limitu liczby plików aplikacji. */
export const APPLICATION_COUNT_LIMIT_MESSAGE = `Aplikacja może mieć maksymalnie ${ATTACHMENTS_MAX_COUNT} załączników`;
/** Ten sam limit dla dokumentów profilu (10 plików, 25 MB łącznie). */
export const PROFILE_COUNT_LIMIT_MESSAGE = `Profil może mieć maksymalnie ${ATTACHMENTS_MAX_COUNT} dokumentów`;
export const PROFILE_TOTAL_SIZE_LIMIT_MESSAGE = 'Łączny rozmiar dokumentów profilu nie może przekroczyć 25 MB';
const APPLICATION_TOTAL_SIZE_LIMIT_MESSAGE = 'Łączny rozmiar załączników nie może przekroczyć 25 MB';

export const ATTACHMENT_KIND_LABELS: Record<AttachmentKind, string> = {
  [AttachmentKind.Cv]: 'CV',
  [AttachmentKind.CoverLetter]: 'List motywacyjny',
  [AttachmentKind.Portfolio]: 'Portfolio',
  [AttachmentKind.Other]: 'Inny'
};

export const ATTACHMENT_KIND_OPTIONS: readonly { value: AttachmentKind; label: string }[] =
  [AttachmentKind.Cv, AttachmentKind.CoverLetter, AttachmentKind.Portfolio, AttachmentKind.Other]
    .map(value => ({ value, label: ATTACHMENT_KIND_LABELS[value] }));

/** Polska etykieta rodzaju; nieznana wartość (np. nowsza wersja API) → „Inny”. */
export function getAttachmentKindLabel(kind: AttachmentKind): string {
  return ATTACHMENT_KIND_LABELS[kind] ?? ATTACHMENT_KIND_LABELS[AttachmentKind.Other];
}

/** Komunikat błędu walidacji pojedynczego pliku po stronie klienta albo null, gdy plik jest poprawny. */
export function validateAttachmentFile(file: Pick<File, 'name' | 'size'>): string | null {
  const name = file.name.toLowerCase();
  if (!ATTACHMENT_ALLOWED_EXTENSIONS.some(ext => name.endsWith(ext))) {
    return 'Nieobsługiwany typ pliku – wybierz plik PDF, DOCX, PNG lub JPEG';
  }
  if (file.size === 0) {
    return 'Wybrany plik jest pusty';
  }
  if (file.size > ATTACHMENT_MAX_FILE_SIZE) {
    return 'Plik jest za duży – maksymalny rozmiar to 5 MB';
  }
  return null;
}

/** Wynik doboru nowych plików: przyjęte pliki i komunikaty dla odrzuconych (z nazwą pliku). */
export interface AttachmentSelection {
  accepted: File[];
  errors: string[];
}

/**
 * Dobiera nowo wybrane pliki do już istniejących (na serwerze i na liście) z zachowaniem limitów:
 * typ i rozmiar pliku, liczba załączników (10) i łączny rozmiar (25 MB). Pliki sprawdzane są po kolei –
 * przyjmujemy te, które się mieszczą, a dla pozostałych zwracamy powód.
 */
export function selectAttachmentFiles(
  files: readonly File[],
  current: { count: number; totalSize: number; files?: readonly File[] },
  messages: { count: string; totalSize: string } = { count: APPLICATION_COUNT_LIMIT_MESSAGE, totalSize: APPLICATION_TOTAL_SIZE_LIMIT_MESSAGE }
): AttachmentSelection {
  const accepted: File[] = [];
  const errors: string[] = [];
  let count = current.count;
  let totalSize = current.totalSize;

  for (const file of files) {
    const error = validateAttachmentFile(file)
      ?? (isSameFileListed(file, [...(current.files ?? []), ...accepted]) ? 'Ten plik jest już na liście' : null)
      ?? (count >= ATTACHMENTS_MAX_COUNT ? messages.count : null)
      ?? (totalSize + file.size > ATTACHMENTS_MAX_TOTAL_SIZE ? messages.totalSize : null);

    if (error) {
      errors.push(`${file.name}: ${error}`);
      continue;
    }
    accepted.push(file);
    count++;
    totalSize += file.size;
  }

  return { accepted, errors };
}

/** Domyślny rodzaj nowego pliku: CV, dopóki aplikacja go nie ma, potem „Inny”. */
export function defaultAttachmentKind(usedKinds: readonly AttachmentKind[]): AttachmentKind {
  return usedKinds.includes(AttachmentKind.Cv) ? AttachmentKind.Other : AttachmentKind.Cv;
}

function isSameFileListed(file: File, listed: readonly File[]): boolean {
  return listed.some(other => other === file
    || (other.name === file.name && other.size === file.size && other.lastModified === file.lastModified));
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

/** Licznik wykorzystania limitu, np. „Wykorzystano 3,5 MB z 25 MB”. */
export function formatAttachmentsUsage(totalBytes: number): string {
  const used = totalBytes > 0 ? formatFileSize(totalBytes) : '0 MB';
  return `Wykorzystano ${used} z 25 MB`;
}
