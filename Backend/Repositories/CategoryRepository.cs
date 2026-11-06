using Backend.Data;
using Backend.Interfaces;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class CategoryRepository : ICategoryRepository
{
    private readonly ApplicationDbContext _context;

    public CategoryRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<CategorySummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await Summaries(_context.Categories.AsNoTracking(), DateTime.UtcNow)
            .OrderBy(s => s.Category.SortOrder)
            .ThenBy(s => s.Category.Name)
            .ThenBy(s => s.Category.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<CategorySummary?> GetSummaryAsync(int id)
    {
        return await Summaries(_context.Categories.AsNoTracking().Where(c => c.Id == id), DateTime.UtcNow)
            .FirstOrDefaultAsync();
    }

    public async Task<Category?> GetCategoryForUpdateAsync(int id)
    {
        return await _context.Categories.FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<bool> ExistsAsync(int id)
    {
        return await _context.Categories.AnyAsync(c => c.Id == id);
    }

    public async Task<bool> IsNameOrSlugTakenAsync(string name, string slug, int? exceptCategoryId = null)
    {
        // porównanie w kolacji kolumn (utf8mb4_0900_ai_ci) - tak samo jak unikalne indeksy
        return await _context.Categories.AnyAsync(c =>
            (c.Name == name || c.Slug == slug) && (!exceptCategoryId.HasValue || c.Id != exceptCategoryId.Value));
    }

    public async Task AddCategoryAsync(Category category)
    {
        _context.Categories.Add(category);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateCategoryAsync(Category category)
    {
        await _context.SaveChangesAsync();
    }

    public async Task<bool> DeleteCategoryAsync(int id)
    {
        // oferty tracą kategorię po stronie bazy (ON DELETE SET NULL) - bez ładowania ich do pamięci
        return await _context.Categories.Where(c => c.Id == id).ExecuteDeleteAsync() > 0;
    }

    // Licznik ofert widocznych publicznie w jednym zapytaniu (podzapytanie COUNT) - zgodny z licznikami faset
    private static IQueryable<CategorySummary> Summaries(IQueryable<Category> query, DateTime now)
    {
        var listed = JobOffer.ListedPubliclyAt(now);
        return query.Select(c => new CategorySummary
        {
            Category = c,
            ActiveJobsCount = c.JobOffers.AsQueryable().Count(listed)
        });
    }
}
