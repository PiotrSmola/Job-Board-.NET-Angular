// Pomocnicze funkcje do odczytu payloadu JWT (bez weryfikacji podpisu – to robi backend)

const ROLE_CLAIMS = ['role', 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role'];

export type JwtPayload = Record<string, unknown>;

function base64UrlDecode(segment: string): string {
  let base64 = segment.replace(/-/g, '+').replace(/_/g, '/');
  const padding = base64.length % 4;
  if (padding === 1) {
    throw new Error('Invalid base64url string');
  }
  if (padding) {
    base64 += '='.repeat(4 - padding);
  }

  // atob zwraca bajty jako znaki – dekodujemy je jako UTF-8 (polskie znaki w imionach)
  const binary = atob(base64);
  const bytes = Uint8Array.from(binary, char => char.charCodeAt(0));
  return new TextDecoder().decode(bytes);
}

export function decodeJwtPayload(token: string | null | undefined): JwtPayload | null {
  if (!token) return null;

  const parts = token.split('.');
  if (parts.length !== 3 || !parts[1]) return null;

  try {
    const payload: unknown = JSON.parse(base64UrlDecode(parts[1]));
    return payload && typeof payload === 'object' && !Array.isArray(payload)
      ? (payload as JwtPayload)
      : null;
  } catch {
    return null;
  }
}

/** Zwraca czas wygaśnięcia tokena w ms (epoch) albo null, gdy token jest nieczytelny lub nie ma `exp`. */
export function getJwtExpiration(token: string | null | undefined): number | null {
  const exp = decodeJwtPayload(token)?.['exp'];
  return typeof exp === 'number' && Number.isFinite(exp) ? exp * 1000 : null;
}

/** Token bez czytelnego `exp` traktujemy jako nieważny. */
export function isJwtExpired(token: string | null | undefined, now = Date.now()): boolean {
  const expiration = getJwtExpiration(token);
  return expiration === null || expiration <= now;
}

export function getJwtRoles(token: string | null | undefined): string[] {
  const payload = decodeJwtPayload(token);
  if (!payload) return [];

  for (const claim of ROLE_CLAIMS) {
    const value = payload[claim];
    if (typeof value === 'string') return [value];
    if (Array.isArray(value)) return value.filter((role): role is string => typeof role === 'string');
  }
  return [];
}
