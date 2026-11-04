import { JobOfferStatus } from '../models/job-offer.model';
import { ApplicationStatus } from '../models/job-application.model';
import {
  APPLICATION_STATUS_PRESENTATION,
  RECRUITER_STATUS_OPTIONS,
  calendarDaysUntil,
  canWithdraw,
  formatExpiresSoon,
  getJobState,
  getJobStatePresentation
} from './status-labels';

describe('status-labels', () => {
  describe('getJobState', () => {
    it.each([
      [JobOfferStatus.Draft, false, 'draft', 'Szkic'],
      [JobOfferStatus.Active, false, 'active', 'Aktywna'],
      [JobOfferStatus.Active, true, 'expired', 'Wygasła'],
      [JobOfferStatus.Closed, false, 'closed', 'Zamknięta']
    ])('status %s with isExpired=%s is %s', (status, isExpired, state, label) => {
      expect(getJobState({ status, isExpired })).toBe(state);
      expect(getJobStatePresentation({ status, isExpired }).label).toBe(label);
    });
  });

  it('has a Polish label for every application status', () => {
    expect(Object.values(APPLICATION_STATUS_PRESENTATION).map(p => p.label)).toEqual([
      'Złożona', 'W analizie', 'Rozmowa', 'Oferta', 'Zatrudniony', 'Odrzucona', 'Wycofana'
    ]);
  });

  it('does not let the recruiter choose "Wycofana"', () => {
    expect(RECRUITER_STATUS_OPTIONS.map(o => o.value)).not.toContain(ApplicationStatus.Withdrawn);
    expect(RECRUITER_STATUS_OPTIONS.length).toBe(6);
  });

  it.each([
    [ApplicationStatus.Submitted, true],
    [ApplicationStatus.InReview, true],
    [ApplicationStatus.Interview, true],
    [ApplicationStatus.Offered, true],
    [ApplicationStatus.Hired, false],
    [ApplicationStatus.Rejected, false],
    [ApplicationStatus.Withdrawn, false]
  ])('canWithdraw(%s) is %s', (status, expected) => {
    expect(canWithdraw(status)).toBe(expected);
  });

  describe('formatExpiresSoon', () => {
    const now = new Date(2026, 10, 4, 12, 0, 0);
    const at = (days: number, hours = 12) => new Date(2026, 10, 4 + days, hours, 0, 0).toISOString();

    it.each([
      [at(0, 18), 'Wygasa dziś'],
      [at(1), 'Wygasa jutro'],
      [at(3), 'Wygasa za 3 dni'],
      [at(7), 'Wygasa za 7 dni']
    ])('%s -> %s', (expiresAt, expected) => {
      expect(formatExpiresSoon(expiresAt, now)).toBe(expected);
    });

    it.each([[null], [at(8)], [at(0, 10)], [at(-2)]])('returns null for %s', expiresAt => {
      expect(formatExpiresSoon(expiresAt, now)).toBeNull();
    });

    it('counts calendar days, not 24-hour periods', () => {
      expect(calendarDaysUntil(new Date(2026, 10, 5, 1, 0, 0), new Date(2026, 10, 4, 23, 0, 0))).toBe(1);
    });
  });
});
