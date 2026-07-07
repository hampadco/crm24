namespace Crm.Web.Models;

public class ListingPage<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 12;
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }

    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;

    public int FromIndex => TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int ToIndex => TotalCount == 0 ? 0 : Math.Min(Page * PageSize, TotalCount);

    public static ListingPage<T> Empty(int page = 1, int pageSize = 12) => new()
    {
        Page = page < 1 ? 1 : page,
        PageSize = pageSize,
        TotalCount = 0,
        TotalPages = 0
    };
}

public class PaginationViewModel
{
    public required string ControllerName { get; init; }
    public string ActionName { get; init; } = "Index";
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
    public int FromIndex { get; init; }
    public int ToIndex { get; init; }
    public Dictionary<string, string?> RouteValues { get; init; } = new();

    public Dictionary<string, string?> RouteForPage(int targetPage)
    {
        var route = new Dictionary<string, string?>(RouteValues, StringComparer.OrdinalIgnoreCase)
        {
            ["page"] = targetPage.ToString()
        };
        return route;
    }
}

public static class ListingDefaults
{
    public const int PageSize = 12;
}
