using Backend.Data;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class SkillRepository : ISkillRepository
{
    private readonly ApplicationDbContext _context;

    public SkillRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<SkillSummary>> SuggestAsync(string? search, int limit, CancellationToken cancellationToken = default)
    {
        var skills = _context.JobOffers
            .AsNoTracking()
            .Where(JobOffer.ListedPubliclyAt(DateTime.UtcNow))
            .SelectMany(j => j.Skills);

        var term = string.IsNullOrWhiteSpace(search) ? null : SkillNames.KeyOf(SkillNames.Normalize(search));
        if (term != null)
        {
            // NormalizedName jest już zapisane małymi literami (kolacja binarna) - fraza porównywana tak samo
            skills = skills.Where(s => s.NormalizedName.Contains(term));
        }

        var counted = skills
            .GroupBy(s => new { s.Id, s.Name, s.NormalizedName })
            .Select(g => new { g.Key.Name, g.Key.NormalizedName, Count = g.Count() });

        var ordered = term == null
            ? counted.OrderByDescending(s => s.Count)
            : counted.OrderBy(s => s.NormalizedName.StartsWith(term) ? 0 : 1).ThenByDescending(s => s.Count);

        var result = await ordered
            .ThenBy(s => s.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return result.Select(s => new SkillSummary(s.Name, s.Count)).ToList();
    }
}
