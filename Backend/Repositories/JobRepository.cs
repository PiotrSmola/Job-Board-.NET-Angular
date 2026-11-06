using Backend.Data;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class JobRepository : IJobRepository
{
    private readonly ApplicationDbContext _context;

    public JobRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<PagedList<JobOffer>> GetPublicJobsAsync(JobParams jobParams)
    {
        var query = _context.JobOffers
            .AsNoTracking()
            .Include(j => j.Company)
            .Where(JobOffer.ListedPubliclyAt(DateTime.UtcNow));

        return QueryJobsAsync(query, jobParams);
    }

    public Task<PagedList<JobOffer>> GetManagedJobsAsync(ManageJobParams jobParams, ManagementScope scope)
    {
        var now = DateTime.UtcNow;
        var query = _context.JobOffers.AsNoTracking().Include(j => j.Company).AsQueryable();

        // pracodawca widzi wyłącznie oferty swojej firmy (filtr companyId z żądania nie poszerza zakresu)
        if (scope.CompanyId.HasValue)
        {
            query = query.Where(j => j.CompanyId == scope.CompanyId.Value);
        }

        query = jobParams.State?.ToLowerInvariant() switch
        {
            ManageJobParams.Draft => query.Where(j => j.Status == JobOfferStatus.Draft),
            ManageJobParams.Active => query.Where(j => j.Status == JobOfferStatus.Active && (j.ExpiresAt == null || j.ExpiresAt > now)),
            ManageJobParams.Expired => query.Where(j => j.Status == JobOfferStatus.Active && j.ExpiresAt != null && j.ExpiresAt <= now),
            ManageJobParams.Closed => query.Where(j => j.Status == JobOfferStatus.Closed),
            _ => query
        };

        return QueryJobsAsync(query, jobParams);
    }

    // Wspólne filtry, sortowanie i stronicowanie listy ofert
    private static async Task<PagedList<JobOffer>> QueryJobsAsync(IQueryable<JobOffer> query, JobParams jobParams)
    {
        if (!string.IsNullOrWhiteSpace(jobParams.SearchTerm))
        {
            query = query.Where(j =>
                j.Title.ToLower().Contains(jobParams.SearchTerm.ToLower()) ||
                j.Company.Name.ToLower().Contains(jobParams.SearchTerm.ToLower()) ||
                j.Description.ToLower().Contains(jobParams.SearchTerm.ToLower())
            );
        }

        if (jobParams.CompanyId.HasValue)
        {
            query = query.Where(j => j.CompanyId == jobParams.CompanyId.Value);
        }

        if (jobParams.WorkType.HasValue)
        {
            query = query.Where(j => j.WorkType == jobParams.WorkType.Value);
        }

        if (!string.IsNullOrWhiteSpace(jobParams.Location))
        {
            query = query.Where(j => j.Location.ToLower().Contains(jobParams.Location.ToLower()));
        }

        if (jobParams.MinSalary.HasValue && jobParams.MaxSalary.HasValue)
        {
            query = query.Where(j =>
                (j.SalaryMin.HasValue && j.SalaryMin >= jobParams.MinSalary.Value) &&
                (j.SalaryMax.HasValue && j.SalaryMax <= jobParams.MaxSalary.Value)
            );
        }
        else if (jobParams.MinSalary.HasValue)
        {
            query = query.Where(j =>
                (j.SalaryMin.HasValue && j.SalaryMin >= jobParams.MinSalary.Value) ||
                (j.SalaryMax.HasValue && j.SalaryMax >= jobParams.MinSalary.Value)
            );
        }
        else if (jobParams.MaxSalary.HasValue)
        {
            query = query.Where(j =>
                (j.SalaryMin.HasValue && j.SalaryMin <= jobParams.MaxSalary.Value) ||
                (j.SalaryMax.HasValue && j.SalaryMax <= jobParams.MaxSalary.Value)
            );
        }

        // Id jako drugi klucz sortowania zapewnia stabilną kolejność między stronami
        query = jobParams.OrderBy switch
        {
            "oldest" => query.OrderBy(j => j.CreatedAt).ThenBy(j => j.Id),
            // wynagrodzenie: oba kierunki po tym samym kluczu (górna granica widełek, a gdy jej brak - dolna),
            // więc salaryLow jest dokładnym odwróceniem salaryHigh; oferty bez widełek zawsze na końcu
            "salaryHigh" => query
                .OrderBy(j => (j.SalaryMax ?? j.SalaryMin) == null)
                .ThenByDescending(j => j.SalaryMax ?? j.SalaryMin)
                .ThenByDescending(j => j.Id),
            "salaryLow" => query
                .OrderBy(j => (j.SalaryMax ?? j.SalaryMin) == null)
                .ThenBy(j => j.SalaryMax ?? j.SalaryMin)
                .ThenBy(j => j.Id),
            "titleAsc" => query.OrderBy(j => j.Title).ThenBy(j => j.Id),
            "titleDesc" => query.OrderByDescending(j => j.Title).ThenByDescending(j => j.Id),
            "companyAsc" => query.OrderBy(j => j.Company.Name).ThenBy(j => j.Id),
            "companyDesc" => query.OrderByDescending(j => j.Company.Name).ThenByDescending(j => j.Id),
            "locationAsc" => query.OrderBy(j => j.Location).ThenBy(j => j.Id),
            "locationDesc" => query.OrderByDescending(j => j.Location).ThenByDescending(j => j.Id),
            _ => query.OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id)
        };

        return await PagedList<JobOffer>.CreateAsync(query, jobParams.PageNumber, jobParams.PageSize);
    }

    public async Task<JobOffer?> GetJobByIdAsync(int id)
    {
        // bez śledzenia - przy aktualizacji UpdateJobAsync dołącza encję do kontekstu
        return await _context.JobOffers
            .AsNoTracking()
            .Include(j => j.Company)
            .FirstOrDefaultAsync(j => j.Id == id);
    }

    // Zwraca ofertę z załadowaną firmą (do DTO)
    public async Task<JobOffer> AddJobAsync(JobOffer jobOffer)
    {
        _context.JobOffers.Add(jobOffer);
        await _context.SaveChangesAsync();
        await _context.Entry(jobOffer).Reference(j => j.Company).LoadAsync();
        return jobOffer;
    }

    // Zwraca ofertę z załadowaną firmą (do DTO)
    public async Task<JobOffer> UpdateJobAsync(JobOffer jobOffer)
    {
        // o firmie decyduje CompanyId - nawigacja wczytana bez śledzenia nie jest dołączana do kontekstu
        // (inaczej EF próbowałby śledzić firmę i mógłby nadpisać CompanyId wartością z nieaktualnej nawigacji)
        jobOffer.Company = null!;
        _context.Entry(jobOffer).State = EntityState.Modified;
        await _context.SaveChangesAsync();
        await _context.Entry(jobOffer).Reference(j => j.Company).LoadAsync();
        return jobOffer;
    }

    public async Task<(bool Deleted, IReadOnlyList<string> FileKeys)> DeleteJobAsync(int id)
    {
        // Blokady przed odczytem kluczy plików (jak przy usuwaniu aplikacji): równoległy upload załączników czeka do
        // końca transakcji i trafia już na usuniętą aplikację, więc żaden plik nie zostanie osierocony. Najpierw wiersz
        // oferty (jak sam DELETE oferty) - równoległe aplikowanie czeka na nim przy kontroli klucza obcego, zanim
        // zablokuje cokolwiek w aplikacjach, więc kolejność blokad nie prowadzi do deadlocka.
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var matched = await _context.JobOffers
            .Where(j => j.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Title, j => j.Title));
        if (matched == 0)
        {
            return (false, []);
        }

        await _context.JobApplications
            .Where(a => a.JobOfferId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, a => a.Status));

        var fileKeys = await _context.ApplicationAttachments
            .Where(a => a.JobApplication.JobOfferId == id)
            .Select(a => a.FileKey)
            .ToListAsync();

        // aplikacje, ich historia i załączniki usuwane kaskadowo przez bazę
        await _context.JobOffers.Where(j => j.Id == id).ExecuteDeleteAsync();

        await transaction.CommitAsync();
        return (true, fileKeys);
    }

    public async Task<bool> DeleteJobWithoutApplicationsAsync(int id)
    {
        return await _context.JobOffers
            .Where(j => j.Id == id && !j.Applications.Any())
            .ExecuteDeleteAsync() > 0;
    }

    public async Task<bool> JobExistsAsync(int id)
    {
        return await _context.JobOffers.AnyAsync(j => j.Id == id);
    }
}
