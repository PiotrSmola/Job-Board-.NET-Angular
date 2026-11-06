using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Mappings;
using Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

// Zarządzanie kategoriami ofert przez admina
[Route("api/admin/categories")]
[ApiController]
[Authorize(Roles = Roles.Admin)]
public class AdminCategoriesController : ControllerBase
{
    private readonly ICategoryRepository _categoryRepository;

    public AdminCategoriesController(ICategoryRepository categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    [HttpPost]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CategoryDto>> CreateCategory(CategoryFormDto categoryDto)
    {
        var name = TextNormalizer.Normalize(categoryDto.Name);
        if (name.Length == 0)
        {
            return BadRequest(new { message = CategoryMessages.NameEmpty });
        }

        if (await _categoryRepository.IsNameOrSlugTakenAsync(name, categoryDto.Slug))
        {
            return BadRequest(new { message = CategoryMessages.NameOrSlugTaken });
        }

        var category = new Category { Name = name, Slug = categoryDto.Slug, SortOrder = categoryDto.SortOrder };

        try
        {
            await _categoryRepository.AddCategoryAsync(category);
        }
        catch (Exception ex) when (ex.IsDuplicateKey())
        {
            return BadRequest(new { message = CategoryMessages.NameOrSlugTaken });
        }

        var dto = (await _categoryRepository.GetSummaryAsync(category.Id))!.ToDto();
        return CreatedAtAction(nameof(CategoriesController.GetCategory), "Categories", new { id = category.Id }, dto);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CategoryDto>> UpdateCategory(int id, CategoryFormDto categoryDto)
    {
        var category = await _categoryRepository.GetCategoryForUpdateAsync(id);
        if (category == null)
        {
            return NotFound(new { message = CategoryMessages.NotFound });
        }

        var name = TextNormalizer.Normalize(categoryDto.Name);
        if (name.Length == 0)
        {
            return BadRequest(new { message = CategoryMessages.NameEmpty });
        }

        if (await _categoryRepository.IsNameOrSlugTakenAsync(name, categoryDto.Slug, id))
        {
            return BadRequest(new { message = CategoryMessages.NameOrSlugTaken });
        }

        category.Name = name;
        category.Slug = categoryDto.Slug;
        category.SortOrder = categoryDto.SortOrder;

        try
        {
            await _categoryRepository.UpdateCategoryAsync(category);
        }
        catch (Exception ex) when (ex.IsDuplicateKey())
        {
            return BadRequest(new { message = CategoryMessages.NameOrSlugTaken });
        }

        return Ok((await _categoryRepository.GetSummaryAsync(id))!.ToDto());
    }

    // Oferty usuniętej kategorii zostają, ale bez kategorii
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        return await _categoryRepository.DeleteCategoryAsync(id)
            ? NoContent()
            : NotFound(new { message = CategoryMessages.NotFound });
    }
}
