using System.Net;
using Backend.Data;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Integration;

// Zapis taksonomii przy tworzeniu i edycji ofert: umiejętności (tworzenie, dopasowanie bez wielkości liter, limit 15,
// przecinek), kategoria, poziom, typy umów, waluta; uprawnienia pracodawcy; podpowiedzi /api/skills.
[Collection(ApiCollection.Name)]
public class JobTaxonomyWriteTests
{
    private readonly ApiFixture _api;

    public JobTaxonomyWriteTests(ApiFixture api) => _api = api;

    private async Task<HttpResponseMessage> PostAsync(Action<CreateJobOfferDto> customize)
    {
        using var admin = await _api.AdminClientAsync();
        return await admin.PostJsonAsync("/api/jobs", _api.NewJobDto(customize));
    }

    private async Task<int> SkillRowsAsync(string key) =>
        await _api.QueryDbAsync(db => db.Skills.CountAsync(s => s.NormalizedName == key));

    private static UpdateJobOfferDto UpdateFrom(JobOfferDto job, Action<UpdateJobOfferDto>? customize = null)
    {
        var dto = new UpdateJobOfferDto
        {
            Title = job.Title,
            CompanyId = job.CompanyId, // admin musi wskazać firmę
            Description = job.Description,
            Location = job.Location,
            WorkType = job.WorkType,
            SalaryMin = job.SalaryMin,
            SalaryMax = job.SalaryMax,
            CategoryId = job.CategoryId,
            Seniority = job.Seniority,
            ContractTypes = job.ContractTypes,
            SalaryCurrency = job.SalaryCurrency,
            SalaryPeriod = job.SalaryPeriod,
            Skills = job.Skills.ToList()
        };
        customize?.Invoke(dto);
        return dto;
    }

    // --- umiejętności ---

    [Fact]
    public async Task Create_WithSkills_NormalizesDeduplicatesAndReusesExistingOnesIgnoringCase()
    {
        var token = TaxonomyScenario.Token();
        var response = await PostAsync(j => j.Skills = [$"  Rust {token}  ", $"RUST  {token}", $"Zażółć {token}"]);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var first = await response.ReadJsonAsync<JobOfferDto>();
        Assert.Equal(new[] { $"Rust {token}", $"Zażółć {token}" }, first.Skills);

        // druga oferta z tą samą umiejętnością w innym zapisie nie tworzy duplikatu i zachowuje pierwszą nazwę
        var second = await (await PostAsync(j => j.Skills = [$"rust {token}"])).ReadJsonAsync<JobOfferDto>();
        Assert.Equal(new[] { $"Rust {token}" }, second.Skills);
        Assert.Equal(1, await SkillRowsAsync($"rust {token}"));

        // szczegóły oferty zwracają te same umiejętności
        using var client = _api.CreateClient();
        var details = await (await client.GetAsync($"/api/jobs/{first.Id}")).ReadJsonAsync<JobOfferDto>();
        Assert.Equal(first.Skills, details.Skills);
    }

    [Fact]
    public async Task Create_WithFifteenSkills_Succeeds_AndSixteenIsRejected()
    {
        var token = TaxonomyScenario.Token();
        List<string> Skills(int count) => Enumerable.Range(1, count).Select(i => $"s{i:D2}-{token}").ToList();

        var ok = await PostAsync(j => j.Skills = Skills(15));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(15, (await ok.ReadJsonAsync<JobOfferDto>()).Skills.Count);

        var tooMany = await PostAsync(j => j.Skills = Skills(16));
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Contains("Skills", await tooMany.ReadValidationFieldsAsync());
    }

    [Theory]
    [InlineData("C#, C++")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("​")]
    public async Task Create_WithInvalidSkillName_ReturnsBadRequest(string name)
    {
        var response = await PostAsync(j => j.Skills = [name]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Skills", await response.ReadValidationFieldsAsync());
    }

    [Fact]
    public async Task Create_WithSkillNameLongerThanFiftyCharacters_ReturnsBadRequest()
    {
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(j => j.Skills = [new string('k', 50)])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(j => j.Skills = [new string('k', 51)])).StatusCode);
    }

    [Fact]
    public async Task Create_WithTheSameNewSkillInParallel_CreatesItOnce()
    {
        var name = "Parallel " + TaxonomyScenario.Token();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PostAsync(j => j.Skills = [name])));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(1, await SkillRowsAsync(name.ToLowerInvariant()));
    }

    [Fact]
    public async Task Update_ReplacesSkills_AndAnEmptyListClearsThem()
    {
        var token = TaxonomyScenario.Token();
        var job = await _api.CreateJobAsync(j => j.Skills = [$"a {token}", $"b {token}"]);
        using var admin = await _api.AdminClientAsync();

        var replaced = await admin.PutJsonAsync($"/api/jobs/{job.Id}", UpdateFrom(job, j => j.Skills = [$"B {token}", $"c {token}"]));
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.Equal(new[] { $"b {token}", $"c {token}" }, (await replaced.ReadJsonAsync<JobOfferDto>()).Skills);

        var cleared = await admin.PutJsonAsync($"/api/jobs/{job.Id}", UpdateFrom(job, j => j.Skills = []));
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Empty((await cleared.ReadJsonAsync<JobOfferDto>()).Skills);

        // powiązania znikają, same umiejętności zostają w słowniku
        Assert.Equal(0, await _api.QueryDbAsync(db => db.JobOffers.Where(o => o.Id == job.Id).SelectMany(o => o.Skills).CountAsync()));
        Assert.Equal(1, await SkillRowsAsync($"c {token}"));
    }

    [Fact]
    public async Task StatusChange_KeepsSkillsAndTaxonomy()
    {
        var token = TaxonomyScenario.Token();
        var category = await _api.CreateCategoryAsync();
        var job = await _api.CreateJobAsync(j =>
        {
            j.CategoryId = category.Id; j.Seniority = SeniorityLevel.Lead; j.Skills = [$"keep {token}"];
        });
        using var admin = await _api.AdminClientAsync();

        var closed = await admin.PatchJsonAsync($"/api/jobs/{job.Id}/status", new UpdateJobOfferStatusDto { Status = JobOfferStatus.Closed });

        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        var dto = await closed.ReadJsonAsync<JobOfferDto>();
        Assert.Equal(new[] { $"keep {token}" }, dto.Skills);
        Assert.Equal(category.Id, dto.CategoryId);
        Assert.Equal(SeniorityLevel.Lead, dto.Seniority);
    }

    // --- pozostałe pola taksonomii ---

    [Fact]
    public async Task CreateAndUpdate_StoreCategoryLevelContractsCurrencyAndPeriod()
    {
        var category = await _api.CreateCategoryAsync();
        var other = await _api.CreateCategoryAsync();
        var job = await _api.CreateJobAsync(j =>
        {
            j.CategoryId = category.Id; j.Seniority = SeniorityLevel.Mid;
            j.ContractTypes = ContractTypes.B2B | ContractTypes.Internship;
            j.SalaryCurrency = Currency.GBP; j.SalaryPeriod = SalaryPeriod.Hour;
        });

        Assert.Equal((category.Id, category.Name, category.Slug), (job.CategoryId, job.CategoryName, job.CategorySlug));
        Assert.Equal((SeniorityLevel.Mid, ContractTypes.B2B | ContractTypes.Internship, Currency.GBP, SalaryPeriod.Hour),
            (job.Seniority, job.ContractTypes, job.SalaryCurrency, job.SalaryPeriod));

        using var admin = await _api.AdminClientAsync();
        var updated = await (await admin.PutJsonAsync($"/api/jobs/{job.Id}", UpdateFrom(job, j =>
        {
            j.CategoryId = other.Id; j.Seniority = null; j.ContractTypes = ContractTypes.None;
            j.SalaryCurrency = Currency.PLN; j.SalaryPeriod = SalaryPeriod.Year;
        }))).ReadJsonAsync<JobOfferDto>();

        Assert.Equal(other.Id, updated.CategoryId);
        Assert.Equal(other.Slug, updated.CategorySlug);
        Assert.Null(updated.Seniority);
        Assert.Equal(ContractTypes.None, updated.ContractTypes);
        Assert.Equal((Currency.PLN, SalaryPeriod.Year), (updated.SalaryCurrency, updated.SalaryPeriod));
    }

    [Fact]
    public async Task Create_WithoutTaxonomyFields_UsesDefaults()
    {
        var job = await _api.CreateJobAsync();

        Assert.Null(job.CategoryId);
        Assert.Null(job.CategoryName);
        Assert.Null(job.Seniority);
        Assert.Equal(ContractTypes.None, job.ContractTypes);
        Assert.Equal((Currency.PLN, SalaryPeriod.Month), (job.SalaryCurrency, job.SalaryPeriod));
        Assert.Empty(job.Skills);
    }

    [Fact]
    public async Task Create_WithUnknownCategory_ReturnsBadRequestWithMessage()
    {
        var create = await PostAsync(j => j.CategoryId = 987654);
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal("Category not found", await create.ReadMessageAsync());

        var job = await _api.CreateJobAsync();
        using var admin = await _api.AdminClientAsync();
        var update = await admin.PutJsonAsync($"/api/jobs/{job.Id}", UpdateFrom(job, j => j.CategoryId = 987654));
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        Assert.Equal("Category not found", await update.ReadMessageAsync());
    }

    [Theory]
    [InlineData("""{"seniority":9}""")]
    [InlineData("""{"contractTypes":32}""")]
    [InlineData("""{"contractTypes":-1}""")]
    [InlineData("""{"salaryCurrency":9}""")]
    [InlineData("""{"salaryPeriod":9}""")]
    public async Task Create_WithOutOfRangeEnumValues_ReturnsBadRequest(string extraJson)
    {
        using var admin = await _api.AdminClientAsync();
        var json = $$"""
            {"title":"T","companyId":{{_api.DefaultCompanyId}},"description":"D","location":"L","workType":0,{{extraJson.Trim('{', '}')}}}
            """;

        var response = await admin.PostRawJsonAsync("/api/jobs", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- pracodawca ---

    [Fact]
    public async Task Employer_CanSetTaxonomyOnOwnOffers_AndCannotTouchForeignOnes()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var foreign = await _api.RegisterApprovedEmployerAsync();
        var category = await _api.CreateCategoryAsync();
        var token = TaxonomyScenario.Token();

        var own = await _api.CreateEmployerJobAsync(employer, j =>
        {
            j.CategoryId = category.Id; j.Seniority = SeniorityLevel.Senior; j.ContractTypes = ContractTypes.B2B;
            j.Skills = [$"Employer skill {token}"];
        });
        Assert.Equal(category.Id, own.CategoryId);
        Assert.Equal(new[] { $"Employer skill {token}" }, own.Skills);

        using var employerClient = _api.CreateClient(employer);
        var edited = await employerClient.PutJsonAsync($"/api/jobs/{own.Id}", UpdateFrom(own, j => j.Skills = [$"Other {token}"]));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(new[] { $"Other {token}" }, (await edited.ReadJsonAsync<JobOfferDto>()).Skills);

        using var foreignClient = _api.CreateClient(foreign);
        var attempt = await foreignClient.PutJsonAsync($"/api/jobs/{own.Id}", UpdateFrom(own, j => j.CategoryId = null));
        Assert.Equal(HttpStatusCode.NotFound, attempt.StatusCode);

        var unchanged = await (await employerClient.GetAsync($"/api/jobs/{own.Id}")).ReadJsonAsync<JobOfferDto>();
        Assert.Equal(category.Id, unchanged.CategoryId);

        // lista zarządzania pracodawcy filtruje taksonomię tylko w jego ofertach
        var (_, mine) = await _api.GetManagedAsync(employerClient, TaxonomyScenario.Query(("category", category.Slug)));
        Assert.Equal(new[] { own.Id }, mine.Select(m => m.Id).ToArray());
        var (_, theirs) = await _api.GetManagedAsync(foreignClient, TaxonomyScenario.Query(("category", category.Slug)));
        Assert.Empty(theirs);
    }

    // --- podpowiedzi umiejętności ---

    [Fact]
    public async Task Skills_SuggestsPublicSkillsByPopularity_PrefixFirst()
    {
        var token = "q" + TaxonomyScenario.Token();
        await _api.CreateJobAsync(j => j.Skills = [token, "x-" + token]);
        await _api.CreateJobAsync(j => j.Skills = [token, "x-" + token]);
        await _api.CreateJobAsync(j => j.Skills = [token]);
        await _api.CreateJobAsync(j => j.Skills = ["x-" + token + "-y"]);
        // szkic nie zwiększa liczników, a umiejętność tylko z niego nie jest podpowiadana
        await _api.CreateJobWithStatusAsync(JobOfferStatus.Draft, customize: j => j.Skills = ["x-" + token + "-y", "hidden-" + token]);
        using var client = _api.CreateClient();

        var response = await client.GetAsync($"/api/skills?search={token.ToUpperInvariant()}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var skills = await response.ReadJsonAsync<SkillSuggestionDto[]>();

        // prefiks na początku, potem fragmenty od najpopularniejszych
        Assert.Equal(
            new[] { (token, 3), ("x-" + token, 2), ("x-" + token + "-y", 1) },
            skills.Select(s => (s.Name, s.JobsCount)).ToArray());

        var limited = await (await client.GetAsync($"/api/skills?search={token}&limit=1")).ReadJsonAsync<SkillSuggestionDto[]>();
        Assert.Single(limited);
    }

    [Fact]
    public async Task Skills_WithoutSearch_ReturnsMostPopularAndValidatesParameters()
    {
        using var client = _api.CreateClient();

        var response = await client.GetAsync("/api/skills");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.InRange((await response.ReadJsonAsync<SkillSuggestionDto[]>()).Length, 1, 10);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/skills?limit=20")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/skills?limit=21")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/skills?limit=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/skills?search=" + new string('a', 51))).StatusCode);
    }
}
