using Microsoft.EntityFrameworkCore;

namespace CantinaApi.Common;

public static class QueryableExtensions
{
    // The query must already be ordered so pages are stable.
    public static async Task<PagedResponse<T>> ToPagedResponseAsync<T>(
        this IQueryable<T> query, PageRequest page, CancellationToken cancellationToken)
    {
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalCount / (double)page.PageSize);

        return new PagedResponse<T>(items, page.Page, page.PageSize, totalCount, totalPages);
    }
}
