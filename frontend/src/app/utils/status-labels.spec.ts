import { JobOfferStatus } from '../models/job-offer.model';
import { ApplicationStatus } from '../models/job-application.model';
import { JobState } from '../models/job-params.model';
import { CompanyStatus } from '../models/company.model';
import {
  APPLICATION_STATUS_FILTER_OPTIONS,
  APPLICATION_STATUS_PRESENTATION,
  COMPANY_STATUS_FILTER_OPTIONS,
  COMPANY_STATUS_PRESENTATION,
  getCompanyStatusPresentation,
  JOB_STATE_OPTIONS,
  JOB_STATE_PRESENTATION,
  JOB_STATUS_OPTIONS,
  RECRUITER_STATUS_OPTIONS,
  WITHDRAWABLE_STATUSES,
  calendarDaysUntil,
  canAddAttachments,
  canWithdraw,
  formatExpiresSoon,
  getApplicationStatusPresentation,
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

  it.each([
    [ApplicationStatus.Submitted, true],
    [ApplicationStatus.InReview, true],
    [ApplicationStatus.Interview, true],
    [ApplicationStatus.Offered, true],
    [ApplicationStatus.Hired, false],
    [ApplicationStatus.Rejected, false],
    [ApplicationStatus.Withdrawn, false]
  ])('canAddAttachments(%s) is %s', (status, expected) => {
    expect(canAddAttachments(status)).toBe(expected);
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

    it('returns a negative number of days for past dates', () => {
      expect(calendarDaysUntil(new Date(2026, 10, 1, 12, 0, 0), now)).toBe(-3);
    });

    it('counts days across a month boundary', () => {
      expect(calendarDaysUntil(new Date(2026, 11, 2, 12, 0, 0), new Date(2026, 10, 28, 12, 0, 0))).toBe(4);
    });

    it('accepts ISO strings as well as Date objects', () => {
      expect(calendarDaysUntil(at(2), now)).toBe(2);
      expect(calendarDaysUntil(new Date(at(2)), now)).toBe(2);
    });

    it('respects a custom "within days" window', () => {
      expect(formatExpiresSoon(at(10), now, 14)).toBe('Wygasa za 10 dni');
      expect(formatExpiresSoon(at(10), now, 7)).toBeNull();
    });

    it('treats the boundary of exactly 7 days as inside and 8 days as outside the default window', () => {
      expect(formatExpiresSoon(at(7), now)).not.toBeNull();
      expect(formatExpiresSoon(at(8), now)).toBeNull();
    });

    it('returns null for an expiration at exactly the current moment', () => {
      expect(formatExpiresSoon(now.toISOString(), now)).toBeNull();
    });
  });

  describe('completeness of the enum maps', () => {
    const applicationStatuses = Object.values(ApplicationStatus).filter((v): v is ApplicationStatus => typeof v === 'number');
    const jobStates: JobState[] = ['draft', 'active', 'expired', 'closed'];

    it('covers all 7 application statuses', () => {
      expect(applicationStatuses.length).toBe(7);
    });

    it.each(applicationStatuses)('has a label, a tone and a presentation for application status %s', status => {
      const presentation = APPLICATION_STATUS_PRESENTATION[status];

      expect(presentation).toBeDefined();
      expect(presentation.label.trim()).not.toBe('');
      expect(presentation.tone).toBeTruthy();
      expect(getApplicationStatusPresentation(status)).toEqual(presentation);
      expect(getApplicationStatusPresentation(status).label).not.toBe('Nieznany');
    });

    it('uses a unique label for every application status', () => {
      const labels = applicationStatuses.map(s => APPLICATION_STATUS_PRESENTATION[s].label);
      expect(new Set(labels).size).toBe(labels.length);
    });

    it('maps every application status to the expected Polish label and tone', () => {
      expect(applicationStatuses.map(s => [APPLICATION_STATUS_PRESENTATION[s].label, APPLICATION_STATUS_PRESENTATION[s].tone])).toEqual([
        ['Złożona', 'info'],
        ['W analizie', 'warning'],
        ['Rozmowa', 'accent'],
        ['Oferta', 'positive'],
        ['Zatrudniony', 'success'],
        ['Odrzucona', 'danger'],
        ['Wycofana', 'muted']
      ]);
    });

    it('falls back to a neutral "Nieznany" presentation for an unknown application status', () => {
      expect(getApplicationStatusPresentation(99 as ApplicationStatus)).toEqual({ label: 'Nieznany', tone: 'neutral' });
    });

    it.each(jobStates)('has a label and tone for job state %s', state => {
      expect(JOB_STATE_PRESENTATION[state].label.trim()).not.toBe('');
      expect(JOB_STATE_PRESENTATION[state].tone).toBeTruthy();
    });

    it('has a filter option for every job state plus "Wszystkie"', () => {
      expect(JOB_STATE_OPTIONS.map(o => o.value)).toEqual([null, 'draft', 'active', 'expired', 'closed']);
      expect(JOB_STATE_OPTIONS[0].label).toBe('Wszystkie');
      expect(JOB_STATE_OPTIONS.every(o => o.label.trim() !== '')).toBe(true);
    });

    it('lets the form choose only draft, active and closed (expired is derived from the date)', () => {
      expect(JOB_STATUS_OPTIONS).toEqual([
        { value: JobOfferStatus.Draft, label: 'Szkic' },
        { value: JobOfferStatus.Active, label: 'Aktywna' },
        { value: JobOfferStatus.Closed, label: 'Zamknięta' }
      ]);
    });

    it('has a filter option for every application status plus "Wszystkie"', () => {
      expect(APPLICATION_STATUS_FILTER_OPTIONS.map(o => o.value)).toEqual([null, ...applicationStatuses]);
      expect(APPLICATION_STATUS_FILTER_OPTIONS[0].label).toBe('Wszystkie');
      expect(APPLICATION_STATUS_FILTER_OPTIONS.slice(1).map(o => o.label))
        .toEqual(applicationStatuses.map(s => APPLICATION_STATUS_PRESENTATION[s].label));
    });

    it('offers the recruiter every status except Withdrawn with the same labels as the presentation map', () => {
      expect(RECRUITER_STATUS_OPTIONS.map(o => o.value)).toEqual(applicationStatuses.filter(s => s !== ApplicationStatus.Withdrawn));
      RECRUITER_STATUS_OPTIONS.forEach(o => expect(o.label).toBe(APPLICATION_STATUS_PRESENTATION[o.value].label));
    });

    it('allows withdrawing only from the statuses the API accepts', () => {
      expect([...WITHDRAWABLE_STATUSES]).toEqual([
        ApplicationStatus.Submitted, ApplicationStatus.InReview, ApplicationStatus.Interview, ApplicationStatus.Offered
      ]);
      expect(applicationStatuses.filter(canWithdraw)).toEqual([...WITHDRAWABLE_STATUSES]);
    });
  });

  describe('getJobState for unusual input', () => {
    it('treats a draft as a draft even when the date has passed', () => {
      expect(getJobState({ status: JobOfferStatus.Draft, isExpired: true })).toBe('draft');
    });

    it('treats a closed offer as closed even when the date has passed', () => {
      expect(getJobState({ status: JobOfferStatus.Closed, isExpired: true })).toBe('closed');
    });

    it('falls back to active/expired for an unknown status value', () => {
      expect(getJobState({ status: 99 as JobOfferStatus, isExpired: false })).toBe('active');
      expect(getJobState({ status: 99 as JobOfferStatus, isExpired: true })).toBe('expired');
    });
  });

  describe('company status', () => {
    it('maps every status to a Polish label and tone', () => {
      expect(COMPANY_STATUS_PRESENTATION).toEqual({
        [CompanyStatus.Pending]: { label: 'Oczekuje', tone: 'warning' },
        [CompanyStatus.Approved]: { label: 'Zatwierdzona', tone: 'success' },
        [CompanyStatus.Rejected]: { label: 'Odrzucona', tone: 'danger' }
      });
    });

    it('offers "Wszystkie" and every status in the filter', () => {
      expect(COMPANY_STATUS_FILTER_OPTIONS).toEqual([
        { value: null, label: 'Wszystkie' },
        { value: CompanyStatus.Pending, label: 'Oczekuje' },
        { value: CompanyStatus.Approved, label: 'Zatwierdzona' },
        { value: CompanyStatus.Rejected, label: 'Odrzucona' }
      ]);
    });

    it('falls back to a neutral "Nieznany" for an unknown status', () => {
      expect(getCompanyStatusPresentation(9 as CompanyStatus)).toEqual({ label: 'Nieznany', tone: 'neutral' });
    });
  });
});
