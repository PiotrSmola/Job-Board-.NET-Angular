namespace Backend.Models;

// Umiejętność z liczbą ofert widocznych publicznie (projekcja - nie jest encją)
public record SkillSummary(string Name, int JobsCount);
