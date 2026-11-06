using System.Net;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Integration;

// Usunięcie konta (DELETE /api/account z hasłem w treści): aplikacje, załączniki, dokumenty profilu, zapisane oferty
// i e-maile znikają razem z kontem, pliki z magazynu po zatwierdzeniu; admin i ostatni członek firmy z ofertami - 400
[Collection(ApiCollection.Name)]
public class DeleteAccountTests
{
    private readonly ApiFixture _api;
    private readonly AttachmentScenario _attachments;

    public DeleteAccountTests(ApiFixture api)
    {
        _api = api;
        _attachments = new AttachmentScenario(api);
    }

    [Fact]
    public async Task Candidate_DeletesTheAccountWithAllDataAndFiles()
    {
        var setup = await _attachments.NewSetupAsync();
        var candidate = setup.Candidate;
        await _attachments.UploadOkAsync(candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "cv.pdf");
        using var client = _api.CreateClient(candidate);
        var documents = await client.UploadDocumentsOkAsync(new AttachmentUpload(AttachmentTestFiles.Pdf(), "profil.pdf", AttachmentKind.Cv));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"{AccountApi.SavedJobsUrl}/{setup.Job.Id}", null)).StatusCode);

        var attachmentKeys = await _attachments.StoredKeysAsync(setup.Application.Id);
        var documentKey = await _api.QueryDbAsync(db => db.ProfileDocuments.Where(d => d.Id == documents[0].Id).Select(d => d.FileKey).SingleAsync());
        var files = attachmentKeys.Append(documentKey).Select(_attachments.StoragePath).ToList();
        Assert.All(files, path => Assert.True(File.Exists(path)));

        var response = await client.DeleteAccountAsync(candidate.Password);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.All(files, path => Assert.False(File.Exists(path)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(AccountApi.ProfileUrl)).StatusCode);

        using var anonymous = _api.CreateClient();
        var login = await anonymous.PostJsonAsync("/api/auth/login", new LoginDto { Email = candidate.Email, Password = candidate.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        var leftovers = await _api.QueryDbAsync(async db => new
        {
            Users = await db.Users.CountAsync(u => u.Id == candidate.Id),
            Applications = await db.JobApplications.CountAsync(a => a.UserId == candidate.Id),
            Attachments = await db.ApplicationAttachments.CountAsync(a => a.JobApplicationId == setup.Application.Id),
            Documents = await db.ProfileDocuments.CountAsync(d => d.UserId == candidate.Id),
            Saved = await db.SavedJobs.CountAsync(s => s.UserId == candidate.Id),
            Emails = await db.EmailMessages.CountAsync(m => m.UserId == candidate.Id),
            Roles = await db.UserRoles.CountAsync(r => r.UserId == candidate.Id)
        });
        Assert.Equivalent(new { Users = 0, Applications = 0, Attachments = 0, Documents = 0, Saved = 0, Emails = 0, Roles = 0 }, leftovers);

        // oferta i pracodawca zostają
        using var employer = _api.CreateClient(setup.Employer);
        Assert.Equal(HttpStatusCode.OK, (await employer.GetAsync($"/api/jobs/{setup.Job.Id}")).StatusCode);
    }

    [Fact]
    public async Task WrongPassword_Returns400AndKeepsTheAccount()
    {
        var user = await _api.RegisterUserAsync("delete");
        using var client = _api.CreateClient(user);

        var response = await client.DeleteAccountAsync("Wrong-Passw0rd");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Current password is incorrect", await response.ReadMessageAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(AccountApi.ProfileUrl)).StatusCode);
    }

    [Fact]
    public async Task MissingPassword_Returns400()
    {
        var user = await _api.RegisterUserAsync("delete");
        using var client = _api.CreateClient(user);

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/account")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        });
        // bez treści - 415 (brak JSON-a)
        var withoutBody = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/account"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Password", await response.ReadValidationFieldsAsync());
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, withoutBody.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(AccountApi.ProfileUrl)).StatusCode);
    }

    [Fact]
    public async Task Admin_CannotDeleteTheAccountHere()
    {
        using var admin = await _api.AdminClientAsync();

        var response = await admin.DeleteAccountAsync(TestUsers.AdminPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Administrator accounts cannot be deleted here", await response.ReadMessageAsync());
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(AccountApi.ProfileUrl)).StatusCode);
    }

    [Fact]
    public async Task LastMemberOfACompanyWithOffers_CannotDeleteTheAccount()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        await _api.CreateEmployerJobAsync(employer);
        using var client = _api.CreateClient(employer);

        var response = await client.DeleteAccountAsync(employer.User.Password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("You are the last member of a company with job offers", await response.ReadMessageAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(AccountApi.ProfileUrl)).StatusCode);
    }

    [Fact]
    public async Task EmployerWithoutOffers_DeletesTheAccountAndTheEmptyCompany()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var client = _api.CreateClient(employer);

        var response = await client.DeleteAccountAsync(employer.User.Password);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await _api.QueryDbAsync(db => db.Companies.AnyAsync(c => c.Id == employer.CompanyId)));
    }

    [Fact]
    public async Task EmployerWithAnotherMember_DeletesTheAccountAndKeepsTheCompanyWithOffers()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        // drugi członek tej samej firmy (konto pracodawcy przypisane w bazie)
        var colleague = await _api.RegisterEmployerAsync();
        await _api.QueryDbAsync(db => db.Users.Where(u => u.Id == colleague.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.CompanyId, employer.CompanyId)));
        using var client = _api.CreateClient(employer);

        var response = await client.DeleteAccountAsync(employer.User.Password);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(await _api.QueryDbAsync(db => db.JobOffers.AnyAsync(j => j.Id == job.Id && j.CompanyId == employer.CompanyId)));

        // ostatni członek już nie może
        using var colleagueClient = _api.CreateClient(colleague);
        var last = await colleagueClient.DeleteAccountAsync(colleague.User.Password);
        Assert.Equal(HttpStatusCode.BadRequest, last.StatusCode);
    }

    [Fact]
    public async Task Recruiter_DeletionKeepsTheCandidateHistoryWithoutTheAuthor()
    {
        var setup = await _attachments.NewSetupAsync();
        // drugi członek firmy, żeby konto rekrutera dało się usunąć
        var colleague = await _api.RegisterEmployerAsync();
        await _api.QueryDbAsync(db => db.Users.Where(u => u.Id == colleague.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.CompanyId, setup.Employer.CompanyId)));
        using var recruiter = _api.CreateClient(setup.Employer);
        Assert.Equal(HttpStatusCode.OK,
            (await ApiFixture.ChangeApplicationStatusAsync(recruiter, setup.Application.Id, ApplicationStatus.InReview)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await recruiter.DeleteAccountAsync(setup.Employer.User.Password)).StatusCode);

        using var candidate = _api.CreateClient(setup.Candidate);
        var application = await _api.GetApplicationAsync(candidate, setup.Application.Id);
        Assert.Equal(ApplicationStatus.InReview, application.Status);
        Assert.Equal(2, application.History!.Count);
    }
}
