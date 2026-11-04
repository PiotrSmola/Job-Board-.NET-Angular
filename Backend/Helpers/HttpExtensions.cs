using System.Text.Json;

namespace Backend.Helpers;

public static class HttpExtensions
{
    // Dodaje nagłówek Pagination (CurrentPage, ItemsPerPage, TotalItems, TotalPages)
    public static void AddPaginationHeader<T>(this HttpResponse response, PagedList<T> pagedList)
    {
        var paginationHeader = new PaginationHeader(
            pagedList.CurrentPage,
            pagedList.PageSize,
            pagedList.TotalCount,
            pagedList.TotalPages
        );

        response.Headers.Append("Pagination", JsonSerializer.Serialize(paginationHeader));
    }
}
