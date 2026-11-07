using Backend.Data;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class CompanyRepository : ICompanyRepository
{
    private readonly ApplicationDbContext _context;

    public CompanyRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<PagedList<CompanySummary>> GetPublicCompaniesAsync(CompanyParams companyParams)
    {
        var now = DateTime.UtcNow;
        var published = JobOffer.PublishedAt(now);
        var query = Search(_context.Companies.AsNoTracking().Where(c => c.Status == CompanyStatus.Approved), companyParams.SearchTerm);

        // Id jako ostatni klucz sortowania zapewnia stabilną kolejność między stronami
        query = companyParams.OrderBy switch
        {
            CompanyParams.NameDesc => query.OrderByDescending(c => c.Name).ThenByDescending(c => c.Id),
            CompanyParams.JobsDesc => query
                .OrderByDescending(c => c.JobOffers.AsQueryable().Count(published))
                .ThenBy(c => c.Name)
                .ThenBy(c => c.Id),
            _ => query.OrderBy(c => c.Name).ThenBy(c => c.Id)
        };

        return PagedList<CompanySummary>.CreateAsync(Summaries(query, now), companyParams.PageNumber, companyParams.PageSize);
    }

    public Task<PagedList<CompanySummary>> GetCompaniesAsync(AdminCompanyParams companyParams)
    {
        var query = Search(_context.Companies.AsNoTracking(), companyParams.SearchTerm);

        if (companyParams.Status.HasValue)
        {
            query = query.Where(c => c.Status == companyParams.Status.Value);
        }

        query = companyParams.OrderBy switch
        {
            AdminCompanyParams.Oldest => query.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id),
            CompanyParams.NameAsc => query.OrderBy(c => c.Name).ThenBy(c => c.Id),
            CompanyParams.NameDesc => query.OrderByDescending(c => c.Name).ThenByDescending(c => c.Id),
            _ => query.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
        };

        return PagedList<CompanySummary>.CreateAsync(Summaries(query, DateTime.UtcNow), companyParams.PageNumber, companyParams.PageSize);
    }

    public async Task<CompanySummary?> GetSummaryAsync(int id)
    {
        return await Summaries(_context.Companies.AsNoTracking().Where(c => c.Id == id), DateTime.UtcNow)
            .FirstOrDefaultAsync();
    }

    public async Task<Company?> GetCompanyForUpdateAsync(int id)
    {
        return await _context.Companies.FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<CompanyStatus?> GetStatusAsync(int id)
    {
        return await _context.Companies
            .Where(c => c.Id == id)
            .Select(c => (CompanyStatus?)c.Status)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> IsNameTakenAsync(string name, int? exceptCompanyId = null)
    {
        // ToLower jawnie - porównanie nie zależy od kolacji kolumny (unikalny indeks i tak chroni przed wyścigiem)
        var lowered = name.ToLower();
        return await _context.Companies.AnyAsync(c =>
            c.Name.ToLower() == lowered && (!exceptCompanyId.HasValue || c.Id != exceptCompanyId.Value));
    }

    public async Task<bool> HasJobsOrMembersAsync(int id)
    {
        return await _context.JobOffers.AnyAsync(j => j.CompanyId == id) ||
               await _context.Users.AnyAsync(u => u.CompanyId == id);
    }

    public async Task AddCompanyAsync(Company company)
    {
        _context.Companies.Add(company);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateCompanyAsync(Company company)
    {
        await _context.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<string>> DeleteCompanyAsync(Company company)
    {
        // Blokada wiersza firmy przed odczytem kluczy plików: upload zdjęć / tła zapisuje rekordy pod tą samą blokadą,
        // więc odczytane klucze obejmują pliki zatwierdzone tuż przed usunięciem, a późniejszy upload nie znajdzie firmy
        await using var transaction = await _context.Database.BeginTransactionAsync();

        await LockCompanyAsync(_context, company.Id);
        var fileKeys = await CompanyFileKeysAsync(_context, company.Id);

        // zdjęcia usuwa kaskada w bazie
        _context.Companies.Remove(company);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return fileKeys;
    }

    // UPDATE bez zmiany wartości tylko blokuje wiersz firmy (wynik = liczba dopasowanych wierszy)
    internal static Task<int> LockCompanyAsync(ApplicationDbContext context, int companyId,
        CancellationToken cancellationToken = default) =>
        context.Companies
            .Where(c => c.Id == companyId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, c => c.Status), cancellationToken);

    // Klucze plików firmy w magazynie (zdjęcia i tło ofert) - do usunięcia po zatwierdzonym usunięciu firmy
    internal static async Task<List<string>> CompanyFileKeysAsync(ApplicationDbContext context, int companyId)
    {
        var fileKeys = await context.CompanyPhotos
            .Where(p => p.CompanyId == companyId)
            .Select(p => p.FileKey)
            .ToListAsync();

        var backgroundKey = await context.Companies
            .Where(c => c.Id == companyId)
            .Select(c => c.OfferBackgroundKey)
            .FirstOrDefaultAsync();
        if (backgroundKey != null)
        {
            fileKeys.Add(backgroundKey);
        }

        return fileKeys;
    }

    // Filtr po nazwie i lokalizacji (bez rozróżniania wielkości liter)
    private static IQueryable<Company> Search(IQueryable<Company> query, string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return query;
        }

        var term = searchTerm.Trim().ToLower();
        return query.Where(c =>
            c.Name.ToLower().Contains(term) ||
            (c.Location != null && c.Location.ToLower().Contains(term)));
    }

    // Projekcja z licznikami w jednym zapytaniu SQL (podzapytania COUNT).
    // Oferty firmy niezatwierdzonej nie są widoczne publicznie, więc licznik wynosi wtedy 0 (zgodnie z listą ofert)
    private static IQueryable<CompanySummary> Summaries(IQueryable<Company> query, DateTime now)
    {
        var published = JobOffer.PublishedAt(now);
        return query.Select(c => new CompanySummary
        {
            Company = c,
            ActiveJobsCount = c.Status == CompanyStatus.Approved ? c.JobOffers.AsQueryable().Count(published) : 0,
            MembersCount = c.Members.Count,
            PhotosCount = c.Photos.Count
        });
    }
}
