using System.Net;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Publiczna lista kategorii i zarządzanie nimi przez admina (CRUD, unikalność, usuwanie kategorii z ofertami, uprawnienia)
[Collection(ApiCollection.Name)]
public class CategoriesTests
{
    private const string TakenMessage = "Category name or slug is already taken";
    private const string NotFoundMessage = "Category not found";

    private readonly ApiFixture _api;

    public CategoriesTests(ApiFixture api) => _api = api;

    private static CategoryFormDto Form(string token, int sortOrder = 0) =>
        new() { Name = "Kategoria " + token, Slug = "kat-" + token, SortOrder = sortOrder };

    private async Task<CategoryDto[]> ListAsync()
    {
        using var client = _api.CreateClient();
        var response = await client.GetAsync("/api/categories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<CategoryDto[]>();
    }

    // --- lista publiczna ---

    [Fact]
    public async Task List_IsPublic_ContainsTheSeededCategories_AndIsSortedBySortOrderThenName()
    {
        var categories = await ListAsync();

        Assert.Contains(categories, c => c.Slug == "programowanie" && c.Name == "Programowanie");
        Assert.Equal(
            categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name, StringComparer.Ordinal).Select(c => c.SortOrder).ToArray(),
            categories.Select(c => c.SortOrder).ToArray());
    }

    [Fact]
    public async Task List_CountsOnlyOffersVisibleOnTheListing()
    {
        var category = await _api.CreateCategoryAsync();
        await _api.CreateJobAsync(j => j.CategoryId = category.Id);
        await _api.CreateJobWithStatusAsync(JobOfferStatus.Draft, customize: j => j.CategoryId = category.Id);
        await _api.CreateJobWithStatusAsync(JobOfferStatus.Closed, customize: j => j.CategoryId = category.Id);
        await _api.CreateExpiredJobAsync(j => j.CategoryId = category.Id);
        var pendingCompany = _api.CompanyId("Pending taxonomy " + TaxonomyScenario.Token(), CompanyStatus.Pending);
        // oferta aktywna, ale firma niezatwierdzona - niewidoczna publicznie
        await _api.CreateJobAsync(j => { j.CompanyId = pendingCompany; j.CategoryId = category.Id; });

        var listed = (await ListAsync()).Single(c => c.Id == category.Id);

        Assert.Equal(1, listed.ActiveJobsCount);
        // ten sam licznik co faseta kategorii
        var facetCategory = (await _api.GetFacetsAsync(string.Empty)).Categories.Single(c => c.Id == category.Id);
        Assert.Equal(listed.ActiveJobsCount, facetCategory.Count);
    }

    [Fact]
    public async Task Get_ReturnsOneCategory_OrNotFound()
    {
        var category = await _api.CreateCategoryAsync();
        using var client = _api.CreateClient();

        var found = await client.GetAsync($"/api/categories/{category.Id}");
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal(category.Slug, (await found.ReadJsonAsync<CategoryDto>()).Slug);

        var missing = await client.GetAsync("/api/categories/987654");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(NotFoundMessage, await missing.ReadMessageAsync());
    }

    [Fact]
    public async Task Seed_CreatesTheDemoCategoriesAndTaxonomyForDemoOffers()
    {
        var slugs = (await ListAsync()).Select(c => c.Slug).ToHashSet();
        Assert.Superset(
            new HashSet<string>
            {
                "programowanie", "devops-i-chmura", "testowanie", "analiza-danych",
                "design-ux-ui", "zarzadzanie-projektami", "bezpieczenstwo-it", "wsparcie-it"
            },
            slugs);

        var (_, items) = await _api.GetJobsAsync(TaxonomyScenario.Query(("searchTerm", "Senior .NET Developer")));
        var offer = items.First(i => i.Title == "Senior .NET Developer");
        Assert.Equal("programowanie", offer.CategorySlug);
        Assert.Equal(SeniorityLevel.Senior, offer.Seniority);
        Assert.Equal(ContractTypes.EmploymentContract | ContractTypes.B2B, offer.ContractTypes);
        Assert.Contains(".NET", offer.Skills);
    }

    // --- tworzenie ---

    [Fact]
    public async Task Create_AsAdmin_ReturnsCreatedWithLocationAndNormalizedName()
    {
        var token = TaxonomyScenario.Token();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PostJsonAsync("/api/admin/categories",
            new CategoryFormDto { Name = $"  Nowa   kategoria {token} ", Slug = "nowa-" + token, SortOrder = 7 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var category = await response.ReadJsonAsync<CategoryDto>();
        Assert.Equal($"Nowa kategoria {token}", category.Name);
        Assert.Equal(("nowa-" + token, 7, 0), (category.Slug, category.SortOrder, category.ActiveJobsCount));
        Assert.EndsWith($"/api/Categories/{category.Id}", response.Headers.Location!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_WithTakenNameOrSlug_ReturnsBadRequest()
    {
        var existing = await _api.CreateCategoryAsync();
        var token = TaxonomyScenario.Token();
        using var admin = await _api.AdminClientAsync();

        // nazwa bez rozróżniania wielkości liter
        var sameName = await admin.PostJsonAsync("/api/admin/categories",
            new CategoryFormDto { Name = existing.Name.ToUpperInvariant(), Slug = "inny-" + token });
        Assert.Equal(HttpStatusCode.BadRequest, sameName.StatusCode);
        Assert.Equal(TakenMessage, await sameName.ReadMessageAsync());

        var sameSlug = await admin.PostJsonAsync("/api/admin/categories",
            new CategoryFormDto { Name = "Inna " + token, Slug = existing.Slug });
        Assert.Equal(HttpStatusCode.BadRequest, sameSlug.StatusCode);
        Assert.Equal(TakenMessage, await sameSlug.ReadMessageAsync());
    }

    [Theory]
    [InlineData("", "ok-slug", 0)]
    [InlineData("   ", "ok-slug", 0)]
    [InlineData("​", "ok-slug", 0)]
    [InlineData("Name", "", 0)]
    [InlineData("Name", "Upper-Case", 0)]
    [InlineData("Name", "with space", 0)]
    [InlineData("Name", "ząb", 0)]
    [InlineData("Name", "ok-slug", -1)]
    public async Task Create_WithInvalidFields_ReturnsBadRequest(string name, string slug, int sortOrder)
    {
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PostJsonAsync("/api/admin/categories", new CategoryFormDto { Name = name, Slug = slug, SortOrder = sortOrder });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithTooLongNameOrSlug_ReturnsBadRequest()
    {
        using var admin = await _api.AdminClientAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostJsonAsync("/api/admin/categories",
            new CategoryFormDto { Name = new string('n', 101), Slug = "ok" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostJsonAsync("/api/admin/categories",
            new CategoryFormDto { Name = "ok", Slug = new string('s', 101) })).StatusCode);
    }

    // --- edycja ---

    [Fact]
    public async Task Update_ChangesFields_AndAllowsKeepingItsOwnNameAndSlug()
    {
        var category = await _api.CreateCategoryAsync();
        var token = TaxonomyScenario.Token();
        using var admin = await _api.AdminClientAsync();

        var same = await admin.PutJsonAsync($"/api/admin/categories/{category.Id}",
            new CategoryFormDto { Name = category.Name, Slug = category.Slug, SortOrder = 5 });
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        Assert.Equal(5, (await same.ReadJsonAsync<CategoryDto>()).SortOrder);

        var renamed = await admin.PutJsonAsync($"/api/admin/categories/{category.Id}", Form(token, 9));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var dto = await renamed.ReadJsonAsync<CategoryDto>();
        Assert.Equal(($"Kategoria {token}", "kat-" + token, 9), (dto.Name, dto.Slug, dto.SortOrder));
        Assert.Equal("kat-" + token, (await ListAsync()).Single(c => c.Id == category.Id).Slug);
    }

    [Fact]
    public async Task Update_WithNameOrSlugOfAnotherCategory_ReturnsBadRequest()
    {
        var first = await _api.CreateCategoryAsync();
        var second = await _api.CreateCategoryAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PutJsonAsync($"/api/admin/categories/{second.Id}",
            new CategoryFormDto { Name = "Zupełnie inna " + TaxonomyScenario.Token(), Slug = first.Slug });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TakenMessage, await response.ReadMessageAsync());
    }

    [Fact]
    public async Task Update_OfAnUnknownCategory_ReturnsNotFoundWithMessage()
    {
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PutJsonAsync("/api/admin/categories/987654", Form(TaxonomyScenario.Token()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFoundMessage, await response.ReadMessageAsync());
    }

    // --- usuwanie ---

    [Fact]
    public async Task Delete_RemovesTheCategory_AndItsOffersLoseOnlyTheCategory()
    {
        var category = await _api.CreateCategoryAsync();
        var token = TaxonomyScenario.Token();
        var job = await _api.CreateJobAsync(j =>
        {
            j.CategoryId = category.Id; j.Seniority = SeniorityLevel.Senior; j.Skills = ["keeps " + token];
        });
        using var admin = await _api.AdminClientAsync();

        var response = await admin.DeleteAsync($"/api/admin/categories/{category.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(await ListAsync(), c => c.Id == category.Id);
        using var client = _api.CreateClient();
        var after = await (await client.GetAsync($"/api/jobs/{job.Id}")).ReadJsonAsync<JobOfferDto>();
        Assert.Null(after.CategoryId);
        Assert.Null(after.CategoryName);
        Assert.Equal(SeniorityLevel.Senior, after.Seniority);
        Assert.Equal(new[] { "keeps " + token }, after.Skills);

        var again = await admin.DeleteAsync($"/api/admin/categories/{category.Id}");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(NotFoundMessage, await again.ReadMessageAsync());
    }

    // --- uprawnienia ---

    [Fact]
    public async Task Write_Endpoints_RequireAnAdmin()
    {
        var category = await _api.CreateCategoryAsync();
        var employer = await _api.RegisterApprovedEmployerAsync();
        using var anonymous = _api.CreateClient();
        using var user = await _api.UserClientAsync();
        using var employerClient = _api.CreateClient(employer);

        foreach (var (client, expected) in new[]
                 {
                     (anonymous, HttpStatusCode.Unauthorized),
                     (user, HttpStatusCode.Forbidden),
                     (employerClient, HttpStatusCode.Forbidden)
                 })
        {
            Assert.Equal(expected, (await client.PostJsonAsync("/api/admin/categories", Form(TaxonomyScenario.Token()))).StatusCode);
            Assert.Equal(expected, (await client.PutJsonAsync($"/api/admin/categories/{category.Id}", Form(TaxonomyScenario.Token()))).StatusCode);
            Assert.Equal(expected, (await client.DeleteAsync($"/api/admin/categories/{category.Id}")).StatusCode);
        }

        Assert.Contains(await ListAsync(), c => c.Id == category.Id);
    }
}
