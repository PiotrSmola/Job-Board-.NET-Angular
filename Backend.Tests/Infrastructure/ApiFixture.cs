using System.Net;
using System.Net.Http.Headers;
using Backend.Data;
using Backend.DTOs;
using Backend.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Infrastructure;

// Fixture z uruchomioną aplikacją na własnej bazie MySQL. Współdzielona między klasami testowymi
// (kolekcja "Api"); testy mutujące dane tworzą własnych użytkowników i oferty.
public class ApiFixture : IAsyncLifetime
{
    private readonly bool _cacheTokens;
    private readonly Lazy<Task<string>> _adminToken;
    private readonly Lazy<Task<string>> _userToken;

    public JobBoardFactory Factory { get; }

    // domyślnie: środowisko Development (reset danych dozwolony), jawnie ustawiony sekret JWT
    public ApiFixture() : this("Development", JobBoardFactory.TestJwtSecret, allowSeedRefresh: false)
    {
    }

    // cacheTokens: false - każde AdminClientAsync/UserClientAsync loguje się od nowa (potrzebne, gdy testy
    // resetują dane: konta z seeda dostają nowe id i security stamp, więc wcześniejsze tokeny tracą ważność)
    // demoAccounts: jawna wartość Seed:DemoAccounts (null - domyślnie: konta demo tylko w Development)
    protected ApiFixture(string environment, string? jwtSecret, bool allowSeedRefresh, bool cacheTokens = true, bool? demoAccounts = null)
    {
        _cacheTokens = cacheTokens;
        Factory = new JobBoardFactory(environment, jwtSecret, allowSeedRefresh, demoAccounts);
        _adminToken = new Lazy<Task<string>>(() => LoginAsync(TestUsers.AdminEmail, TestUsers.AdminPassword));
        _userToken = new Lazy<Task<string>>(() => LoginAsync(TestUsers.UserEmail, TestUsers.UserPassword));
    }

    public Task InitializeAsync()
    {
        // wymusza start aplikacji (migracje i seed) już przy tworzeniu fixture
        _ = Factory.Server;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await Factory.DisposeAndDropDatabaseAsync();

    public HttpClient CreateClient(string? token = null)
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (token != null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    public HttpClient CreateClient(TestUser user) => CreateClient(user.Token);

    public async Task<HttpClient> AdminClientAsync() =>
        CreateClient(_cacheTokens ? await _adminToken.Value : await LoginAsync(TestUsers.AdminEmail, TestUsers.AdminPassword));

    public async Task<HttpClient> UserClientAsync() =>
        CreateClient(_cacheTokens ? await _userToken.Value : await LoginAsync(TestUsers.UserEmail, TestUsers.UserPassword));

    public async Task<string> LoginAsync(string email, string password)
    {
        using var client = CreateClient();
        var response = await client.PostJsonAsync("/api/auth/login", new LoginDto { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadJsonAsync<UserDto>()).Token;
    }

    // Rejestruje nowego użytkownika z unikalnym adresem e-mail
    public async Task<TestUser> RegisterUserAsync(string? emailPrefix = null, string password = "Passw0rd1")
    {
        var unique = Guid.NewGuid().ToString("N");
        var email = $"{emailPrefix ?? "user"}_{unique}@test.local";
        var dto = new RegisterDto { Email = email, Password = password, FirstName = "Test", LastName = "User" };

        using var client = CreateClient();
        var response = await client.PostJsonAsync("/api/auth/register", dto);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var user = await response.ReadJsonAsync<UserDto>();
        return new TestUser(email, password, dto.FirstName, dto.LastName, user.Token, JwtTestHelper.GetUserId(user.Token));
    }

    // Odczyt bezpośrednio z bazy (pola, których API nie zwraca, np. ReviewedByUserId)
    public async Task<T> QueryDbAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        using var scope = Factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    // Unikalna nazwa (np. firmy) - testy współdzielą bazę, więc nazwy nie mogą się powtarzać
    public static string UniqueName(string prefix) => $"{prefix} {Guid.NewGuid().ToString("N")[..10]}";

    // Pracodawca zarejestrowany przez POST /api/auth/register-employer (jego firma czeka na weryfikację).
    // customize pozwala zmienić dane rejestracji; zwrócone CompanyId pochodzi z GET /api/employer/company.
    public async Task<TestEmployer> RegisterEmployerAsync(string? companyName = null, Action<RegisterEmployerDto>? customize = null)
    {
        var unique = Guid.NewGuid().ToString("N");
        var dto = new RegisterEmployerDto
        {
            Email = $"employer_{unique}@test.local",
            Password = "Passw0rd1",
            FirstName = "Emp",
            LastName = "Loyer",
            CompanyName = companyName ?? UniqueName("Firma")
        };
        customize?.Invoke(dto);

        using var anonymous = CreateClient();
        var response = await anonymous.PostJsonAsync("/api/auth/register-employer", dto);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.ReadJsonAsync<UserDto>();

        using var client = CreateClient(user.Token);
        var companyResponse = await client.GetAsync("/api/employer/company");
        Assert.Equal(HttpStatusCode.OK, companyResponse.StatusCode);
        var company = await companyResponse.ReadJsonAsync<CompanyDto>();

        return new TestEmployer(
            new TestUser(dto.Email, dto.Password, dto.FirstName, dto.LastName, user.Token, JwtTestHelper.GetUserId(user.Token)),
            company.Id,
            company.Name);
    }

    // Pracodawca, którego firmę admin od razu zatwierdził
    public async Task<TestEmployer> RegisterApprovedEmployerAsync(string? companyName = null)
    {
        var employer = await RegisterEmployerAsync(companyName);
        await ApproveCompanyAsync(employer.CompanyId);
        return employer;
    }

    public HttpClient CreateClient(TestEmployer employer) => CreateClient(employer.Token);

    public async Task<CompanyDto> ApproveCompanyAsync(int companyId)
    {
        using var admin = await AdminClientAsync();
        var response = await admin.PostAsync($"/api/admin/companies/{companyId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<CompanyDto>();
    }

    public async Task<CompanyDto> RejectCompanyAsync(int companyId, string reason = "Not enough information")
    {
        using var admin = await AdminClientAsync();
        var response = await admin.PostJsonAsync($"/api/admin/companies/{companyId}/reject", new RejectCompanyDto { Reason = reason });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<CompanyDto>();
    }

    // Firma utworzona przez admina (od razu zatwierdzona)
    public async Task<CompanyDto> CreateCompanyAsync(string? name = null, Action<CompanyFormDto>? customize = null)
    {
        var dto = new CompanyFormDto { Name = name ?? UniqueName("Firma") };
        customize?.Invoke(dto);

        using var admin = await AdminClientAsync();
        var response = await admin.PostJsonAsync("/api/admin/companies", dto);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<CompanyDto>();
    }

    // Dane oferty pracodawcy - bez companyId (dla pracodawcy firma wynika z konta)
    public CreateJobOfferDto NewEmployerJobDto(Action<CreateJobOfferDto>? customize = null) =>
        NewJobDto(j =>
        {
            j.CompanyId = null;
            customize?.Invoke(j);
        });

    // Tworzy ofertę jako pracodawca (oczekiwany sukces 201)
    public async Task<JobOfferDto> CreateEmployerJobAsync(TestEmployer employer, Action<CreateJobOfferDto>? customize = null)
    {
        using var client = CreateClient(employer);
        var response = await client.PostJsonAsync("/api/jobs", NewEmployerJobDto(customize));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<JobOfferDto>();
    }

    // Zmiana statusu aplikacji przez wskazanego klienta (pracodawcę albo admina)
    public static Task<HttpResponseMessage> ChangeApplicationStatusAsync(HttpClient client, int applicationId, ApplicationStatus status, string? comment = null) =>
        client.PutJsonAsync($"/api/jobapplications/{applicationId}/status",
            new UpdateApplicationStatusDto { Status = status, Comment = comment });

    public const string DefaultCompanyName = "Test Company";

    // Zatwierdzona firma testowa (tworzona przy pierwszym użyciu - także po resecie danych demo)
    public int DefaultCompanyId => CompanyId(DefaultCompanyName);

    // Id firmy o danej nazwie; brakująca firma jest tworzona bezpośrednio w bazie z zadanym statusem
    public int CompanyId(string name, CompanyStatus status = CompanyStatus.Approved)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var lowered = name.ToLower();
        var company = context.Companies.FirstOrDefault(c => c.Name.ToLower() == lowered);
        if (company == null)
        {
            company = new Company { Name = name, Status = status, CreatedAt = DateTime.UtcNow };
            context.Companies.Add(company);
            context.SaveChanges();
        }

        return company.Id;
    }

    // Dane nowej oferty w firmie testowej (admin musi podać companyId)
    public CreateJobOfferDto NewJobDto(Action<CreateJobOfferDto>? customize = null)
    {
        var dto = new CreateJobOfferDto
        {
            Title = "Test position " + Guid.NewGuid().ToString("N")[..8],
            CompanyId = DefaultCompanyId,
            Description = "Test description",
            SalaryMin = 5000,
            SalaryMax = 8000,
            Location = "Rzeszów",
            WorkType = WorkType.Remote
        };
        customize?.Invoke(dto);
        return dto;
    }

    // Tworzy ofertę jako admin
    public async Task<JobOfferDto> CreateJobAsync(Action<CreateJobOfferDto>? customize = null)
    {
        using var admin = await AdminClientAsync();
        var response = await admin.PostJsonAsync("/api/jobs", NewJobDto(customize));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<JobOfferDto>();
    }

    // Oferta w zadanym statusie; data ważności w przeszłości jest dozwolona tylko dla szkicu i oferty zamkniętej
    public Task<JobOfferDto> CreateJobWithStatusAsync(JobOfferStatus status, DateTime? expiresAt = null, Action<CreateJobOfferDto>? customize = null) =>
        CreateJobAsync(j =>
        {
            j.Status = status;
            j.ExpiresAt = expiresAt;
            customize?.Invoke(j);
        });

    // Oferta aktywna z datą ważności w przeszłości (API nie pozwala jej utworzyć, więc data jest ustawiana w bazie)
    public async Task<JobOfferDto> CreateExpiredJobAsync(Action<CreateJobOfferDto>? customize = null)
    {
        var job = await CreateJobAsync(j =>
        {
            j.Status = JobOfferStatus.Active;
            j.ExpiresAt = DateTime.UtcNow.AddDays(30);
            customize?.Invoke(j);
        });
        return await SetJobExpiresAtAsync(job.Id, DateTime.UtcNow.AddDays(-2));
    }

    // Ustawia ExpiresAt bezpośrednio w bazie (z pominięciem walidacji API) i zwraca ofertę widzianą przez admina
    public async Task<JobOfferDto> SetJobExpiresAtAsync(int jobId, DateTime? expiresAt)
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.JobOffers.Where(j => j.Id == jobId)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.ExpiresAt, expiresAt));
        }

        using var admin = await AdminClientAsync();
        var response = await admin.GetAsync($"/api/jobs/{jobId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<JobOfferDto>();
    }

    public async Task<HttpResponseMessage> ChangeApplicationStatusAsync(int applicationId, ApplicationStatus status, string? comment = null)
    {
        using var admin = await AdminClientAsync();
        return await admin.PutJsonAsync($"/api/jobapplications/{applicationId}/status",
            new UpdateApplicationStatusDto { Status = status, Comment = comment });
    }

    // Zmiana statusu przez admina z oczekiwanym sukcesem; zwraca aplikację z historią
    public async Task<JobApplicationDto> SetApplicationStatusAsync(int applicationId, ApplicationStatus status, string? comment = null)
    {
        var response = await ChangeApplicationStatusAsync(applicationId, status, comment);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<JobApplicationDto>();
    }

    public async Task<HttpResponseMessage> WithdrawAsync(TestUser user, int applicationId)
    {
        using var client = CreateClient(user);
        return await client.PostAsync($"/api/jobapplications/{applicationId}/withdraw", null);
    }

    public async Task<JobApplicationDto> GetApplicationAsync(HttpClient client, int applicationId)
    {
        var response = await client.GetAsync($"/api/jobapplications/{applicationId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<JobApplicationDto>();
    }

    public async Task<JobApplicationDto> ApplyAsync(TestUser user, int jobId, string? note = null)
    {
        using var client = CreateClient(user);
        var response = await client.PostJsonAsync("/api/jobapplications",
            new CreateJobApplicationDto { JobOfferId = jobId, Note = note });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<JobApplicationDto>();
    }
}

// Pracodawca zarejestrowany przez API wraz z firmą
public sealed record TestEmployer(TestUser User, int CompanyId, string CompanyName)
{
    public string Email => User.Email;
    public string Token => User.Token;
    public string Id => User.Id;
}

// Wspólna kolekcja: jedna aplikacja i jedna baza dla wszystkich klas integracyjnych na danych seeda
[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "Api";
}
