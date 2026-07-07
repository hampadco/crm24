using Microsoft.EntityFrameworkCore;
using Crm.Web.Models;

namespace Crm.Web.Services;

public static class PagingExtensions
{
    public static async Task<ListingPage<T>> ToPagedListAsync<T>(
        this IQueryable<T> query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize switch
        {
            < 1 => ListingDefaults.PageSize,
            > 100 => 100,
            _ => pageSize
        };

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

        return new ListingPage<T>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages
        };
    }
}
