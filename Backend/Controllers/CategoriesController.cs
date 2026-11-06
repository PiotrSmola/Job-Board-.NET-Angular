using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Mappings;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

// Publiczna lista kategorii ofert (zarządza nimi admin - AdminCategoriesController)
[Route("api/[controller]")]
[ApiController]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryRepository _categoryRepository;

    public CategoriesController(ICategoryRepository categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    // Wszystkie kategorie wg sortOrder i nazwy, z liczbą ofert widocznych publicznie (także 0)
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<CategoryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<CategoryDto>>> GetCategories(CancellationToken cancellationToken)
    {
        var categories = await _categoryRepository.GetAllAsync(cancellationToken);
        return Ok(categories.Select(c => c.ToDto()));
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryDto>> GetCategory(int id)
    {
        var category = await _categoryRepository.GetSummaryAsync(id);
        return category == null
            ? NotFound(new { message = CategoryMessages.NotFound })
            : Ok(category.ToDto());
    }
}
