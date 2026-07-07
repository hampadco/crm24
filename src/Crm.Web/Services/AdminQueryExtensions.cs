using Microsoft.EntityFrameworkCore;
using Crm.Web.Models.Admin;

namespace Crm.Web.Services;

public static class AdminQueryExtensions
{
    public static async Task<PagedList<T>> ToPagedListAsync<T>(
        this IQueryable<T> query,
        ContentListQuery listQuery,
        CancellationToken cancellationToken = default)
    {
        var page = listQuery.NormalizedPage;
        var pageSize = listQuery.NormalizedPageSize;

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        if (totalPages > 0 && page > totalPages)
            page = totalPages;

        IReadOnlyList<T> items;
        if (totalCount == 0)
        {
            items = Array.Empty<T>();
        }
        else
        {
            items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
        }

        return new PagedList<T>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            Search = listQuery.Search
        };
    }
}
