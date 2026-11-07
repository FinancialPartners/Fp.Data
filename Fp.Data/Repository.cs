using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Fp.Data;

public class Repository<T>(DbContext context) : IRepository<T> where T : class
{
    private const int MaxPageSize = 1000;

    protected DbContext Context { get; } = context ?? throw new ArgumentNullException(nameof(context));

    protected DbSet<T> DbSet { get; } = context.Set<T>();

    public virtual IQueryable<T> Query() => DbSet.AsNoTracking();

    public virtual IQueryable<T> QueryTracked() => DbSet;

    public virtual async Task<T?> FindAsync(object id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        return await DbSet.FindAsync([id], cancellationToken);
    }

    public virtual async Task<IReadOnlyList<T>> ListAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) =>
        await Filter(Query(), predicate).ToListAsync(cancellationToken);

    public virtual Task<bool> AnyAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) =>
        Filter(Query(), predicate).AnyAsync(cancellationToken);

    public virtual async Task<PagedResult<T>> PagedAsync(
        int page,
        int pageSize,
        Func<IQueryable<T>, IOrderedQueryable<T>> orderBy,
        Expression<Func<T, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, MaxPageSize);
        ArgumentNullException.ThrowIfNull(orderBy);

        var query = Filter(Query(), predicate);
        var total = await query.CountAsync(cancellationToken);
        var items = await orderBy(query).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        return new PagedResult<T>(items, page, pageSize, total);
    }

    public virtual void Add(T entity) => DbSet.Add(entity);

    public virtual void AddRange(IEnumerable<T> entities) => DbSet.AddRange(entities);

    public virtual void Update(T entity) => DbSet.Update(entity);

    public virtual void Remove(T entity) => DbSet.Remove(entity);

    private static IQueryable<T> Filter(IQueryable<T> query, Expression<Func<T, bool>>? predicate) =>
        predicate is null ? query : query.Where(predicate);
}

public class UnitOfWork(DbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
