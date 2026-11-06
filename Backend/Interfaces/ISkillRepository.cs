using Backend.Models;

namespace Backend.Interfaces;

public interface ISkillRepository
{
    // Podpowiedzi: umiejętności użyte w ofertach widocznych publicznie, z nazwą zawierającą frazę (bez wielkości liter;
    // przed fragmentami pasującymi w środku idą prefiksy), od najpopularniejszych
    Task<IReadOnlyList<SkillSummary>> SuggestAsync(string? search, int limit, CancellationToken cancellationToken = default);
}
