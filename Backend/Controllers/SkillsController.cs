using System.ComponentModel.DataAnnotations;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Controllers;

// Publiczne podpowiedzi umiejętności (autocomplete filtrów i formularza oferty)
[Route("api/[controller]")]
[ApiController]
public class SkillsController : ControllerBase
{
    private readonly ISkillRepository _skillRepository;

    public SkillsController(ISkillRepository skillRepository)
    {
        _skillRepository = skillRepository;
    }

    // Umiejętności z ofert widocznych publicznie, których nazwa zawiera frazę (prefiksy przed fragmentami),
    // od najpopularniejszych; limit 1-20 (domyślnie 10)
    [HttpGet]
    [EnableRateLimiting(RateLimitingSettings.PublicListsPolicy)]
    [ProducesResponseType(typeof(IEnumerable<SkillSuggestionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<SkillSuggestionDto>>> GetSkills(
        [FromQuery] SkillSuggestParams suggestParams, CancellationToken cancellationToken)
    {
        var skills = await _skillRepository.SuggestAsync(suggestParams.Search, suggestParams.Limit, cancellationToken);
        return Ok(skills.Select(s => new SkillSuggestionDto { Name = s.Name, JobsCount = s.JobsCount }));
    }
}

public class SkillSuggestParams
{
    public const int MaxLimit = 20;
    public const int DefaultLimit = 10;

    [MaxLength(SkillNames.MaxLength)]
    public string? Search { get; set; }

    [Range(1, MaxLimit)]
    public int Limit { get; set; } = DefaultLimit;
}
