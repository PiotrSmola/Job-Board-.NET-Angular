// Nazwa pobieranego pliku z nagłówka Content-Disposition (RFC 6266 / RFC 5987)

const EXTENDED_FILENAME = /(?:^|;)\s*filename\*\s*=\s*([^;]*)/i;
const PLAIN_FILENAME = /(?:^|;)\s*filename\s*=\s*(?:"((?:\\.|[^"\\])*)"|([^;]*))/i;
const CONTROL_CHARS = /[\u0000-\u001f\u007f-\u009f]/g;
// znaki niedozwolone w nazwach plików Windows (m.in. ':' – strumienie NTFS)
const RESERVED_CHARS = /[<>:"|?*]/g;

/**
 * Nazwa pliku z nagłówka: najpierw `filename*=` (np. `UTF-8''%C5%BC.pdf`), potem `filename=`.
 * Zwraca null, gdy nagłówka brak albo nie da się z niego odczytać nazwy.
 */
export function getFileNameFromContentDisposition(header: string | null | undefined): string | null {
  if (!header) return null;

  const extended = EXTENDED_FILENAME.exec(header);
  if (extended) {
    const name = sanitizeFileName(decodeExtendedValue(extended[1].trim()));
    if (name) return name;
  }

  const plain = PLAIN_FILENAME.exec(header);
  if (plain) {
    const value = plain[1] !== undefined ? plain[1].replace(/\\(.)/g, '$1') : plain[2].trim();
    const name = sanitizeFileName(value);
    if (name) return name;
  }

  return null;
}

/** Nazwa do zapisu pliku: z nagłówka, potem `fallback` (np. `cvFileName` z DTO), na końcu `defaultName`. */
export function resolveDownloadFileName(
  header: string | null | undefined,
  fallback: string | null | undefined,
  defaultName = 'cv'
): string {
  return getFileNameFromContentDisposition(header) ?? sanitizeFileName(fallback ?? null) ?? defaultName;
}

/** Wartość w formacie `charset'język'zakodowany-tekst`; null, gdy format lub kodowanie są błędne. */
function decodeExtendedValue(value: string): string | null {
  const match = /^([\w!#$%&+^`{}~-]*)'[^']*'(.*)$/.exec(value.replace(/^"(.*)"$/, '$1'));
  if (!match) return null;

  const [, charset, encoded] = match;
  try {
    if (/^utf-8$/i.test(charset)) {
      return decodeURIComponent(encoded);
    }
    if (/^iso-8859-1$/i.test(charset)) {
      return encoded.replace(/%([0-9a-f]{2})/gi, (_, hex: string) => String.fromCharCode(parseInt(hex, 16)));
    }
  } catch {
    // niepoprawna sekwencja %XX – próbujemy zwykłego filename=
  }
  return null;
}

/** Bez ścieżek, znaków sterujących i zarezerwowanych (zamieniane na '_'); null dla pustej nazwy. */
function sanitizeFileName(name: string | null): string | null {
  if (!name) return null;
  const baseName = name.split(/[/\\]/).pop() ?? '';
  const cleaned = baseName.replace(CONTROL_CHARS, '').replace(RESERVED_CHARS, '_').trim();
  return cleaned && cleaned !== '.' && cleaned !== '..' ? cleaned : null;
}
