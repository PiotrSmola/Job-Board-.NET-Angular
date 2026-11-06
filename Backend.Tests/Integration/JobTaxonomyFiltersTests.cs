using System.Net;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Filtry taksonomii na /api/jobs i /api/jobs/manage oraz liczniki /api/jobs/facets. Dane seeda są współdzielone,
// więc każdy test tworzy własne oferty z unikalną umiejętnością-tokenem i zawęża do niej wyniki (skills=token).
[Collection(ApiCollection.Name)]
public class JobTaxonomyFiltersTests
{
    private readonly ApiFixture _api;

    public JobTaxonomyFiltersTests(ApiFixture api) => _api = api;

    private sealed record Scenario(
        string Token, string SkillA, string SkillB, CategoryDto CategoryA, CategoryDto CategoryB, JobOfferDto[] Jobs);

    // Cztery oferty wspólnego tokenu:
    //  J1  kat. A, Senior, B2B,                 zdalna,      PLN, [token, a-]
    //  J2  kat. A, Junior, Etat|B2B,            hybrydowa,   EUR, [token, b-]
    //  J3  kat. B, Senior, Zlecenie,            zdalna,      PLN, [token, a-, b-]
    //  J4  bez kat., bez poziomu i umów,        stacjonarna, USD, [token]
    private async Task<Scenario> CreateScenarioAsync()
    {
        var token = "t" + TaxonomyScenario.Token();
        var skillA = "a-" + token;
        var skillB = "b-" + token;
        var categoryA = await _api.CreateCategoryAsync();
        var categoryB = await _api.CreateCategoryAsync();

        var jobs = new[]
        {
            await _api.CreateJobAsync(j =>
            {
                j.CategoryId = categoryA.Id; j.Seniority = SeniorityLevel.Senior; j.ContractTypes = ContractTypes.B2B;
                j.WorkType = WorkType.Remote; j.SalaryCurrency = Currency.PLN; j.Skills = [token, skillA];
            }),
            await _api.CreateJobAsync(j =>
            {
                j.CategoryId = categoryA.Id; j.Seniority = SeniorityLevel.Junior;
                j.ContractTypes = ContractTypes.EmploymentContract | ContractTypes.B2B;
                j.WorkType = WorkType.Hybrid; j.SalaryCurrency = Currency.EUR; j.Skills = [token, skillB];
            }),
            await _api.CreateJobAsync(j =>
            {
                j.CategoryId = categoryB.Id; j.Seniority = SeniorityLevel.Senior; j.ContractTypes = ContractTypes.MandateContract;
                j.WorkType = WorkType.Remote; j.SalaryCurrency = Currency.PLN; j.Skills = [token, skillA, skillB];
            }),
            await _api.CreateJobAsync(j =>
            {
                j.WorkType = WorkType.Onsite; j.SalaryCurrency = Currency.USD; j.Skills = [token];
            })
        };

        return new Scenario(token, skillA, skillB, categoryA, categoryB, jobs);
    }

    private async Task<int[]> ListIdsAsync(Scenario s, params (string Name, string? Value)[] filters)
    {
        // oferty zawsze zawężone do tokena scenariusza; dodatkowy filtr skills jest dopisywany do jego listy
        var skills = string.Join(',', new[] { s.Token }.Concat(filters.Where(f => f.Name == "skills").Select(f => f.Value!)));
        var query = TaxonomyScenario.Query([("skills", skills), .. filters.Where(f => f.Name != "skills")]);
        var (_, items) = await _api.GetJobsAsync(query);
        return items.Select(i => i.Id).Order().ToArray();
    }

    private static int[] Ids(Scenario s, params int[] jobNumbers) =>
        jobNumbers.Select(n => s.Jobs[n - 1].Id).Order().ToArray();

    // --- filtry ---

    [Fact]
    public async Task Jobs_ReturnTaxonomyFieldsAndSortedSkills()
    {
        var s = await CreateScenarioAsync();

        var (_, items) = await _api.GetJobsAsync(TaxonomyScenario.Query(("skills", s.Token), ("category", s.CategoryB.Slug)));

        var job = Assert.Single(items);
        Assert.Equal(s.Jobs[2].Id, job.Id);
        Assert.Equal(s.CategoryB.Id, job.CategoryId);
        Assert.Equal(s.CategoryB.Name, job.CategoryName);
        Assert.Equal(s.CategoryB.Slug, job.CategorySlug);
        Assert.Equal(SeniorityLevel.Senior, job.Seniority);
        Assert.Equal(ContractTypes.MandateContract, job.ContractTypes);
        Assert.Equal(Currency.PLN, job.SalaryCurrency);
        Assert.Equal(SalaryPeriod.Month, job.SalaryPeriod);
        Assert.Equal(new[] { s.SkillA, s.SkillB, s.Token }, job.Skills);
    }

    [Fact]
    public async Task Jobs_CombinedFilters_AreJoinedWithAnd()
    {
        var s = await CreateScenarioAsync();

        Assert.Equal(Ids(s, 1), await ListIdsAsync(s, ("category", s.CategoryA.Slug), ("seniority", "3")));
        Assert.Equal(Ids(s, 1), await ListIdsAsync(s, ("categoryId", s.CategoryA.Id.ToString()), ("contractTypes", "2"), ("workType", "0")));
        Assert.Equal(Ids(s), await ListIdsAsync(s, ("category", s.CategoryB.Slug), ("seniority", "1")));
        // kategoria i categoryId naraz = oba warunki
        Assert.Equal(Ids(s), await ListIdsAsync(s, ("category", s.CategoryA.Slug), ("categoryId", s.CategoryB.Id.ToString())));
    }

    [Fact]
    public async Task Jobs_SeniorityAndWorkTypeLists_MatchAnyValue()
    {
        var s = await CreateScenarioAsync();

        Assert.Equal(Ids(s, 1, 2, 3), await ListIdsAsync(s, ("seniority", "1,3")));
        Assert.Equal(Ids(s, 1, 2, 3), await ListIdsAsync(s, ("workType", "0,1")));
        Assert.Equal(Ids(s, 4), await ListIdsAsync(s, ("workType", "2")));
        Assert.Equal(Ids(s, 4), await ListIdsAsync(s, ("workType", "Onsite")));
        Assert.Equal(Ids(s, 2), await ListIdsAsync(s, ("seniority", "1,1,")));
    }

    [Theory]
    [InlineData("2", new[] { 1, 2 })]
    [InlineData("1", new[] { 2 })]
    [InlineData("6", new[] { 1, 2, 3 })]
    [InlineData("3", new[] { 1, 2 })]
    [InlineData("8", new int[0])]
    [InlineData("0", new[] { 1, 2, 3, 4 })]
    public async Task Jobs_ContractTypesMask_MatchesAnySelectedType(string mask, int[] expectedJobs)
    {
        var s = await CreateScenarioAsync();

        Assert.Equal(Ids(s, expectedJobs), await ListIdsAsync(s, ("contractTypes", mask)));
    }

    [Fact]
    public async Task Jobs_CurrencyFilter_TreatsPlnAsARealFilter()
    {
        var s = await CreateScenarioAsync();

        Assert.Equal(Ids(s, 1, 3), await ListIdsAsync(s, ("currency", "0")));
        Assert.Equal(Ids(s, 2), await ListIdsAsync(s, ("currency", "1")));
        Assert.Equal(Ids(s, 1, 2, 3, 4), await ListIdsAsync(s));
    }

    [Fact]
    public async Task Jobs_SkillsFilter_RequiresAllSkillsIgnoringCase()
    {
        var s = await CreateScenarioAsync();

        Assert.Equal(Ids(s, 1, 3), await ListIdsAsync(s, ("skills", s.SkillA)));
        Assert.Equal(Ids(s, 3), await ListIdsAsync(s, ("skills", $"{s.SkillA.ToUpperInvariant()}, {s.SkillB}")));
        Assert.Equal(Ids(s), await ListIdsAsync(s, ("skills", s.SkillA + ",no-such-skill-" + s.Token)));
    }

    [Fact]
    public async Task Jobs_SalaryRangeFilter_StillWorksTogetherWithTaxonomy()
    {
        var s = await CreateScenarioAsync();

        // widełki z NewJobDto: 5000-8000 (w walucie oferty); z obiema granicami oferta musi się w nich mieścić
        Assert.Equal(Ids(s, 1, 3), await ListIdsAsync(s, ("currency", "0"), ("minSalary", "4000"), ("maxSalary", "9000")));
        Assert.Equal(Ids(s), await ListIdsAsync(s, ("currency", "0"), ("minSalary", "9000")));
    }

    [Fact]
    public async Task Jobs_AreOnlyTheListedOnes_DraftsStayOutOfPublicFilters()
    {
        var s = await CreateScenarioAsync();
        await _api.CreateJobWithStatusAsync(JobOfferStatus.Draft, customize: j =>
        {
            j.CategoryId = s.CategoryA.Id; j.Skills = [s.Token];
        });

        Assert.Equal(Ids(s, 1, 2), await ListIdsAsync(s, ("category", s.CategoryA.Slug)));

        // panel zarządzania widzi też szkice
        using var admin = await _api.AdminClientAsync();
        var (_, managed) = await _api.GetManagedAsync(admin, TaxonomyScenario.Query(("skills", s.Token), ("category", s.CategoryA.Slug)));
        Assert.Equal(3, managed.Length);
    }

    [Fact]
    public async Task Manage_AppliesTheSameTaxonomyFilters()
    {
        var s = await CreateScenarioAsync();
        using var admin = await _api.AdminClientAsync();

        var (_, items) = await _api.GetManagedAsync(admin,
            TaxonomyScenario.Query(("skills", s.Token), ("seniority", "3"), ("contractTypes", "4"), ("workType", "0,1")));

        Assert.Equal(new[] { s.Jobs[2].Id }, items.Select(i => i.Id).ToArray());
    }

    // --- walidacja parametrów ---

    [Theory]
    [InlineData("workType=9")]
    [InlineData("workType=abc")]
    [InlineData("workType=-1")]
    [InlineData("seniority=6")]
    [InlineData("seniority=Boss")]
    [InlineData("contractTypes=32")]
    [InlineData("contractTypes=-1")]
    [InlineData("currency=9")]
    [InlineData("category=Has%20Space")]
    [InlineData("category=UPPER_case")]
    public async Task Jobs_InvalidFilterValues_ReturnBadRequest(string query)
    {
        using var client = _api.CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/jobs?" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/jobs/facets?" + query)).StatusCode);
    }

    [Fact]
    public async Task Jobs_TooLongOrTooManyListValues_ReturnBadRequest()
    {
        using var client = _api.CreateClient();
        var elevenSkills = string.Join(',', Enumerable.Range(1, 11).Select(i => "skill" + i));
        var longSkill = new string('x', 51);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/jobs?skills=" + elevenSkills)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/jobs?skills=" + longSkill)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/jobs?category=" + new string('a', 101))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/jobs?searchTerm=" + new string('a', 201))).StatusCode);
        // dokładnie na granicy - poprawne
        var tenSkills = string.Join(',', Enumerable.Range(1, 10).Select(i => new string('x', 50 - 2) + i.ToString("D2")));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/jobs?skills=" + tenSkills)).StatusCode);
    }

    // --- facety ---

    private static Dictionary<int, int> Counts(IEnumerable<FacetValueDto> values) => values.ToDictionary(v => v.Value, v => v.Count);

    [Fact]
    public async Task Facets_WithoutOtherFilters_CountEveryValue()
    {
        var s = await CreateScenarioAsync();

        var facets = await _api.GetFacetsAsync(TaxonomyScenario.Query(("skills", s.Token)));

        Assert.Equal(4, facets.Total);
        Assert.Equal(2, facets.Categories.Single(c => c.Id == s.CategoryA.Id).Count);
        Assert.Equal(1, facets.Categories.Single(c => c.Id == s.CategoryB.Id).Count);
        Assert.Equal(s.CategoryB.Slug, facets.Categories.Single(c => c.Id == s.CategoryB.Id).Slug);
        Assert.Equal(new Dictionary<int, int> { [1] = 1, [3] = 2 }, Counts(facets.Seniority));
        // pojedyncze bity: Etat (1) w J2, B2B (2) w J1 i J2, Zlecenie (4) w J3
        Assert.Equal(new Dictionary<int, int> { [1] = 1, [2] = 2, [4] = 1 }, Counts(facets.ContractTypes));
        Assert.Equal(new Dictionary<int, int> { [0] = 2, [1] = 1, [2] = 1 }, Counts(facets.WorkTypes));

        // faseta umiejętności pomija filtr umiejętności, więc do wyodrębnienia ofert scenariusza służy kategoria A (J1, J2)
        var skills = (await _api.GetFacetsAsync(TaxonomyScenario.Query(("category", s.CategoryA.Slug)))).Skills;
        Assert.Equal(2, skills.Single(k => k.Name == s.Token).Count);
        Assert.Equal(1, skills.Single(k => k.Name == s.SkillA).Count);
        Assert.Equal(1, skills.Single(k => k.Name == s.SkillB).Count);
    }

    [Fact]
    public async Task Facets_EachFacetSkipsItsOwnFilterButAppliesTheOthers()
    {
        var s = await CreateScenarioAsync();

        // wybrane: kategoria A i poziom Senior -> wynik to wyłącznie J1
        var facets = await _api.GetFacetsAsync(TaxonomyScenario.Query(
            ("skills", s.Token), ("category", s.CategoryA.Slug), ("seniority", "3")));

        Assert.Equal(1, facets.Total);
        // kategorie: bez filtra kategorii, z filtrem poziomu -> J1 (A) i J3 (B)
        Assert.Equal(new[] { 1, 1 }, facets.Categories.Where(c => c.Id == s.CategoryA.Id || c.Id == s.CategoryB.Id).Select(c => c.Count).ToArray());
        // poziomy: bez filtra poziomu, z filtrem kategorii A -> Junior (J2) i Senior (J1)
        Assert.Equal(new Dictionary<int, int> { [1] = 1, [3] = 1 }, Counts(facets.Seniority));
        // umowy i tryby pracy: oba filtry -> tylko J1 (B2B, zdalna)
        Assert.Equal(new Dictionary<int, int> { [2] = 1 }, Counts(facets.ContractTypes));
        Assert.Equal(new Dictionary<int, int> { [0] = 1 }, Counts(facets.WorkTypes));
    }

    [Fact]
    public async Task Facets_SkillsFacetSkipsTheSkillsFilter_AndZeroCountsAreOmitted()
    {
        var s = await CreateScenarioAsync();

        // filtr umiejętności "a-" zostawia J1 i J3, ale liczniki umiejętności liczą oferty bez tego filtra
        var facets = await _api.GetFacetsAsync(TaxonomyScenario.Query(("skills", s.SkillA), ("category", s.CategoryA.Slug)));

        Assert.Equal(1, facets.Total);
        // oferty kategorii A (J1, J2): umiejętność b- występuje w J2 mimo filtra a-
        Assert.Equal(1, facets.Skills.Single(k => k.Name == s.SkillB).Count);
        Assert.Equal(1, facets.Skills.Single(k => k.Name == s.SkillA).Count);
        Assert.DoesNotContain(facets.ContractTypes, c => c.Count == 0);
        Assert.DoesNotContain(facets.Categories, c => c.Count == 0);
    }

    [Fact]
    public async Task Facets_ReturnAtMostTwentySkills_AndNothingForEmptyResults()
    {
        var s = await CreateScenarioAsync();
        await _api.CreateJobAsync(j => { j.CategoryId = s.CategoryA.Id; j.Skills = Enumerable.Range(1, 15).Select(i => $"x{i:D2}-{s.Token}").ToList(); });
        await _api.CreateJobAsync(j => { j.CategoryId = s.CategoryA.Id; j.Skills = Enumerable.Range(16, 15).Select(i => $"x{i:D2}-{s.Token}").ToList(); });

        var facets = await _api.GetFacetsAsync(TaxonomyScenario.Query(("category", s.CategoryA.Slug)));
        Assert.Equal(20, facets.Skills.Count);

        var empty = await _api.GetFacetsAsync(TaxonomyScenario.Query(("skills", "no-such-skill-" + s.Token)));
        Assert.Equal(0, empty.Total);
        Assert.Empty(empty.Seniority);
        Assert.Empty(empty.ContractTypes);
        Assert.Empty(empty.WorkTypes);
    }

    [Fact]
    public async Task Facets_IsPublic_AndIgnoresPaging()
    {
        var s = await CreateScenarioAsync();

        var facets = await _api.GetFacetsAsync(TaxonomyScenario.Query(("skills", s.Token), ("pageSize", "1"), ("pageNumber", "3"), ("orderBy", "oldest")));

        Assert.Equal(4, facets.Total);
    }
}
