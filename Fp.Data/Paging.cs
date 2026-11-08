using Microsoft.EntityFrameworkCore;

namespace Fp.Data;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}

public static class QueryableExtensions
{
    public const int MaxPageSize = 1000;

    /// <summary>
    /// Returns one page (1-based, page size 1 to <see cref="MaxPageSize"/>). The query must be ordered to get stable pages.
    /// </summary>
    public static async Task<PagedResult<T>> ToPagedAsync<T>(
        this IOrderedQueryable<T> query, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, MaxPageSize);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<T>(items, page, pageSize, total);
    }
}
