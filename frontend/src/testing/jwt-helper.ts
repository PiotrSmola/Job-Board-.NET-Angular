// Pomocnik do testów: buduje (niepodpisane) tokeny JWT w formacie header.payload.signature (base64url)

export function base64UrlEncode(text: string): string {
  const bytes = new TextEncoder().encode(text);
  let binary = '';
  bytes.forEach(byte => (binary += String.fromCharCode(byte)));
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export function makeJwt(
  payload: Record<string, unknown> | string,
  header: Record<string, unknown> = { alg: 'HS256', typ: 'JWT' }
): string {
  const body = typeof payload === 'string' ? payload : JSON.stringify(payload);
  return `${base64UrlEncode(JSON.stringify(header))}.${base64UrlEncode(body)}.signature`;
}

/** Token wygasający za `secondsFromNow` sekund (ujemna wartość = już wygasły). */
export function makeJwtExpiringIn(secondsFromNow: number, extra: Record<string, unknown> = {}, now = Date.now()): string {
  return makeJwt({ exp: Math.floor(now / 1000) + secondsFromNow, ...extra });
}

export const MS_ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';
