using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Mappings;
using Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

// Profil firmy zalogowanego pracodawcy
[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = Roles.Employer)]
public class EmployerController : ControllerBase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly ICurrentUserAccess _access;

    public EmployerController(ICompanyRepository companyRepository, ICurrentUserAccess access)
    {
        _companyRepository = companyRepository;
        _access = access;
    }

    // Pełne dane własnej firmy (ze statusem weryfikacji i powodem odrzucenia)
    [HttpGet("company")]
    [ProducesResponseType(typeof(CompanyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CompanyDto>> GetCompany()
    {
        var companyId = await _access.GetEmployerCompanyIdAsync();
        var company = companyId.HasValue ? await _companyRepository.GetSummaryAsync(companyId.Value) : null;
        if (company == null)
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        return Ok(company.ToDto(includeReview: true));
    }

    // Edycja profilu; firma odrzucona wraca po edycji do weryfikacji (Pending, powód odrzucenia czyszczony)
    [HttpPut("company")]
    [ProducesResponseType(typeof(CompanyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CompanyDto>> UpdateCompany(CompanyFormDto companyDto)
    {
        var companyId = await _access.GetEmployerCompanyIdAsync();
        var company = companyId.HasValue ? await _companyRepository.GetCompanyForUpdateAsync(companyId.Value) : null;
        if (company == null)
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        if (await _companyRepository.IsNameTakenAsync(CompanyNames.Normalize(companyDto.Name), company.Id))
        {
            return BadRequest(new { message = CompanyMessages.NameTaken });
        }

        companyDto.ApplyTo(company);
        if (company.Status == CompanyStatus.Rejected)
        {
            company.Resubmit();
        }

        try
        {
            await _companyRepository.UpdateCompanyAsync(company);
        }
        catch (Exception ex) when (ex.IsDuplicateKey())
        {
            // równoległe żądanie zajęło nazwę (unikalny indeks)
            return BadRequest(new { message = CompanyMessages.NameTaken });
        }

        var summary = await _companyRepository.GetSummaryAsync(company.Id);
        return Ok(summary!.ToDto(includeReview: true));
    }
}
