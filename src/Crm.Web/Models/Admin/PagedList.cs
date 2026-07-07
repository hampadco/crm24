namespace Crm.Web.Models.Admin;

public class PagedList<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
    public string? Search { get; init; }

    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;

    public int FromIndex => TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int ToIndex => TotalCount == 0 ? 0 : Math.Min(Page * PageSize, TotalCount);

    public static PagedList<T> Empty(ContentListQuery query) => new()
    {
        Page = query.NormalizedPage,
        PageSize = query.NormalizedPageSize,
        Search = query.Search
    };
}
