import { getSafeReturnUrl } from './return-url';

describe('getSafeReturnUrl', () => {
  it.each([
    '/jobs/1?x=1',
    '/jobs',
    '/admin',
    '/my-applications#section',
    '/',
    '/loginfoo', // tylko dokładnie /login i /register są odrzucane
    '/registered/1',
    '/reset-passwords'
  ])('accepts internal path %s', url => {
    expect(getSafeReturnUrl(url)).toBe(url);
  });

  it.each([
    ['null', null],
    ['undefined', undefined],
    ['empty string', '']
  ])('rejects %s', (_label, url) => {
    expect(getSafeReturnUrl(url)).toBeNull();
  });

  it.each([
    '//evil.com',
    '//evil.com/path',
    '/\\evil.com',
    '/\\/evil.com',
    'http://evil.com',
    'https://evil.com/jobs',
    'javascript:alert(1)',
    'jobs/1',
    'evil.com'
  ])('rejects external or non-absolute url %s', url => {
    expect(getSafeReturnUrl(url)).toBeNull();
  });

  it.each([
    ['tab', '/\tevil'],
    ['newline', '/jobs\n/x'],
    ['carriage return', '/jobs\r'],
    ['null char', '/jobs\u0000'],
    ['DEL', '/jobs\u007f'],
    ['tab splitting a double slash', '/\t/evil.com']
  ])('rejects control characters (%s)', (_label, url) => {
    expect(getSafeReturnUrl(url)).toBeNull();
  });

  it.each([
    '/login',
    '/login?returnUrl=%2Fjobs',
    '/login/',
    '/login#x',
    '/register',
    '/register?x=1',
    '/forgot-password',
    '/reset-password?email=a%40b.c&token=abc',
    '/confirm-email?userId=1&token=abc'
  ])('rejects auth page %s', url => {
    expect(getSafeReturnUrl(url)).toBeNull();
  });
});
