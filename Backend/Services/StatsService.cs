using Backend.Data;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

// Agregacje statystyk liczone w SQL (GROUP BY po dniu, zliczenia warunkowe) - do pamięci trafiają wyłącznie wyniki
// zagregowane (jeden wiersz na dzień / status / ofertę z limitem), nigdy surowe aplikacje czy użytkownicy.
// Dni są liczone w UTC. Sumy "totals" to stan bieżący / od początku, a seria, lejek, mediana i tabele - zakres.
public class StatsService : IStatsService
{
    private const int TopJobsLimit = 10;
    // pracodawca widzi wszystkie oferty firmy, ale odpowiedź ma górną granicę
    private const int EmployerJobsLimit = 500;

    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public StatsService(ApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<AdminStatsDto> GetAdminStatsAsync(StatsRange range, CancellationToken cancellationToken)
    {
        var totals = new AdminTotalsDto(
            await GetUserTotalsAsync(cancellationToken),
            await GetCompanyTotalsAsync(cancellationToken),
            await GetJobCountsAsync(null, cancellationToken),
            await GetApplicationTotalsAsync(null, cancellationToken));

        return new AdminStatsDto(
            ToDto(range),
            totals,
            await GetAdminSeriesAsync(range, cancellationToken),
            await GetFunnelAsync(null, range, cancellationToken),
            await GetTopJobsAsync(range, cancellationToken),
            await GetMedianFirstResponseHoursAsync(null, range, cancellationToken));
    }

    public async Task<EmployerStatsDto> GetEmployerStatsAsync(int companyId, StatsRange range, CancellationToken cancellationToken)
    {
        var totalViews = await _context.DailyViews
            .Where(v => v.JobOffer.CompanyId == companyId)
            .SumAsync(v => (long)v.Views, cancellationToken);

        var totals = new EmployerTotalsDto(
            await GetJobCountsAsync(companyId, cancellationToken),
            await GetApplicationTotalsAsync(companyId, cancellationToken),
            totalViews);

        return new EmployerStatsDto(
            ToDto(range),
            totals,
            await GetEmployerSeriesAsync(companyId, range, cancellationToken),
            await GetFunnelAsync(companyId, range, cancellationToken),
            await GetEmployerJobsAsync(companyId, range, cancellationToken),
            await GetMedianFirstResponseHoursAsync(companyId, range, cancellationToken));
    }

    public async Task<IReadOnlyList<AdminSeriesPointDto>> GetAdminSeriesAsync(StatsRange range, CancellationToken cancellationToken)
    {
        var applications = await CountPerDayAsync(_context.JobApplications.Select(a => a.AppliedAt), range, cancellationToken);
        var jobs = await CountPerDayAsync(_context.JobOffers.Select(j => j.CreatedAt), range, cancellationToken);
        var users = await CountPerDayAsync(_context.Users.Select(u => u.CreatedAt), range, cancellationToken);
        var views = await ViewsPerDayAsync(null, range, cancellationToken);

        // brakujące dni uzupełniane zerami po stronie serwera
        return range.EachDay()
            .Select(day => new AdminSeriesPointDto(
                day,
                applications.GetValueOrDefault(day),
                jobs.GetValueOrDefault(day),
                users.GetValueOrDefault(day),
                views.GetValueOrDefault(day)))
            .ToList();
    }

    public async Task<IReadOnlyList<EmployerSeriesPointDto>> GetEmployerSeriesAsync(int companyId, StatsRange range, CancellationToken cancellationToken)
    {
        var applications = await CountPerDayAsync(
            _context.JobApplications.Where(a => a.JobOffer.CompanyId == companyId).Select(a => a.AppliedAt), range, cancellationToken);
        var views = await ViewsPerDayAsync(companyId, range, cancellationToken);

        return range.EachDay()
            .Select(day => new EmployerSeriesPointDto(day, applications.GetValueOrDefault(day), views.GetValueOrDefault(day)))
            .ToList();
    }

    private static StatsRangeDto ToDto(StatsRange range) => new(range.From, range.To);

    // Liczba zdarzeń na dzień UTC w zakresie (jedno zapytanie GROUP BY po dacie)
    private static async Task<Dictionary<DateOnly, int>> CountPerDayAsync(
        IQueryable<DateTime> timestamps, StatsRange range, CancellationToken cancellationToken)
    {
        var start = range.StartUtc;
        var end = range.EndExclusiveUtc;

        var rows = await timestamps
            .Where(t => t >= start && t < end)
            .GroupBy(t => t.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => DateOnly.FromDateTime(r.Day), r => r.Count);
    }

    private async Task<Dictionary<DateOnly, int>> ViewsPerDayAsync(int? companyId, StatsRange range, CancellationToken cancellationToken)
    {
        var from = range.From;
        var to = range.To;

        var rows = await _context.DailyViews
            .Where(v => v.Date >= from && v.Date <= to && (companyId == null || v.JobOffer.CompanyId == companyId))
            .GroupBy(v => v.Date)
            .Select(g => new { Day = g.Key, Views = g.Sum(v => v.Views) })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Day, r => r.Views);
    }

    private async Task<UserTotalsDto> GetUserTotalsAsync(CancellationToken cancellationToken)
    {
        var rows = await (
                from userRole in _context.UserRoles
                join role in _context.Roles on userRole.RoleId equals role.Id
                group role by role.Name into g
                select new { Role = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int CountOf(string role) => rows.Where(r => r.Role == role).Select(r => r.Count).FirstOrDefault();
        return new UserTotalsDto(CountOf(Roles.User), CountOf(Roles.Employer), CountOf(Roles.Admin));
    }

    private async Task<CompanyTotalsDto> GetCompanyTotalsAsync(CancellationToken cancellationToken)
    {
        var rows = await _context.Companies
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int CountOf(CompanyStatus status) => rows.Where(r => r.Status == status).Select(r => r.Count).FirstOrDefault();
        return new CompanyTotalsDto(CountOf(CompanyStatus.Pending), CountOf(CompanyStatus.Approved), CountOf(CompanyStatus.Rejected));
    }

    // Stany ofert: szkic / aktywna (niewygasła) / wygasła (aktywna po terminie) / zamknięta - jedno zapytanie
    private async Task<JobStatusCountsDto> GetJobCountsAsync(int? companyId, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var counts = await _context.JobOffers
            .Where(j => companyId == null || j.CompanyId == companyId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Draft = g.Count(j => j.Status == JobOfferStatus.Draft),
                Active = g.Count(j => j.Status == JobOfferStatus.Active && (j.ExpiresAt == null || j.ExpiresAt > now)),
                Expired = g.Count(j => j.Status == JobOfferStatus.Active && j.ExpiresAt != null && j.ExpiresAt <= now),
                Closed = g.Count(j => j.Status == JobOfferStatus.Closed)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return counts == null
            ? new JobStatusCountsDto(0, 0, 0, 0)
            : new JobStatusCountsDto(counts.Draft, counts.Active, counts.Expired, counts.Closed);
    }

    // Wszystkie statusy 0-6 (także z zerem), w kolejności enuma
    private async Task<ApplicationTotalsDto> GetApplicationTotalsAsync(int? companyId, CancellationToken cancellationToken)
    {
        var rows = await ApplicationsOf(companyId)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var byStatus = Enum.GetValues<ApplicationStatus>()
            .Select(status => new ApplicationStatusCountDto(status, rows.Where(r => r.Status == status).Select(r => r.Count).FirstOrDefault()))
            .ToList();

        return new ApplicationTotalsDto(byStatus.Sum(s => s.Count), byStatus);
    }

    // Lejek: aplikacje złożone w zakresie wg najwyższego osiągniętego etapu (InReview < Interview < Offered < Hired)
    // w historii statusów. Etapy są narastające (zatrudniony przeszedł też rozmowę), odrzucenie i wycofanie nie są etapami.
    // Jedno zapytanie: dla każdej aplikacji MAX(status) z etapów, grupowane po tej wartości.
    private async Task<StatsFunnelDto> GetFunnelAsync(int? companyId, StatsRange range, CancellationToken cancellationToken)
    {
        var rows = await ApplicationsIn(companyId, range)
            .Select(a => a.StatusHistory
                .Where(h => h.Status >= ApplicationStatus.InReview && h.Status <= ApplicationStatus.Hired)
                .Max(h => (int?)h.Status))
            .GroupBy(stage => stage)
            .Select(g => new { Stage = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int Reached(ApplicationStatus stage) => rows.Where(r => r.Stage >= (int)stage).Sum(r => r.Count);

        return new StatsFunnelDto(
            rows.Sum(r => r.Count),
            Reached(ApplicationStatus.InReview),
            Reached(ApplicationStatus.Interview),
            Reached(ApplicationStatus.Offered),
            Reached(ApplicationStatus.Hired));
    }

    // Mediana (w godzinach) czasu od złożenia aplikacji z zakresu do pierwszej zmiany statusu przez kogoś innego niż
    // kandydat (wpis historii z innym autorem niż kandydat i statusem różnym od Submitted - sama notatka nie liczy się).
    // Różnice w sekundach liczy SQL; mediana pobiera tylko 1-2 środkowe wartości z posortowanego zbioru
    // (COUNT + ORDER BY ... LIMIT/OFFSET), więc do pamięci nie trafia cały zbiór.
    private async Task<double?> GetMedianFirstResponseHoursAsync(int? companyId, StatsRange range, CancellationToken cancellationToken)
    {
        var seconds = ApplicationsIn(companyId, range)
            .Select(a => new
            {
                a.AppliedAt,
                FirstResponse = a.StatusHistory
                    .Where(h => h.ChangedByUserId != null && h.ChangedByUserId != a.UserId && h.Status != ApplicationStatus.Submitted)
                    .Min(h => (DateTime?)h.ChangedAt)
            })
            .Where(x => x.FirstResponse != null)
            .Select(x => EF.Functions.DateDiffSecond(x.AppliedAt, x.FirstResponse!.Value));

        var count = await seconds.CountAsync(cancellationToken);
        if (count == 0)
        {
            return null;
        }

        var middle = await seconds
            .OrderBy(s => s)
            .Skip((count - 1) / 2)
            .Take(count % 2 == 0 ? 2 : 1)
            .ToListAsync(cancellationToken);

        var medianSeconds = Math.Max(0, middle.Average());
        return Math.Round(medianSeconds / 3600, 2);
    }

    // Top ofert zakresu po liczbie aplikacji (przy remisie - po wyświetleniach), tylko oferty z jakąkolwiek aktywnością
    private async Task<IReadOnlyList<AdminTopJobDto>> GetTopJobsAsync(StatsRange range, CancellationToken cancellationToken)
    {
        var (start, end, from, to) = (range.StartUtc, range.EndExclusiveUtc, range.From, range.To);

        return await _context.JobOffers
            .Select(j => new
            {
                j.Id,
                j.Title,
                Company = j.Company.Name,
                Applications = j.Applications.Count(a => a.AppliedAt >= start && a.AppliedAt < end),
                Views = j.DailyViews.Where(v => v.Date >= from && v.Date <= to).Sum(v => (int?)v.Views) ?? 0
            })
            .Where(j => j.Applications > 0 || j.Views > 0)
            .OrderByDescending(j => j.Applications)
            .ThenByDescending(j => j.Views)
            .ThenBy(j => j.Id)
            .Take(TopJobsLimit)
            .Select(j => new AdminTopJobDto(j.Id, j.Title, j.Company, j.Applications, j.Views))
            .ToListAsync(cancellationToken);
    }

    // Oferty firmy z aplikacjami i wyświetleniami w zakresie; konwersja = aplikacje / wyświetlenia (0 bez wyświetleń)
    private async Task<IReadOnlyList<EmployerJobStatDto>> GetEmployerJobsAsync(int companyId, StatsRange range, CancellationToken cancellationToken)
    {
        var (start, end, from, to) = (range.StartUtc, range.EndExclusiveUtc, range.From, range.To);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var rows = await _context.JobOffers
            .Where(j => j.CompanyId == companyId)
            .Select(j => new
            {
                j.Id,
                j.Title,
                j.Status,
                j.ExpiresAt,
                Applications = j.Applications.Count(a => a.AppliedAt >= start && a.AppliedAt < end),
                Views = j.DailyViews.Where(v => v.Date >= from && v.Date <= to).Sum(v => (int?)v.Views) ?? 0
            })
            .OrderByDescending(j => j.Applications)
            .ThenByDescending(j => j.Views)
            .ThenBy(j => j.Id)
            .Take(EmployerJobsLimit)
            .ToListAsync(cancellationToken);

        return rows
            .Select(j => new EmployerJobStatDto(
                j.Id,
                j.Title,
                j.Status,
                j.Status == JobOfferStatus.Active && j.ExpiresAt.HasValue && j.ExpiresAt.Value <= now,
                j.Views,
                j.Applications,
                j.Views > 0 ? Math.Round((double)j.Applications / j.Views, 4) : 0))
            .ToList();
    }

    // Aplikacje na oferty firmy (null = wszystkie)
    private IQueryable<JobApplication> ApplicationsOf(int? companyId) =>
        _context.JobApplications.Where(a => companyId == null || a.JobOffer.CompanyId == companyId);

    // Aplikacje złożone w zakresie dni
    private IQueryable<JobApplication> ApplicationsIn(int? companyId, StatsRange range)
    {
        var (start, end) = (range.StartUtc, range.EndExclusiveUtc);
        return ApplicationsOf(companyId).Where(a => a.AppliedAt >= start && a.AppliedAt < end);
    }
}
