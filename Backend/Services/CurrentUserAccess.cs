using System.Security.Claims;
using Backend.Data;
using Backend.Interfaces;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public class CurrentUserAccess : ICurrentUserAccess
{
    private readonly ApplicationDbContext _context;
    private readonly ClaimsPrincipal _user;
    private Task<int?>? _employerCompanyId;

    public CurrentUserAccess(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _user = httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();
    }

    public string? UserId => _user.FindFirstValue(ClaimTypes.NameIdentifier);

    public bool IsAdmin => _user.IsInRole(Roles.Admin);

    public bool IsEmployer => _user.IsInRole(Roles.Employer);

    public Task<int?> GetEmployerCompanyIdAsync() => _employerCompanyId ??= LoadEmployerCompanyIdAsync();

    public async Task<ManagementScope?> GetManagementScopeAsync()
    {
        if (IsAdmin)
        {
            return ManagementScope.AllCompanies;
        }

        var companyId = await GetEmployerCompanyIdAsync();
        return companyId.HasValue ? new ManagementScope(companyId) : null;
    }

    public async Task<bool> CanManageCompanyAsync(int companyId)
    {
        if (IsAdmin)
        {
            return true;
        }

        return await GetEmployerCompanyIdAsync() == companyId;
    }

    public Task<bool> CanManageJobAsync(JobOffer job) => CanManageCompanyAsync(job.CompanyId);

    public async Task<bool> CanManageApplicationAsync(JobApplication application)
    {
        if (IsAdmin)
        {
            return true;
        }

        // encja do zmiany statusu jest wczytywana bez nawigacji - wtedy firma oferty z bazy
        var companyId = application.JobOffer?.CompanyId ?? await _context.JobOffers
            .Where(j => j.Id == application.JobOfferId)
            .Select(j => (int?)j.CompanyId)
            .FirstOrDefaultAsync();

        return companyId.HasValue && await CanManageCompanyAsync(companyId.Value);
    }

    public async Task<bool> CanViewJobAsync(JobOffer job)
    {
        // oferty zamknięte i wygasłe są widoczne publicznie (frontend pokazuje baner)
        if (job.Status != JobOfferStatus.Draft && job.Company.Status == CompanyStatus.Approved)
        {
            return true;
        }

        return await CanManageJobAsync(job);
    }

    private async Task<int?> LoadEmployerCompanyIdAsync()
    {
        var userId = UserId;
        if (!IsEmployer || string.IsNullOrEmpty(userId))
        {
            return null;
        }

        return await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.CompanyId)
            .FirstOrDefaultAsync();
    }
}
