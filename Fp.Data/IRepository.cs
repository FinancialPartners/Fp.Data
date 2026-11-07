using System.Linq.Expressions;

namespace Fp.Data;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}

/// <summary>
/// Thin helper on top of EF Core. It never saves: call <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// You can always use the <c>DbContext</c> directly instead.
/// </summary>
public interface IRepository<T> where T : class
{
    /// <summary>Query without change tracking, for reading.</summary>
    IQueryable<T> Query();

    /// <summary>Query with change tracking, for loading entities you are going to change.</summary>
    IQueryable<T> QueryTracked();

    /// <summary>Finds an entity by its primary key, or <c>null</c>. Uses the change tracker first.</summary>
    Task<T?> FindAsync(object id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> ListAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page (1-based, page size 1 to 1000). An ordering is required to get stable pages.
    /// </summary>
    Task<PagedResult<T>> PagedAsync(
        int page,
        int pageSize,
        Func<IQueryable<T>, IOrderedQueryable<T>> orderBy,
        Expression<Func<T, bool>>? predicate = null,
        CancellationToken cancellationToken = default);

    void Add(T entity);

    void AddRange(IEnumerable<T> entities);

    /// <summary>Marks the entity (also a detached one) as modified.</summary>
    void Update(T entity);

    /// <summary>Deletes the entity, or soft deletes it when it implements <see cref="ISoftDelete"/>.</summary>
    void Remove(T entity);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
