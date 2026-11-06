import { AdminStats, EmployerStats } from '../app/models/stats.model';
import { ApplicationStatus } from '../app/models/job-application.model';
import { JobOfferStatus } from '../app/models/job-offer.model';

export function adminStatsFixture(overrides: Partial<AdminStats> = {}): AdminStats {
  return {
    range: { from: '2026-11-01', to: '2026-11-03' },
    totals: {
      users: { candidates: 120, employers: 12, admins: 2 },
      companies: { pending: 1, approved: 9, rejected: 2 },
      jobs: { draft: 3, active: 20, expired: 4, closed: 5 },
      applications: {
        total: 340,
        byStatus: [
          { status: ApplicationStatus.Submitted, count: 100 },
          { status: ApplicationStatus.Hired, count: 5 },
          { status: ApplicationStatus.Withdrawn, count: 0 }
        ]
      }
    },
    series: [
      { date: '2026-11-01', newApplications: 4, newJobs: 1, newUsers: 2, jobViews: 30 },
      { date: '2026-11-02', newApplications: 0, newJobs: 0, newUsers: 1, jobViews: 12 },
      { date: '2026-11-03', newApplications: 7, newJobs: 2, newUsers: 0, jobViews: 55 }
    ],
    funnel: { submitted: 11, inReview: 8, interview: 4, offered: 2, hired: 1 },
    topJobs: [
      { jobId: 1, title: 'Programista .NET', company: 'Acme', applications: 6, views: 40 },
      { jobId: 2, title: 'Analityk', company: 'Żabka', applications: 5, views: 20 }
    ],
    medianFirstResponseHours: 5.4,
    ...overrides
  };
}

export function employerStatsFixture(overrides: Partial<EmployerStats> = {}): EmployerStats {
  return {
    range: { from: '2026-11-01', to: '2026-11-03' },
    totals: {
      jobs: { draft: 1, active: 2, expired: 1, closed: 0 },
      applications: { total: 30, byStatus: [{ status: ApplicationStatus.Interview, count: 4 }] },
      views: 410
    },
    series: [
      { date: '2026-11-01', newApplications: 2, jobViews: 10 },
      { date: '2026-11-02', newApplications: 1, jobViews: 0 },
      { date: '2026-11-03', newApplications: 3, jobViews: 22 }
    ],
    funnel: { submitted: 6, inReview: 5, interview: 3, offered: 1, hired: 0 },
    jobs: [
      { jobId: 10, title: 'Tester', status: JobOfferStatus.Active, isExpired: false, views: 50, applications: 5, conversionRate: 0.1 },
      { jobId: 11, title: 'Szkic', status: JobOfferStatus.Draft, isExpired: false, views: 0, applications: 0, conversionRate: 0 },
      { jobId: 12, title: 'Stara oferta', status: JobOfferStatus.Active, isExpired: true, views: 8, applications: 1, conversionRate: 0.125 }
    ],
    medianFirstResponseHours: null,
    ...overrides
  };
}
