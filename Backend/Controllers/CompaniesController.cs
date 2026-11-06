using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Mappings;
using Backend.Models;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

// Publiczne profile firm - lista wyłącznie zatwierdzonych
[Route("api/[controller]")]
[ApiController]
public class CompaniesController : ControllerBase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly ICurrentUserAccess _access;

    public CompaniesController(ICompanyRepository companyRepository, ICurrentUserAccess access)
    {
        _companyRepository = companyRepository;
        _access = access;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<CompanyDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<CompanyDto>>> GetCompanies([FromQuery] CompanyParams companyParams)
    {
        var companies = await _companyRepository.GetPublicCompaniesAsync(companyParams);

        Response.AddPaginationHeader(companies);

        return Ok(companies.Select(c => c.ToDto(includeReview: false)));
    }

    // Firma zatwierdzona - dla każdego; oczekująca / odrzucona - tylko dla admina i członków firmy (inaczej 404)
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(CompanyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CompanyDto>> GetCompany(int id)
    {
        var company = await _companyRepository.GetSummaryAsync(id);
        if (company == null)
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        var canManage = await _access.CanManageCompanyAsync(id);
        if (company.Company.Status != CompanyStatus.Approved && !canManage)
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        return Ok(company.ToDto(includeReview: canManage));
    }
}
