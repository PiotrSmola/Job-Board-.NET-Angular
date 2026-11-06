using Backend.Data;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class JobRepository : IJobRepository
{
    // ile razy ponawiamy tworzenie umiejętności, gdy równoległe żądanie utworzyło te same nazwy
    private const int SkillCreationAttempts = 3;
    private const int FacetSkillsLimit = 20;

    private readonly ApplicationDbContext _context;

    public JobRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<PagedList<JobOffer>> GetPublicJobsAsync(JobParams jobParams)
    {
        var query = WithTaxonomy(_context.JobOffers.AsNoTracking())
            .Where(JobOffer.ListedPubliclyAt(DateTime.UtcNow));

        return QueryJobsAsync(query, jobParams);
    }

    public Task<PagedList<JobOffer>> GetManagedJobsAsync(ManageJobParams jobParams, ManagementScope scope)
    {
        var now = DateTime.UtcNow;
        var query = WithTaxonomy(_context.JobOffers.AsNoTracking());

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

    // Liczniki faset: każda faseta liczy oferty spełniające wszystkie filtry POZA swoim własnym - dzięki temu
    // użytkownik widzi, ile ofert dodałby kolejny wybór w tej samej grupie. Kilka zapytań agregujących w SQL
    // (bez ładowania ofert do pamięci); wartości z licznikiem 0 są pomijane.
    public async Task<JobFacets> GetPublicFacetsAsync(JobParams jobParams, CancellationToken cancellationToken = default)
    {
        var filter = ParseFilter(jobParams);
        var published = _context.JobOffers.AsNoTracking().Where(JobOffer.ListedPubliclyAt(DateTime.UtcNow));

        var total = await ApplyFilter(published, filter).CountAsync(cancellationToken);

        var categories = await ApplyFilter(published, filter with { CategorySlug = null, CategoryId = null })
            .Where(j => j.CategoryId != null)
            .GroupBy(j => new { j.Category!.Id, j.Category.Slug, j.Category.Name })
            .Select(g => new { g.Key.Id, g.Key.Slug, g.Key.Name, Count = g.Count() })
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var seniority = await ApplyFilter(published, filter with { Seniority = [] })
            .Where(j => j.Seniority != null)
            .GroupBy(j => j.Seniority)
            .Select(g => new { Value = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var workTypes = await ApplyFilter(published, filter with { WorkTypes = [] })
            .GroupBy(j => j.WorkType)
            .Select(g => new { Value = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        // jedno zapytanie z licznikiem warunkowym na każdy bit maski
        var contracts = await ApplyFilter(published, filter with { ContractTypes = ContractTypes.None })
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Employment = g.Count(j => (j.ContractTypes & ContractTypes.EmploymentContract) != 0),
                B2B = g.Count(j => (j.ContractTypes & ContractTypes.B2B) != 0),
                Mandate = g.Count(j => (j.ContractTypes & ContractTypes.MandateContract) != 0),
                SpecificTask = g.Count(j => (j.ContractTypes & ContractTypes.SpecificTaskContract) != 0),
                Internship = g.Count(j => (j.ContractTypes & ContractTypes.Internship) != 0)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var skills = await ApplyFilter(published, filter with { SkillKeys = [] })
            .SelectMany(j => j.Skills)
            .GroupBy(s => new { s.Id, s.Name })
            .Select(g => new { g.Key.Name, Count = g.Count() })
            .OrderByDescending(s => s.Count)
            .ThenBy(s => s.Name)
            .Take(FacetSkillsLimit)
            .ToListAsync(cancellationToken);

        var contractCounts = new[]
        {
            new FacetCount((int)ContractTypes.EmploymentContract, contracts?.Employment ?? 0),
            new FacetCount((int)ContractTypes.B2B, contracts?.B2B ?? 0),
            new FacetCount((int)ContractTypes.MandateContract, contracts?.Mandate ?? 0),
            new FacetCount((int)ContractTypes.SpecificTaskContract, contracts?.SpecificTask ?? 0),
            new FacetCount((int)ContractTypes.Internship, contracts?.Internship ?? 0)
        };

        return new JobFacets
        {
            Total = total,
            Categories = categories.Select(c => new FacetCategory(c.Id, c.Slug, c.Name, c.Count)).ToList(),
            Seniority = seniority.OrderBy(s => s.Value).Select(s => new FacetCount((int)s.Value!.Value, s.Count)).ToList(),
            ContractTypes = contractCounts.Where(c => c.Count > 0).ToList(),
            WorkTypes = workTypes.OrderBy(w => w.Value).Select(w => new FacetCount((int)w.Value, w.Count)).ToList(),
            Skills = skills.Select(s => new SkillSummary(s.Name, s.Count)).ToList()
        };
    }

    // Kategoria i umiejętności potrzebne do DTO listy (jedno zapytanie, bez N+1)
    private static IQueryable<JobOffer> WithTaxonomy(IQueryable<JobOffer> query) =>
        query.Include(j => j.Company).Include(j => j.Category).Include(j => j.Skills);

    private static JobFilter ParseFilter(JobParams jobParams)
    {
        var (filter, errors) = JobFilterParser.Parse(jobParams);
        // kontrolery uruchamiają tę samą walidację przed repozytorium (400) - błąd tutaj to błąd programisty
        return filter ?? throw new ArgumentException(string.Join("; ", errors.Select(e => e.ErrorMessage)), nameof(jobParams));
    }

    // Wspólne filtry, sortowanie i stronicowanie listy ofert
    private static async Task<PagedList<JobOffer>> QueryJobsAsync(IQueryable<JobOffer> query, JobParams jobParams)
    {
        query = ApplyFilter(query, ParseFilter(jobParams));

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

    // Wszystkie filtry listy w SQL (IQueryable) - ten sam kod dla listy i faset
    private static IQueryable<JobOffer> ApplyFilter(IQueryable<JobOffer> query, JobFilter filter)
    {
        if (filter.SearchTerm != null)
        {
            var term = filter.SearchTerm.ToLower();
            query = query.Where(j =>
                j.Title.ToLower().Contains(term) ||
                j.Company.Name.ToLower().Contains(term) ||
                j.Description.ToLower().Contains(term)
            );
        }

        if (filter.CompanyId.HasValue)
        {
            query = query.Where(j => j.CompanyId == filter.CompanyId.Value);
        }

        if (filter.WorkTypes.Count > 0)
        {
            var workTypes = filter.WorkTypes;
            query = query.Where(j => workTypes.Contains(j.WorkType));
        }

        if (filter.Location != null)
        {
            var location = filter.Location.ToLower();
            query = query.Where(j => j.Location.ToLower().Contains(location));
        }

        if (filter.CategorySlug != null)
        {
            var slug = filter.CategorySlug;
            query = query.Where(j => j.Category != null && j.Category.Slug == slug);
        }

        if (filter.CategoryId.HasValue)
        {
            query = query.Where(j => j.CategoryId == filter.CategoryId.Value);
        }

        if (filter.Seniority.Count > 0)
        {
            var levels = filter.Seniority;
            query = query.Where(j => j.Seniority != null && levels.Contains(j.Seniority.Value));
        }

        if (filter.ContractTypes != ContractTypes.None)
        {
            var mask = filter.ContractTypes;
            query = query.Where(j => (j.ContractTypes & mask) != 0);
        }

        if (filter.SkillKeys.Count > 0)
        {
            // oferta musi mieć wszystkie wskazane umiejętności: jedno podzapytanie COUNT zamiast EXISTS na każdą
            // (połączenie oferta-umiejętność jest unikalne, a klucze nie mają duplikatów)
            var keys = filter.SkillKeys;
            var required = keys.Count;
            query = query.Where(j => j.Skills.Count(s => keys.Contains(s.NormalizedName)) == required);
        }

        if (filter.Currency.HasValue)
        {
            var currency = filter.Currency.Value;
            query = query.Where(j => j.SalaryCurrency == currency);
        }

        if (filter.MinSalary.HasValue && filter.MaxSalary.HasValue)
        {
            query = query.Where(j =>
                (j.SalaryMin.HasValue && j.SalaryMin >= filter.MinSalary.Value) &&
                (j.SalaryMax.HasValue && j.SalaryMax <= filter.MaxSalary.Value)
            );
        }
        else if (filter.MinSalary.HasValue)
        {
            query = query.Where(j =>
                (j.SalaryMin.HasValue && j.SalaryMin >= filter.MinSalary.Value) ||
                (j.SalaryMax.HasValue && j.SalaryMax >= filter.MinSalary.Value)
            );
        }
        else if (filter.MaxSalary.HasValue)
        {
            query = query.Where(j =>
                (j.SalaryMin.HasValue && j.SalaryMin <= filter.MaxSalary.Value) ||
                (j.SalaryMax.HasValue && j.SalaryMax <= filter.MaxSalary.Value)
            );
        }

        return query;
    }

    public async Task<JobOffer?> GetJobByIdAsync(int id)
    {
        // bez śledzenia - przy aktualizacji UpdateJobAsync dołącza encję do kontekstu
        return await WithTaxonomy(_context.JobOffers.AsNoTracking())
            .FirstOrDefaultAsync(j => j.Id == id);
    }

    // Zwraca ofertę z załadowaną firmą, kategorią i umiejętnościami (do DTO)
    public async Task<JobOffer> AddJobAsync(JobOffer jobOffer, IReadOnlyList<string> skillNames)
    {
        // umiejętności tworzone (i zatwierdzane) przed ofertą, żeby wyścig o tę samą nazwę nie przerywał zapisu oferty
        jobOffer.Skills = await ResolveSkillsAsync(skillNames);

        _context.JobOffers.Add(jobOffer);
        await _context.SaveChangesAsync();
        await _context.Entry(jobOffer).Reference(j => j.Company).LoadAsync();
        await _context.Entry(jobOffer).Reference(j => j.Category).LoadAsync();
        return jobOffer;
    }

    // Zwraca ofertę z załadowaną firmą, kategorią i umiejętnościami (do DTO).
    // skillNames == null - umiejętności bez zmian (np. szybka zmiana statusu)
    public async Task<JobOffer> UpdateJobAsync(JobOffer jobOffer, IReadOnlyList<string>? skillNames = null)
    {
        var loadedSkills = jobOffer.Skills;

        // o firmie i kategorii decydują CompanyId i CategoryId - nawigacje wczytane bez śledzenia nie są dołączane
        // do kontekstu (inaczej EF próbowałby śledzić firmę / kategorię i mógłby nadpisać klucz obcy wartością
        // z nieaktualnej nawigacji); umiejętności zmieniane są jawnie poniżej
        jobOffer.Company = null!;
        jobOffer.Category = null;
        jobOffer.Skills = new List<Skill>();

        var desiredSkills = skillNames == null ? null : await ResolveSkillsAsync(skillNames);

        _context.Entry(jobOffer).State = EntityState.Modified;

        if (desiredSkills != null)
        {
            // dotychczasowe powiązania z bazy: usuwamy zbędne, dodajemy brakujące (zmiany tylko w tabeli łączącej)
            await _context.Entry(jobOffer).Collection(j => j.Skills).LoadAsync();
            var desiredIds = desiredSkills.Select(s => s.Id).ToHashSet();
            foreach (var skill in jobOffer.Skills.Where(s => !desiredIds.Contains(s.Id)).ToList())
            {
                jobOffer.Skills.Remove(skill);
            }

            var currentIds = jobOffer.Skills.Select(s => s.Id).ToHashSet();
            foreach (var skill in desiredSkills.Where(s => !currentIds.Contains(s.Id)))
            {
                jobOffer.Skills.Add(skill);
            }
        }

        await _context.SaveChangesAsync();
        await _context.Entry(jobOffer).Reference(j => j.Company).LoadAsync();
        await _context.Entry(jobOffer).Reference(j => j.Category).LoadAsync();

        if (desiredSkills == null)
        {
            // niezmienione umiejętności (tylko do mapowania na DTO - encja nie jest już zapisywana)
            jobOffer.Skills = loadedSkills;
        }

        return jobOffer;
    }

    // Śledzone encje umiejętności o podanych nazwach; brakujące są tworzone i zatwierdzane od razu.
    // Unikalny NormalizedName chroni przed duplikatami przy równoległych żądaniach: przegrany zapis wycofuje
    // swoje nowe wpisy i ponawia dopasowanie (zwycięzca jest już w bazie).
    private async Task<List<Skill>> ResolveSkillsAsync(IReadOnlyList<string> skillNames)
    {
        if (skillNames.Count == 0)
        {
            return [];
        }

        var displayNames = skillNames.ToDictionary(SkillNames.KeyOf);
        var keys = displayNames.Keys.ToList();

        for (var attempt = 1; ; attempt++)
        {
            var existing = await _context.Skills.Where(s => keys.Contains(s.NormalizedName)).ToListAsync();
            var existingKeys = existing.Select(s => s.NormalizedName).ToHashSet();
            var created = keys.Where(k => !existingKeys.Contains(k))
                .Select(k => new Skill { Name = displayNames[k], NormalizedName = k })
                .ToList();

            if (created.Count == 0)
            {
                return existing;
            }

            _context.Skills.AddRange(created);
            try
            {
                await _context.SaveChangesAsync();
                return existing.Concat(created).ToList();
            }
            catch (Exception ex) when (ex.IsDuplicateKey() && attempt < SkillCreationAttempts)
            {
                foreach (var skill in created)
                {
                    _context.Entry(skill).State = EntityState.Detached;
                }
            }
        }
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

        // aplikacje, ich historia i załączniki oraz powiązania z umiejętnościami usuwane kaskadowo przez bazę
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
