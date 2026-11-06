using System.Net;
using System.Text.Json;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Integration;

// Zapisane oferty (/api/saved-jobs): tylko kandydat, PUT/DELETE idempotentne, lista od najnowszych zapisów
// z paginacją (także wygasłe i zamknięte oferty), lista id, oferty niewidoczne publicznie - 404 / pomijane
[Collection(ApiCollection.Name)]
public class SavedJobsTests
{
    private readonly ApiFixture _api;

    public SavedJobsTests(ApiFixture api) => _api = api;

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, int jobId) =>
        client.PutAsync($"{AccountApi.SavedJobsUrl}/{jobId}", null);

    private static Task<HttpResponseMessage> RemoveAsync(HttpClient client, int jobId) =>
        client.DeleteAsync($"{AccountApi.SavedJobsUrl}/{jobId}");

    private static async Task<List<int>> IdsAsync(HttpClient client) =>
        await (await client.GetAsync($"{AccountApi.SavedJobsUrl}/ids")).ReadJsonAsync<List<int>>();

    [Fact]
    public async Task Save_IsIdempotentAndListedNewestFirstWithPagination()
    {
        var user = await _api.RegisterUserAsync("saved");
        using var client = _api.CreateClient(user);
        var jobs = new List<JobOfferDto>();
        for (var i = 0; i < 3; i++)
        {
            jobs.Add(await _api.CreateJobAsync());
        }

        foreach (var job in jobs)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await SaveAsync(client, job.Id)).StatusCode);
            await Task.Delay(20);
        }

        // ponowne zapisanie nie tworzy duplikatu ani nie zmienia kolejności
        var again = await SaveAsync(client, jobs[0].Id);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(0, again.Content.Headers.ContentLength ?? 0);

        var response = await client.GetAsync($"{AccountApi.SavedJobsUrl}?pageNumber=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.ReadJsonAsync<List<SavedJobDto>>();
        Assert.Equal([jobs[2].Id, jobs[1].Id], page.Select(s => s.Job.Id));
        Assert.True(page[0].SavedAt >= page[1].SavedAt);
        Assert.Equal(jobs[2].Title, page[0].Job.Title);
        var pagination = response.ReadPagination();
        Assert.Equal(1, pagination.CurrentPage);
        Assert.Equal(2, pagination.ItemsPerPage);
        Assert.Equal(3, pagination.TotalItems);
        Assert.Equal(2, pagination.TotalPages);

        var second = await (await client.GetAsync($"{AccountApi.SavedJobsUrl}?pageNumber=2&pageSize=2")).ReadJsonAsync<List<SavedJobDto>>();
        Assert.Equal([jobs[0].Id], second.Select(s => s.Job.Id));

        Assert.Equal([jobs[2].Id, jobs[1].Id, jobs[0].Id], await IdsAsync(client));
    }

    [Fact]
    public async Task List_HasTheContractShapeWithAFullJobOffer()
    {
        var user = await _api.RegisterUserAsync("saved");
        using var client = _api.CreateClient(user);
        var job = await _api.CreateJobAsync();
        await SaveAsync(client, job.Id);

        using var body = JsonDocument.Parse(await (await client.GetAsync(AccountApi.SavedJobsUrl)).Content.ReadAsStringAsync());

        var item = body.RootElement[0];
        Assert.Equal(["job", "savedAt"], item.EnumerateObject().Select(p => p.Name).Order().ToArray());
        var jobProperties = item.GetProperty("job").EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Contains("status", jobProperties);
        Assert.Contains("isExpired", jobProperties);
        Assert.Contains("company", jobProperties);
    }

    [Fact]
    public async Task List_IncludesTheOfferCategoryAndSkills()
    {
        var user = await _api.RegisterUserAsync("saved");
        using var client = _api.CreateClient(user);
        var category = await _api.CreateCategoryAsync();
        var skill = "s-" + TaxonomyScenario.Token();
        var job = await _api.CreateJobAsync(j => { j.CategoryId = category.Id; j.Skills = [skill]; });
        await SaveAsync(client, job.Id);

        var saved = await (await client.GetAsync(AccountApi.SavedJobsUrl)).ReadJsonAsync<List<SavedJobDto>>();

        var item = Assert.Single(saved, s => s.Job.Id == job.Id);
        Assert.Equal(category.Name, item.Job.CategoryName);
        Assert.Equal([skill], item.Job.Skills);
    }

    [Fact]
    public async Task Remove_IsIdempotent()
    {
        var user = await _api.RegisterUserAsync("saved");
        using var client = _api.CreateClient(user);
        var job = await _api.CreateJobAsync();
        await SaveAsync(client, job.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await RemoveAsync(client, job.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await RemoveAsync(client, job.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await RemoveAsync(client, int.MaxValue)).StatusCode);
        Assert.Empty(await IdsAsync(client));
    }

    [Fact]
    public async Task Save_UnknownDraftOrUnapprovedCompanyOffer_Returns404()
    {
        var user = await _api.RegisterUserAsync("saved");
        using var client = _api.CreateClient(user);
        var draft = await _api.CreateJobWithStatusAsync(JobOfferStatus.Draft);
        var pendingEmployer = await _api.RegisterEmployerAsync();
        var hidden = await _api.CreateEmployerJobAsync(pendingEmployer, j => j.Status = JobOfferStatus.Draft);

        foreach (var jobId in new[] { int.MaxValue, draft.Id, hidden.Id })
        {
            var response = await SaveAsync(client, jobId);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("Job offer not found", await response.ReadMessageAsync());
        }

        Assert.Empty(await IdsAsync(client));
    }

    [Fact]
    public async Task ExpiredAndClosedOffers_StayOnTheList_HiddenOnesAreSkipped()
    {
        var user = await _api.RegisterUserAsync("saved");
        using var client = _api.CreateClient(user);
        var expired = await _api.CreateJobAsync(j => j.ExpiresAt = DateTime.UtcNow.AddDays(10));
        var closed = await _api.CreateJobAsync();
        var laterDraft = await _api.CreateJobAsync();
        foreach (var job in new[] { expired, closed, laterDraft })
        {
            await SaveAsync(client, job.Id);
        }

        await _api.SetJobExpiresAtAsync(expired.Id, DateTime.UtcNow.AddDays(-1));
        await _api.QueryDbAsync(db => db.JobOffers.Where(j => j.Id == closed.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, JobOfferStatus.Closed)));
        await _api.QueryDbAsync(db => db.JobOffers.Where(j => j.Id == laterDraft.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, JobOfferStatus.Draft)));

        var list = await (await client.GetAsync(AccountApi.SavedJobsUrl)).ReadJsonAsync<List<SavedJobDto>>();

        Assert.Equal([closed.Id, expired.Id], list.Select(s => s.Job.Id));
        Assert.Equal(JobOfferStatus.Closed, list[0].Job.Status);
        Assert.True(list[1].Job.IsExpired);
        Assert.Equal([closed.Id, expired.Id], await IdsAsync(client));

        // pozycja szkicu zostaje i wraca po ponownej publikacji
        await _api.QueryDbAsync(db => db.JobOffers.Where(j => j.Id == laterDraft.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, JobOfferStatus.Active)));
        Assert.Contains(laterDraft.Id, await IdsAsync(client));
    }

    [Fact]
    public async Task DeletingTheOffer_RemovesItFromSavedJobs()
    {
        var user = await _api.RegisterUserAsync("saved");
        using var client = _api.CreateClient(user);
        var job = await _api.CreateJobAsync();
        await SaveAsync(client, job.Id);

        using var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/jobs/{job.Id}")).StatusCode);

        Assert.Empty(await IdsAsync(client));
        Assert.False(await _api.QueryDbAsync(db => db.SavedJobs.AnyAsync(s => s.JobOfferId == job.Id)));
    }

    [Fact]
    public async Task SavedJobs_AreForbiddenForEmployersAndAdminsAndRequireAuthentication()
    {
        var job = await _api.CreateJobAsync();
        var employer = await _api.RegisterEmployerAsync();
        using var employerClient = _api.CreateClient(employer);
        using var admin = await _api.AdminClientAsync();
        using var anonymous = _api.CreateClient();

        foreach (var client in new[] { employerClient, admin })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(AccountApi.SavedJobsUrl)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{AccountApi.SavedJobsUrl}/ids")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SaveAsync(client, job.Id)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await RemoveAsync(client, job.Id)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(AccountApi.SavedJobsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SaveAsync(anonymous, job.Id)).StatusCode);
    }

    [Fact]
    public async Task SavedJobs_AreSeparatePerCandidate()
    {
        var first = await _api.RegisterUserAsync("saved");
        var second = await _api.RegisterUserAsync("saved");
        using var firstClient = _api.CreateClient(first);
        using var secondClient = _api.CreateClient(second);
        var job = await _api.CreateJobAsync();

        await SaveAsync(firstClient, job.Id);
        await RemoveAsync(secondClient, job.Id);

        Assert.Equal([job.Id], await IdsAsync(firstClient));
        Assert.Empty(await IdsAsync(secondClient));
    }

    [Fact]
    public async Task ConcurrentSaves_OfTheSameOffer_AllSucceed()
    {
        var user = await _api.RegisterUserAsync("saved");
        using var client = _api.CreateClient(user);
        var job = await _api.CreateJobAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => SaveAsync(client, job.Id)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        Assert.Equal([job.Id], await IdsAsync(client));
    }
}
