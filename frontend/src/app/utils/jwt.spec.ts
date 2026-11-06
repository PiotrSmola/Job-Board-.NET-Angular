import { decodeJwtPayload, getJwtExpiration, getJwtRoles, getJwtUserId, isJwtExpired } from './jwt';
import { MS_ROLE_CLAIM, base64UrlEncode, makeJwt } from '../../testing/jwt-helper';

describe('jwt utils', () => {
  describe('decodeJwtPayload', () => {
    it('decodes a base64url payload', () => {
      expect(decodeJwtPayload(makeJwt({ sub: '1', exp: 123 }))).toEqual({ sub: '1', exp: 123 });
    });

    it('decodes UTF-8 characters (Polish letters) correctly', () => {
      const payload = { name: 'Zażółć gęślą jaźń', surname: 'Łukasiewicz' };
      expect(decodeJwtPayload(makeJwt(payload))).toEqual(payload);
    });

    it('decodes base64url with "-" and "_" characters and no padding', () => {
      const payload = { a: '>>>???~~~' };
      const token = makeJwt(payload);
      expect(token.split('.')[1]).toMatch(/[-_]/);
      expect(token.split('.')[1]).not.toContain('=');
      expect(decodeJwtPayload(token)).toEqual(payload);
    });

    it.each([null, undefined, ''])('returns null for empty token (%s)', token => {
      expect(decodeJwtPayload(token)).toBeNull();
    });

    it('returns null for a token without three segments', () => {
      expect(decodeJwtPayload('abc')).toBeNull();
      expect(decodeJwtPayload('a.b')).toBeNull();
      expect(decodeJwtPayload('a.b.c.d')).toBeNull();
    });

    it('returns null for an empty payload segment', () => {
      expect(decodeJwtPayload('a..c')).toBeNull();
    });

    it('returns null for a payload that is not valid base64', () => {
      expect(decodeJwtPayload('a.!!!.c')).toBeNull();
      expect(decodeJwtPayload('a.abcde.c')).toBeNull(); // długość % 4 === 1
    });

    it('returns null for a payload that is not JSON', () => {
      expect(decodeJwtPayload(`a.${base64UrlEncode('not json')}.c`)).toBeNull();
    });

    it('returns null for JSON that is not an object', () => {
      expect(decodeJwtPayload(makeJwt('123'))).toBeNull();
      expect(decodeJwtPayload(makeJwt('"text"'))).toBeNull();
      expect(decodeJwtPayload(makeJwt('[1,2]'))).toBeNull();
      expect(decodeJwtPayload(makeJwt('null'))).toBeNull();
    });
  });

  describe('getJwtExpiration', () => {
    it('returns exp converted to milliseconds', () => {
      expect(getJwtExpiration(makeJwt({ exp: 2000 }))).toBe(2_000_000);
    });

    it('returns null when exp is missing', () => {
      expect(getJwtExpiration(makeJwt({ sub: '1' }))).toBeNull();
    });

    it('returns null when exp is not a number', () => {
      expect(getJwtExpiration(makeJwt({ exp: '2000' }))).toBeNull();
      expect(getJwtExpiration(makeJwt({ exp: null }))).toBeNull();
    });

    it('returns null for an unreadable token', () => {
      expect(getJwtExpiration('garbage')).toBeNull();
      expect(getJwtExpiration(null)).toBeNull();
    });
  });

  describe('isJwtExpired', () => {
    const now = 1_700_000_000_000;

    it('is false for a token expiring in the future', () => {
      expect(isJwtExpired(makeJwt({ exp: now / 1000 + 60 }), now)).toBe(false);
    });

    it('is true for a token that already expired', () => {
      expect(isJwtExpired(makeJwt({ exp: now / 1000 - 1 }), now)).toBe(true);
    });

    it('is true exactly at the expiration moment', () => {
      expect(isJwtExpired(makeJwt({ exp: now / 1000 }), now)).toBe(true);
    });

    it('treats a token without exp as expired', () => {
      expect(isJwtExpired(makeJwt({ sub: '1' }), now)).toBe(true);
    });

    it('treats an unreadable or missing token as expired', () => {
      expect(isJwtExpired('garbage', now)).toBe(true);
      expect(isJwtExpired(null, now)).toBe(true);
      expect(isJwtExpired(undefined, now)).toBe(true);
    });

    it('uses the current time by default', () => {
      const nowSeconds = Math.floor(Date.now() / 1000);
      expect(isJwtExpired(makeJwt({ exp: nowSeconds + 3600 }))).toBe(false);
      expect(isJwtExpired(makeJwt({ exp: nowSeconds - 3600 }))).toBe(true);
    });
  });

  describe('getJwtRoles', () => {
    it('reads a single "role" claim', () => {
      expect(getJwtRoles(makeJwt({ role: 'Admin' }))).toEqual(['Admin']);
    });

    it('reads an array "role" claim', () => {
      expect(getJwtRoles(makeJwt({ role: ['User', 'Admin'] }))).toEqual(['User', 'Admin']);
    });

    it('reads the long Microsoft role claim', () => {
      expect(getJwtRoles(makeJwt({ [MS_ROLE_CLAIM]: 'Admin' }))).toEqual(['Admin']);
      expect(getJwtRoles(makeJwt({ [MS_ROLE_CLAIM]: ['User', 'Admin'] }))).toEqual(['User', 'Admin']);
    });

    it('prefers the short "role" claim over the Microsoft one', () => {
      expect(getJwtRoles(makeJwt({ role: 'User', [MS_ROLE_CLAIM]: 'Admin' }))).toEqual(['User']);
    });

    it('filters out non-string values from a role array', () => {
      expect(getJwtRoles(makeJwt({ role: ['Admin', 5, null] }))).toEqual(['Admin']);
    });

    it('returns an empty array when there is no role claim', () => {
      expect(getJwtRoles(makeJwt({ sub: '1' }))).toEqual([]);
    });

    it('returns an empty array for an unreadable token', () => {
      expect(getJwtRoles('garbage')).toEqual([]);
      expect(getJwtRoles(null)).toEqual([]);
    });
  });

  describe('getJwtUserId', () => {
    it('reads the short nameid claim written by JwtSecurityTokenHandler', () => {
      expect(getJwtUserId(makeJwt({ nameid: 'u-1' }))).toBe('u-1');
    });

    it('falls back to sub and the long NameIdentifier claim', () => {
      expect(getJwtUserId(makeJwt({ sub: 'u-2' }))).toBe('u-2');
      expect(getJwtUserId(makeJwt({ 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier': 'u-3' }))).toBe('u-3');
    });

    it('returns null without a usable claim', () => {
      expect(getJwtUserId(makeJwt({ nameid: 5 }))).toBeNull();
      expect(getJwtUserId('garbage')).toBeNull();
      expect(getJwtUserId(null)).toBeNull();
    });
  });
});
