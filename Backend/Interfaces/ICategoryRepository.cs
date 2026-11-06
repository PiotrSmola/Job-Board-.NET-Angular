using Backend.Models;

namespace Backend.Interfaces;

public interface ICategoryRepository
{
    // wszystkie kategorie wg sortOrder, nazwy; z licznikiem ofert widocznych publicznie
    Task<IReadOnlyList<CategorySummary>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<CategorySummary?> GetSummaryAsync(int id);
    // śledzona encja do edycji - zapis przez UpdateCategoryAsync
    Task<Category?> GetCategoryForUpdateAsync(int id);
    Task<bool> ExistsAsync(int id);
    // nazwa albo slug zajęte przez inną kategorię (kolacja kolumn: bez wielkości liter i akcentów)
    Task<bool> IsNameOrSlugTakenAsync(string name, string slug, int? exceptCategoryId = null);
    Task AddCategoryAsync(Category category);
    Task UpdateCategoryAsync(Category category);
    // oferty kategorii tracą ją (klucz obcy SET NULL); false = kategoria już nie istnieje
    Task<bool> DeleteCategoryAsync(int id);
}
