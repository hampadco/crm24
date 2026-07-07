namespace Crm.Web.Models;

public class ContentFilterBarViewModel
{
    public required string ControllerName { get; init; }
    public IReadOnlyList<ContentCategory> Categories { get; init; } = Array.Empty<ContentCategory>();
    public IReadOnlyList<ContentTag> Tags { get; init; } = Array.Empty<ContentTag>();
    public bool ShowTags { get; init; } = true;
    public string? SelectedCategorySlug { get; init; }
    public string? SelectedTagSlug { get; init; }
    public string? Search { get; init; }
    public int Page { get; init; } = 1;

    public Dictionary<string, string?> BuildRouteValues(int page)
    {
        var route = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["page"] = page.ToString()
        };

        if (!string.IsNullOrWhiteSpace(Search))
            route["search"] = Search;

        if (!string.IsNullOrWhiteSpace(SelectedCategorySlug))
            route["category"] = SelectedCategorySlug;

        if (!string.IsNullOrWhiteSpace(SelectedTagSlug))
            route["tag"] = SelectedTagSlug;

        return route;
    }
}

public class ArticleListingViewModel
{
    public ListingPage<Article> Results { get; init; } = ListingPage<Article>.Empty();
    public ContentFilterBarViewModel Filters { get; init; } = null!;

    public PaginationViewModel Pagination => BuildPagination("Articles");

    private PaginationViewModel BuildPagination(string controller) => new()
    {
        ControllerName = controller,
        Page = Results.Page,
        TotalPages = Results.TotalPages,
        TotalCount = Results.TotalCount,
        FromIndex = Results.FromIndex,
        ToIndex = Results.ToIndex,
        RouteValues = Filters.BuildRouteValues(Results.Page)
    };
}

public class FaqListingViewModel
{
    public IReadOnlyList<FaqItem> Items { get; init; } = Array.Empty<FaqItem>();
}

public record ContentSidebarItem(string Title, string Slug, string? ThumbnailUrl, DateTime? PublishedAt = null);

public class ContentDetailRelatedSectionsModel
{
    public required string ControllerName { get; init; }
    public string? DetailRouteName { get; init; }
    public required string IndexActionLabel { get; init; }
    public required string RelatedTitle { get; init; }
    public required string LatestTitle { get; init; }
    public required string RelatedIcon { get; init; }
    public required string LatestIcon { get; init; }
    public IReadOnlyList<ContentSidebarItem> Related { get; init; } = Array.Empty<ContentSidebarItem>();
    public IReadOnlyList<ContentSidebarItem> Latest { get; init; } = Array.Empty<ContentSidebarItem>();
}

public class ArticleDetailViewModel
{
    public required Article Article { get; init; }
    public IReadOnlyList<ContentSidebarItem> Related { get; init; } = Array.Empty<ContentSidebarItem>();
    public IReadOnlyList<ContentSidebarItem> Latest { get; init; } = Array.Empty<ContentSidebarItem>();
}
