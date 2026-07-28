using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Models;

/// <summary>صفحه‌بندی مشترک لیست‌های پنل App.</summary>
public static class AppPaging
{
    public const int DefaultPageSize = 20;

    public static async Task<(List<T> Items, int TotalCount, int Page, int PageSize)> ToPageAsync<T>(
        IQueryable<T> query, int page, int pageSize = DefaultPageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total, page, pageSize);
    }

    public static void SetViewBag(dynamic viewBag, int totalCount, int page, int pageSize,
        IDictionary<string, string?>? extraRoutes = null)
    {
        viewBag.TotalCount = totalCount;
        viewBag.Page = page;
        viewBag.PageSize = pageSize;
        viewBag.TotalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        if (extraRoutes is not null)
            viewBag.PagingRoutes = extraRoutes;
    }
}
