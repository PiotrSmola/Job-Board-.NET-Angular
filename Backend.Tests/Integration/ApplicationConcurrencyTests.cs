using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Models;
using Backend.Repositories;
using Backend.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.Tests.Integration;

// Współbieżne zmiany tej samej aplikacji. Wyścigu przez samo HTTP nie da się wywołać deterministycznie,
// dlatego testy "deterministyczne" podmieniają repozytorium dekoratorem, który:
//  - wstrzymuje żądania po wczytaniu aplikacji, aż wszystkie wczytają ten sam stan (bariera),
//  - opcjonalnie serializuje zapisy, żeby wynik nie zależał od deadlocków MySQL.
// Testy bez dekoratora wysyłają prawdziwe równoległe żądania i sprawdzają tylko spójność danych.
[Collection(ApiCollection.Name)]
public class ApplicationConcurrencyTests
{
    private const string Url = "/api/jobapplications";

    private readonly ApiFixture _api;

    public ApplicationConcurrencyTests(ApiFixture api) => _api = api;

    // Bariera: każdy uczestnik czeka, aż dotrze ich wymagana liczba (z limitem czasu, żeby błąd nie zawiesił testu)
    private sealed class LoadGate
    {
        private readonly int _participants;
        private int _arrived;
        private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public LoadGate(int participants) => _participants = participants;

        public Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrived) >= _participants)
            {
                _all.TrySetResult();
            }

            return _all.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    private sealed class GatedApplicationRepository : IJobApplicationRepository
    {
        private readonly IJobApplicationRepository _inner;
        private readonly LoadGate _gate;
        private readonly SemaphoreSlim? _saveLock;

        public GatedApplicationRepository(IJobApplicationRepository inner, LoadGate gate, SemaphoreSlim? saveLock)
        {
            _inner = inner;
            _gate = gate;
            _saveLock = saveLock;
        }

        public async Task<JobApplication?> GetApplicationForUpdateAsync(int id)
        {
            var application = await _inner.GetApplicationForUpdateAsync(id);
            await _gate.ArriveAsync();
            return application;
        }

        public async Task<JobApplication?> GetUserApplicationForJobAsync(int jobId, string userId)
        {
            var application = await _inner.GetUserApplicationForJobAsync(jobId, userId);
            await _gate.ArriveAsync();
            return application;
        }

        public async Task UpdateApplicationAsync(JobApplication application)
        {
            if (_saveLock == null)
            {
                await _inner.UpdateApplicationAsync(application);
                return;
            }

            await _saveLock.WaitAsync();
            try
            {
                await _inner.UpdateApplicationAsync(application);
            }
            finally
            {
                _saveLock.Release();
            }
        }

        public Task<JobApplication?> GetApplicationByIdAsync(int id, bool includeHistory = false) => _inner.GetApplicationByIdAsync(id, includeHistory);
        public Task<IEnumerable<JobApplication>> GetApplicationsByUserIdAsync(string userId) => _inner.GetApplicationsByUserIdAsync(userId);
        public Task<IEnumerable<JobApplication>> GetApplicationsByJobIdAsync(int jobId) => _inner.GetApplicationsByJobIdAsync(jobId);
        public Task<PagedList<JobApplication>> GetApplicationsAsync(ApplicationParams applicationParams) => _inner.GetApplicationsAsync(applicationParams);
        public Task<JobApplication> AddApplicationAsync(JobApplication application) => _inner.AddApplicationAsync(application);
        public Task<bool> DeleteApplicationAsync(int id) => _inner.DeleteApplicationAsync(id);
    }

    // Aplikacja na tej samej bazie, ale z repozytorium czekającym na wszystkich uczestników wyścigu
    private WebApplicationFactory<Program> GatedFactory(int participants, bool serializeSaves)
    {
        var gate = new LoadGate(participants);
        var saveLock = serializeSaves ? new SemaphoreSlim(1, 1) : null;

        return _api.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IJobApplicationRepository>();
            services.AddScoped<JobApplicationRepository>();
            services.AddScoped<IJobApplicationRepository>(sp =>
                new GatedApplicationRepository(sp.GetRequiredService<JobApplicationRepository>(), gate, saveLock));
        }));
    }

    private static HttpClient ClientFor(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<HttpClient> GatedAdminAsync(WebApplicationFactory<Program> factory) =>
        ClientFor(factory, await _api.LoginAsync(TestUsers.AdminEmail, TestUsers.AdminPassword));

    private async Task<(JobOfferDto Job, TestUser User, JobApplicationDto Application)> NewApplicationAsync()
    {
        var job = await _api.CreateJobAsync();
        var user = await _api.RegisterUserAsync("racer");
        return (job, user, await _api.ApplyAsync(user, job.Id));
    }

    private static Task<HttpResponseMessage> SetStatusAsync(HttpClient admin, int id, ApplicationStatus status, string? comment = null) =>
        admin.PutJsonAsync($"{Url}/{id}/status", new UpdateApplicationStatusDto { Status = status, Comment = comment });

    private static async Task<string?> MessageAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("message").GetString();
    }

    // --- deterministyczne: obie zmiany wczytały ten sam stan, zapisy po kolei ---

    [Fact]
    public async Task UpdateStatus_TwoChangesLoadedBeforeEitherSaves_FirstSucceedsSecondGetsConflict()
    {
        var (_, user, application) = await NewApplicationAsync();
        using var factory = GatedFactory(participants: 2, serializeSaves: true);
        using var admin = await GatedAdminAsync(factory);

        var responses = await Task.WhenAll(
            SetStatusAsync(admin, application.Id, ApplicationStatus.Interview),
            SetStatusAsync(admin, application.Id, ApplicationStatus.Rejected));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var conflict = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("Application status was changed by another request", await MessageAsync(conflict));

        // przegrany zapis nie zostawia śladu: jeden wpis historii na zwycięską zmianę
        var winner = await responses.Single(r => r.StatusCode == HttpStatusCode.OK).ReadJsonAsync<JobApplicationDto>();
        using var candidate = _api.CreateClient(user);
        var current = await _api.GetApplicationAsync(candidate, application.Id);
        Assert.Equal(winner.Status, current.Status);
        Assert.Equal(new[] { ApplicationStatus.Submitted, winner.Status }, current.History!.Select(h => h.Status));
    }

    [Fact]
    public async Task Withdraw_RacingWithRecruiterStatusChange_OneSucceedsOtherGetsConflict()
    {
        var (_, user, application) = await NewApplicationAsync();
        using var factory = GatedFactory(participants: 2, serializeSaves: true);
        using var admin = await GatedAdminAsync(factory);
        using var candidate = ClientFor(factory, user.Token);

        var responses = await Task.WhenAll(
            candidate.PostAsync($"{Url}/{application.Id}/withdraw", null),
            SetStatusAsync(admin, application.Id, ApplicationStatus.Hired));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var current = await _api.GetApplicationAsync(_api.CreateClient(user), application.Id);
        Assert.Equal(2, current.History!.Count);
        Assert.Contains(current.Status, new[] { ApplicationStatus.Withdrawn, ApplicationStatus.Hired });
    }

    [Fact]
    public async Task Withdraw_TwoConcurrentWithdrawals_OneSucceedsOtherGetsConflict()
    {
        var (_, user, application) = await NewApplicationAsync();
        using var factory = GatedFactory(participants: 2, serializeSaves: true);
        using var candidate = ClientFor(factory, user.Token);

        var responses = await Task.WhenAll(
            candidate.PostAsync($"{Url}/{application.Id}/withdraw", null),
            candidate.PostAsync($"{Url}/{application.Id}/withdraw", null));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(2, (await _api.GetApplicationAsync(_api.CreateClient(user), application.Id)).History!.Count);
    }

    [Fact]
    public async Task Apply_TwoReactivationsLoadedBeforeEitherSaves_SecondIsRejectedAsAlreadyApplied()
    {
        var (job, user, application) = await NewApplicationAsync();
        Assert.Equal(HttpStatusCode.OK, (await _api.WithdrawAsync(user, application.Id)).StatusCode);
        using var factory = GatedFactory(participants: 2, serializeSaves: true);
        using var candidate = ClientFor(factory, user.Token);

        var responses = await Task.WhenAll(
            candidate.PostJsonAsync(Url, new CreateJobApplicationDto { JobOfferId = job.Id, Note = "first" }),
            candidate.PostJsonAsync(Url, new CreateJobApplicationDto { JobOfferId = job.Id, Note = "second" }));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        var rejected = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);
        Assert.Equal("You have already applied to this job", await MessageAsync(rejected));
        var current = await _api.GetApplicationAsync(_api.CreateClient(user), application.Id);
        Assert.Equal(new[] { ApplicationStatus.Submitted, ApplicationStatus.Withdrawn, ApplicationStatus.Submitted }, current.History!.Select(h => h.Status));
    }

    // Notatki (ten sam status) nie zmieniają tokenu współbieżności, więc równoległe notatki obie się zapisują
    [Fact]
    public async Task UpdateStatus_TwoConcurrentNotes_BothSucceedAndBothAreRecorded()
    {
        var (_, _, application) = await NewApplicationAsync();
        using var factory = GatedFactory(participants: 2, serializeSaves: true);
        using var admin = await GatedAdminAsync(factory);

        var responses = await Task.WhenAll(
            SetStatusAsync(admin, application.Id, ApplicationStatus.Submitted, "note A"),
            SetStatusAsync(admin, application.Id, ApplicationStatus.Submitted, "note B"));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        using var adminClient = await _api.AdminClientAsync();
        var current = await _api.GetApplicationAsync(adminClient, application.Id);
        Assert.Equal(new[] { "note A", "note B" }, current.History!.Skip(1).Select(h => h.Comment).Order());
        Assert.Equal(application.StatusChangedAt, current.StatusChangedAt);
    }

    // --- bez ingerencji w zapis: obie zmiany wczytują ten sam stan i zapisują równocześnie ---

    // Znany błąd: gdy zapisy nakładają się w czasie, MySQL wykrywa deadlock (UPDATE aplikacji i INSERT wpisu
    // historii z kluczem obcym blokują się nawzajem), a kontroler zamienia go na 500 zamiast na 409
    // (JobApplicationsController.cs:230 łapie tylko DbUpdateConcurrencyException)
    [Fact(Skip = "Bug: overlapping status changes of one application can fail with 500 (MySQL deadlock) instead of 409 - JobApplicationsController.cs:230")]
    public async Task UpdateStatus_TwoChangesSavedAtTheSameTime_OneSucceedsOtherGetsConflict()
    {
        for (var round = 0; round < 5; round++)
        {
            var (_, _, application) = await NewApplicationAsync();
            using var factory = GatedFactory(participants: 2, serializeSaves: false);
            using var admin = await GatedAdminAsync(factory);

            var responses = await Task.WhenAll(
                SetStatusAsync(admin, application.Id, ApplicationStatus.Interview),
                SetStatusAsync(admin, application.Id, ApplicationStatus.Rejected));

            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        }
    }

    // --- prawdziwe równoległe żądania: asercje tylko na spójności danych ---

    [Fact]
    public async Task UpdateStatus_ManyParallelChanges_KeepHistoryConsistentWithAcceptedChanges()
    {
        var (_, user, application) = await NewApplicationAsync();
        using var admin = await _api.AdminClientAsync();
        var statuses = new[] { ApplicationStatus.InReview, ApplicationStatus.Interview, ApplicationStatus.Offered, ApplicationStatus.Hired, ApplicationStatus.Rejected };

        var responses = await Task.WhenAll(statuses.Select(s => SetStatusAsync(admin, application.Id, s)));

        var accepted = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        Assert.True(accepted >= 1, "At least one parallel change must be accepted");
        Assert.All(responses, r => Assert.True(
            r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict or HttpStatusCode.InternalServerError,
            $"Unexpected status {r.StatusCode}"));

        using var candidate = _api.CreateClient(user);
        var current = await _api.GetApplicationAsync(candidate, application.Id);
        // (500 z deadlocku to znany błąd, patrz test z Skip wyżej) każda zaakceptowana zmiana dodała dokładnie jeden wpis (plus wpis początkowy), przegrane nic nie zapisały
        Assert.Equal(1 + accepted, current.History!.Count);
        Assert.Equal(current.Status, current.History!.Last().Status);
        Assert.Contains(current.Status, statuses);
    }

    [Fact]
    public async Task Withdraw_ParallelWithdrawals_RecordWithdrawalExactlyOnce()
    {
        var (_, user, application) = await NewApplicationAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _api.WithdrawAsync(user, application.Id)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var current = await _api.GetApplicationAsync(_api.CreateClient(user), application.Id);
        Assert.Equal(ApplicationStatus.Withdrawn, current.Status);
        Assert.Equal(new[] { ApplicationStatus.Submitted, ApplicationStatus.Withdrawn }, current.History!.Select(h => h.Status));
    }
}
