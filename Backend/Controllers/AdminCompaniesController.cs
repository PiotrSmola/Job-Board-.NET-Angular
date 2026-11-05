using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Mappings;
using Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

// Moderacja firm przez admina: weryfikacja zgłoszeń pracodawców, edycja, dodawanie i usuwanie firm
[Route("api/admin/companies")]
[ApiController]
[Authorize(Roles = Roles.Admin)]
public class AdminCompaniesController : ControllerBase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly ICurrentUserAccess _access;

    public AdminCompaniesController(ICompanyRepository companyRepository, ICurrentUserAccess access)
    {
        _companyRepository = companyRepository;
        _access = access;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<CompanyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<CompanyDto>>> GetCompanies([FromQuery] AdminCompanyParams companyParams)
    {
        var companies = await _companyRepository.GetCompaniesAsync(companyParams);

        Response.AddPaginationHeader(companies);

        return Ok(companies.Select(c => c.ToDto(includeReview: true, includeMembers: true)));
    }

    // Firma dodana przez admina jest od razu zatwierdzona
    [HttpPost]
    [ProducesResponseType(typeof(CompanyDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CompanyDto>> CreateCompany(CompanyFormDto companyDto)
    {
        if (await _companyRepository.IsNameTakenAsync(CompanyNames.Normalize(companyDto.Name)))
        {
            return BadRequest(new { message = CompanyMessages.NameTaken });
        }

        var now = DateTime.UtcNow;
        var company = new Company { CreatedAt = now };
        companyDto.ApplyTo(company);
        company.Approve(_access.UserId, now);

        try
        {
            await _companyRepository.AddCompanyAsync(company);
        }
        catch (Exception ex) when (ex.IsDuplicateKey())
        {
            return BadRequest(new { message = CompanyMessages.NameTaken });
        }

        var dto = await SummaryDtoAsync(company.Id);
        return CreatedAtAction(nameof(CompaniesController.GetCompany), "Companies", new { id = company.Id }, dto);
    }

    // Edycja danych firmy (status weryfikacji bez zmian)
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(CompanyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CompanyDto>> UpdateCompany(int id, CompanyFormDto companyDto)
    {
        var company = await _companyRepository.GetCompanyForUpdateAsync(id);
        if (company == null)
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        if (await _companyRepository.IsNameTakenAsync(CompanyNames.Normalize(companyDto.Name), id))
        {
            return BadRequest(new { message = CompanyMessages.NameTaken });
        }

        companyDto.ApplyTo(company);

        try
        {
            await _companyRepository.UpdateCompanyAsync(company);
        }
        catch (Exception ex) when (ex.IsDuplicateKey())
        {
            return BadRequest(new { message = CompanyMessages.NameTaken });
        }

        return Ok(await SummaryDtoAsync(id));
    }

    [HttpPost("{id:int}/approve")]
    [ProducesResponseType(typeof(CompanyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CompanyDto>> ApproveCompany(int id)
    {
        var company = await _companyRepository.GetCompanyForUpdateAsync(id);
        if (company == null)
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        company.Approve(_access.UserId, DateTime.UtcNow);
        await _companyRepository.UpdateCompanyAsync(company);

        return Ok(await SummaryDtoAsync(id));
    }

    // Odrzucenie z powodem widocznym dla pracodawcy; oferty firmy przestają być widoczne publicznie
    [HttpPost("{id:int}/reject")]
    [ProducesResponseType(typeof(CompanyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CompanyDto>> RejectCompany(int id, RejectCompanyDto rejectDto)
    {
        var company = await _companyRepository.GetCompanyForUpdateAsync(id);
        if (company == null)
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        company.Reject(rejectDto.Reason.Trim(), _access.UserId, DateTime.UtcNow);
        await _companyRepository.UpdateCompanyAsync(company);

        return Ok(await SummaryDtoAsync(id));
    }

    // Usunąć można tylko firmę bez ofert i bez kont pracodawców
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteCompany(int id)
    {
        var company = await _companyRepository.GetCompanyForUpdateAsync(id);
        if (company == null)
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        if (await _companyRepository.HasJobsOrMembersAsync(id))
        {
            return BadRequest(new { message = "Company has job offers or members" });
        }

        try
        {
            await _companyRepository.DeleteCompanyAsync(company);
        }
        catch (Exception ex) when (ex.IsForeignKeyViolation())
        {
            // równolegle dodano ofertę albo członka (klucze obce Restrict)
            return BadRequest(new { message = "Company has job offers or members" });
        }

        return NoContent();
    }

    private async Task<CompanyDto> SummaryDtoAsync(int id)
    {
        var summary = await _companyRepository.GetSummaryAsync(id);
        return summary!.ToDto(includeReview: true, includeMembers: true);
    }
}
