namespace Fp.Data;

/// <summary>
/// Marks an entity whose changes are written to the audit log. Requires <see cref="ModelBuilderExtensions.ApplyAuditLog"/>,
/// <c>AddFpAuditLog</c> and <c>UseFpAuditLog</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class AuditableAttribute : Attribute;

/// <summary>
/// Excludes a property from the audit log, for example passwords, tokens or large blobs.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditIgnoreAttribute : Attribute;

public enum AuditAction
{
    Add,
    Modify,
    Delete
}

/// <summary>
/// Request information for the audit log. Implement it in your application, for example from <c>HttpContext</c>.
/// </summary>
public interface IAuditContext
{
    string? CorrelationId { get; }
    string? OriginIp { get; }
    string? RequestUri { get; }
}

/// <summary>
/// Default <see cref="IAuditContext"/> without request information.
/// </summary>
public sealed class NoAuditContext : IAuditContext
{
    public string? CorrelationId => null;
    public string? OriginIp => null;
    public string? RequestUri => null;
}

/// <summary>
/// Header of the audit log: one row per <c>SaveChanges</c> that changed at least one auditable entity.
/// </summary>
public class AuditEntry
{
    public long Id { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string? UserId { get; set; }
    public string? CorrelationId { get; set; }
    public string? OriginIp { get; set; }
    public string? RequestUri { get; set; }
    public List<AuditEntryDetail> Details { get; set; } = [];
}

/// <summary>
/// One changed column of one record.
/// </summary>
public class AuditEntryDetail
{
    public long Id { get; set; }
    public long AuditEntryId { get; set; }
    public AuditEntry AuditEntry { get; set; } = null!;
    public string TableName { get; set; } = "";

    /// <summary>Primary key of the record; composite keys are joined with <c>|</c>.</summary>
    public string? RecordKey { get; set; }

    public AuditAction Action { get; set; }
    public string ColumnName { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}
