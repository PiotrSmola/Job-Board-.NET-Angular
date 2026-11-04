using System.Text.Json;
using Backend.Helpers;
using Microsoft.AspNetCore.Http;

namespace Backend.Tests.Unit;

public class PaginationParamsTests
{
    [Fact]
    public void Defaults_AreFirstPageWithTenItems()
    {
        var parameters = new JobParams();

        Assert.Equal(1, parameters.PageNumber);
        Assert.Equal(10, parameters.PageSize);
        Assert.Equal(10, PaginationParams.DefaultPageSize);
        Assert.Equal(50, PaginationParams.MaxPageSize);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(int.MinValue, 1)]
    public void PageNumber_IsNormalizedToAtLeastOne(int input, int expected)
    {
        var parameters = new ApplicationParams { PageNumber = input };

        Assert.Equal(expected, parameters.PageNumber);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(25, 25)]
    [InlineData(50, 50)]
    [InlineData(51, 50)]
    [InlineData(1000, 50)]
    [InlineData(int.MaxValue, 50)]
    [InlineData(0, 10)]
    [InlineData(-5, 10)]
    public void PageSize_IsClampedToMaximumAndFallsBackToDefault(int input, int expected)
    {
        var parameters = new JobParams { PageSize = input };

        Assert.Equal(expected, parameters.PageSize);
    }

    [Fact]
    public void JobParams_DefaultOrderingIsNewestWithNoFilters()
    {
        var parameters = new JobParams();

        Assert.Equal("newest", parameters.OrderBy);
        Assert.Null(parameters.SearchTerm);
        Assert.Null(parameters.WorkType);
        Assert.Null(parameters.MinSalary);
        Assert.Null(parameters.MaxSalary);
        Assert.Null(parameters.Location);
    }

    [Fact]
    public void ApplicationParams_DefaultOrderingIsNewestWithNoFilters()
    {
        var parameters = new ApplicationParams();

        Assert.Equal("newest", parameters.OrderBy);
        Assert.Null(parameters.SearchTerm);
        Assert.Null(parameters.JobOfferId);
    }
}

public class PagedListTests
{
    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(20, 10, 2)]
    [InlineData(21, 10, 3)]
    [InlineData(7, 3, 3)]
    public void Constructor_CalculatesTotalPagesFromCountAndPageSize(int count, int pageSize, int expectedPages)
    {
        var list = new PagedList<int>(new List<int>(), count, pageNumber: 1, pageSize: pageSize);

        Assert.Equal(expectedPages, list.TotalPages);
        Assert.Equal(count, list.TotalCount);
        Assert.Equal(pageSize, list.PageSize);
    }

    [Fact]
    public void Constructor_ExposesItemsAndPageMetadata()
    {
        var list = new PagedList<string>(new List<string> { "a", "b" }, count: 12, pageNumber: 3, pageSize: 5);

        Assert.Equal(new[] { "a", "b" }, list);
        Assert.Equal(3, list.CurrentPage);
        Assert.Equal(3, list.TotalPages);
    }

    [Theory]
    [InlineData(1, 3, false, true)]
    [InlineData(2, 3, true, true)]
    [InlineData(3, 3, true, false)]
    [InlineData(1, 0, false, false)]
    public void HasPreviousAndHasNext_DependOnCurrentPageAndTotalPages(int page, int totalPages, bool hasPrevious, bool hasNext)
    {
        var count = totalPages * 10;
        var list = new PagedList<int>(new List<int>(), count, page, pageSize: 10);

        Assert.Equal(hasPrevious, list.HasPrevious);
        Assert.Equal(hasNext, list.HasNext);
    }
}

public class PaginationHeaderTests
{
    [Fact]
    public void PaginationHeader_StoresValues()
    {
        var header = new PaginationHeader(2, 10, 25, 3);

        Assert.Equal(2, header.CurrentPage);
        Assert.Equal(10, header.ItemsPerPage);
        Assert.Equal(25, header.TotalItems);
        Assert.Equal(3, header.TotalPages);
    }

    [Fact]
    public void AddPaginationHeader_WritesJsonWithContractFieldNames()
    {
        var context = new DefaultHttpContext();
        var list = new PagedList<int>(new List<int> { 1, 2 }, count: 25, pageNumber: 2, pageSize: 10);

        context.Response.AddPaginationHeader(list);

        var raw = Assert.Single(context.Response.Headers["Pagination"]);
        using var json = JsonDocument.Parse(raw!);
        Assert.Equal(2, json.RootElement.GetProperty("CurrentPage").GetInt32());
        Assert.Equal(10, json.RootElement.GetProperty("ItemsPerPage").GetInt32());
        Assert.Equal(25, json.RootElement.GetProperty("TotalItems").GetInt32());
        Assert.Equal(3, json.RootElement.GetProperty("TotalPages").GetInt32());
        Assert.Equal(4, json.RootElement.EnumerateObject().Count());
    }
}
