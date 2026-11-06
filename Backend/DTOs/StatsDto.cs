using Backend.Models;

namespace Backend.DTOs;

// Odpowiedzi statystyk - kontrakt w docs/CONTINUE-stats-dashboard.md (JSON camelCase, enumy jako liczby,
// dni jako YYYY-MM-DD). Sumy "totals" to stan bieżący / od początku; seria, lejek i tabele dotyczą zakresu.

public record StatsRangeDto(DateOnly From, DateOnly To);

public record JobStatusCountsDto(int Draft, int Active, int Expired, int Closed);

public record ApplicationStatusCountDto(ApplicationStatus Status, int Count);

public record ApplicationTotalsDto(int Total, IReadOnlyList<ApplicationStatusCountDto> ByStatus);

// Aplikacje złożone w zakresie, które osiągnęły dany etap (narastająco wg historii statusów)
public record StatsFunnelDto(int Submitted, int InReview, int Interview, int Offered, int Hired);

public record UserTotalsDto(int Candidates, int Employers, int Admins);

public record CompanyTotalsDto(int Pending, int Approved, int Rejected);

public record AdminTotalsDto(UserTotalsDto Users, CompanyTotalsDto Companies, JobStatusCountsDto Jobs, ApplicationTotalsDto Applications);

public record AdminSeriesPointDto(DateOnly Date, int NewApplications, int NewJobs, int NewUsers, int JobViews);

public record AdminTopJobDto(int JobId, string Title, string Company, int Applications, int Views);

public record AdminStatsDto(
    StatsRangeDto Range,
    AdminTotalsDto Totals,
    IReadOnlyList<AdminSeriesPointDto> Series,
    StatsFunnelDto Funnel,
    IReadOnlyList<AdminTopJobDto> TopJobs,
    double? MedianFirstResponseHours);

public record EmployerTotalsDto(JobStatusCountsDto Jobs, ApplicationTotalsDto Applications, long Views);

public record EmployerSeriesPointDto(DateOnly Date, int NewApplications, int JobViews);

public record EmployerJobStatDto(
    int JobId,
    string Title,
    JobOfferStatus Status,
    bool IsExpired,
    int Views,
    int Applications,
    double ConversionRate);

public record EmployerStatsDto(
    StatsRangeDto Range,
    EmployerTotalsDto Totals,
    IReadOnlyList<EmployerSeriesPointDto> Series,
    StatsFunnelDto Funnel,
    IReadOnlyList<EmployerJobStatDto> Jobs,
    double? MedianFirstResponseHours);
