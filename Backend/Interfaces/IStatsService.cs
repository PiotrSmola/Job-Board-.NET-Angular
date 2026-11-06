using Backend.DTOs;
using Backend.Helpers;

namespace Backend.Interfaces;

// Statystyki panelu admina i pracodawcy; wszystkie zakresy dat to dni UTC (obie granice włącznie)
public interface IStatsService
{
    Task<AdminStatsDto> GetAdminStatsAsync(StatsRange range, CancellationToken cancellationToken);

    // Tylko dane ofert wskazanej firmy
    Task<EmployerStatsDto> GetEmployerStatsAsync(int companyId, StatsRange range, CancellationToken cancellationToken);

    // Same serie dzienne (eksport CSV) - bez pozostałych agregatów
    Task<IReadOnlyList<AdminSeriesPointDto>> GetAdminSeriesAsync(StatsRange range, CancellationToken cancellationToken);

    Task<IReadOnlyList<EmployerSeriesPointDto>> GetEmployerSeriesAsync(int companyId, StatsRange range, CancellationToken cancellationToken);
}
