import { APP_NAME, formatPageTitle } from './page-title';

describe('formatPageTitle', () => {
  it('appends the application name', () => {
    expect(formatPageTitle('Oferty pracy')).toBe('Oferty pracy – Job Board');
  });

  it.each([null, undefined, '', '   '])('returns just the application name for %j', part => {
    expect(formatPageTitle(part)).toBe(APP_NAME);
  });

  it('trims the page part', () => {
    expect(formatPageTitle('  Firmy ')).toBe('Firmy – Job Board');
  });
});
