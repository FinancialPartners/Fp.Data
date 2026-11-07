namespace Fp.Data;

public interface IEntity<TKey>
{
    TKey Id { get; set; }
}

public abstract class Entity<TKey> : IEntity<TKey>
{
    public TKey Id { get; set; } = default!;
}

/// <summary>
/// Entity with a <c>long</c> key.
/// </summary>
public abstract class Entity : Entity<long>;

/// <summary>
/// Filled in automatically by <see cref="AuditSaveChangesInterceptor"/>.
/// </summary>
public interface IAuditable
{
    DateTimeOffset CreatedOn { get; set; }
    string? CreatedBy { get; set; }
    DateTimeOffset LastChangedOn { get; set; }
    string? LastChangedBy { get; set; }
}

public abstract class AuditableEntity<TKey> : Entity<TKey>, IAuditable
{
    public DateTimeOffset CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset LastChangedOn { get; set; }
    public string? LastChangedBy { get; set; }
}

/// <summary>
/// Auditable entity with a <c>long</c> key.
/// </summary>
public abstract class AuditableEntity : AuditableEntity<long>;

/// <summary>
/// Deleting an entity with this interface marks it as deleted instead of removing the row.
/// Call <see cref="ModelBuilderExtensions.ApplySoftDeleteFilter"/> to hide deleted rows from queries.
/// </summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedOn { get; set; }
    string? DeletedBy { get; set; }
}
