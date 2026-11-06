import {
  MAX_RANGE_DAYS,
  dayToLocalDate,
  defaultRange,
  enumerateDays,
  formatFullDay,
  formatLocalDay,
  formatShortDay,
  isValidRange,
  matchPreset,
  parseDay,
  presetRange,
  rangeFromParams,
  rangeLengthDays
} from './stats-range';

const NOW = new Date('2026-11-06T10:30:00Z');

describe('stats-range', () => {
  describe('parseDay', () => {
    it('parses valid days as UTC midnight', () => {
      expect(parseDay('2026-02-28')?.toISOString()).toBe('2026-02-28T00:00:00.000Z');
    });

    it('rejects malformed, nonexistent and empty values', () => {
      expect(parseDay('2026-02-30')).toBeNull();
      expect(parseDay('2026-13-01')).toBeNull();
      expect(parseDay('2026-1-1')).toBeNull();
      expect(parseDay('2026-01-01T00:00:00Z')).toBeNull();
      expect(parseDay('')).toBeNull();
      expect(parseDay(null)).toBeNull();
      expect(parseDay('<script>')).toBeNull();
    });
  });

  describe('presetRange', () => {
    it('ends today (UTC) and includes today in the length', () => {
      expect(presetRange('7d', NOW)).toEqual({ from: '2026-10-31', to: '2026-11-06' });
      expect(presetRange('30d', NOW)).toEqual({ from: '2026-10-08', to: '2026-11-06' });
      expect(presetRange('90d', NOW)).toEqual({ from: '2026-08-09', to: '2026-11-06' });
    });

    it('covers 12 months without exceeding the API limit, also across a leap day', () => {
      expect(presetRange('12m', NOW)).toEqual({ from: '2025-11-07', to: '2026-11-06' });
      const leap = presetRange('12m', new Date('2028-12-31T23:00:00Z'));
      expect(rangeLengthDays(leap)).toBeLessThanOrEqual(MAX_RANGE_DAYS);
      expect(rangeLengthDays(leap)).toBeGreaterThanOrEqual(365);
    });

    it('uses the UTC day, not the local one', () => {
      expect(presetRange('7d', new Date('2026-11-06T23:59:59Z')).to).toBe('2026-11-06');
    });

    it('has the expected lengths', () => {
      expect(rangeLengthDays(presetRange('7d', NOW))).toBe(7);
      expect(rangeLengthDays(presetRange('30d', NOW))).toBe(30);
      expect(rangeLengthDays(presetRange('90d', NOW))).toBe(90);
    });
  });

  describe('matchPreset', () => {
    it('recognizes preset ranges and returns null for a custom one', () => {
      expect(matchPreset(presetRange('90d', NOW), NOW)).toBe('90d');
      expect(matchPreset({ from: '2026-10-01', to: '2026-10-05' }, NOW)).toBeNull();
    });
  });

  describe('rangeFromParams', () => {
    it('accepts a valid range', () => {
      expect(rangeFromParams('2026-10-01', '2026-10-31', NOW)).toEqual({ from: '2026-10-01', to: '2026-10-31' });
    });

    it('accepts a single day and exactly 366 days', () => {
      expect(rangeFromParams('2026-10-01', '2026-10-01', NOW)).toEqual({ from: '2026-10-01', to: '2026-10-01' });
      expect(rangeFromParams('2025-11-06', '2026-11-06', NOW).from).toBe('2025-11-06');
    });

    it('falls back to the default for missing, inverted, too long and malformed ranges', () => {
      const fallback = defaultRange(NOW);
      expect(rangeFromParams(null, null, NOW)).toEqual(fallback);
      expect(rangeFromParams('2026-10-01', null, NOW)).toEqual(fallback);
      expect(rangeFromParams('2026-10-31', '2026-10-01', NOW)).toEqual(fallback);
      expect(rangeFromParams('2025-11-05', '2026-11-06', NOW)).toEqual(fallback);
      expect(rangeFromParams('abc', '2026-10-01', NOW)).toEqual(fallback);
      expect(rangeFromParams('2026-02-30', '2026-03-05', NOW)).toEqual(fallback);
    });
  });

  describe('isValidRange / rangeLengthDays', () => {
    it('counts both ends and rejects invalid days', () => {
      expect(rangeLengthDays({ from: '2026-01-01', to: '2026-01-01' })).toBe(1);
      expect(rangeLengthDays({ from: '2026-01-01', to: '2026-01-31' })).toBe(31);
      expect(rangeLengthDays({ from: 'x', to: '2026-01-31' })).toBeNaN();
      expect(isValidRange({ from: '2026-02-01', to: '2026-01-01' })).toBe(false);
    });
  });

  describe('enumerateDays', () => {
    it('lists every day, across month and year boundaries', () => {
      expect(enumerateDays({ from: '2026-12-30', to: '2027-01-02' })).toEqual(['2026-12-30', '2026-12-31', '2027-01-01', '2027-01-02']);
    });

    it('returns an empty list for invalid or oversized ranges', () => {
      expect(enumerateDays({ from: '2026-02-01', to: '2026-01-01' })).toEqual([]);
      expect(enumerateDays({ from: '2020-01-01', to: '2026-01-01' })).toEqual([]);
    });
  });

  describe('formatting', () => {
    it('formats Polish short and full dates', () => {
      expect(formatShortDay('2026-11-05')).toBe('5 lis');
      expect(formatShortDay('2026-09-01')).toBe('1 wrz');
      expect(formatShortDay('2026-10-12')).toBe('12 paź');
      expect(formatFullDay('2026-11-05')).toBe('05.11.2026');
      expect(formatFullDay('oops')).toBe('oops');
    });

    it('converts between local dates and day strings without shifting the day', () => {
      const local = new Date(2026, 10, 5);
      expect(formatLocalDay(local)).toBe('2026-11-05');
      const back = dayToLocalDate('2026-11-05') as Date;
      expect([back.getFullYear(), back.getMonth(), back.getDate()]).toEqual([2026, 10, 5]);
      expect(dayToLocalDate('nope')).toBeNull();
    });
  });
});
