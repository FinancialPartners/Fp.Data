using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Fp.Data;

/// <summary>
/// Writes an <see cref="AuditEntry"/> with details for changes to entities marked with <see cref="AuditableAttribute"/>.
/// The log is saved in the same <c>SaveChanges</c> (and transaction) as the changes themselves.
/// Only for new records with a database generated key, the key is filled in right after the save.
/// </summary>
public class AuditLogSaveChangesInterceptor(TimeProvider timeProvider, ICurrentUser currentUser, IAuditContext auditContext)
    : SaveChangesInterceptor
{
    public const int MaxValueLength = 4000;

    private static readonly HashSet<string> AuditColumns =
    [
        nameof(IAuditable.CreatedOn), nameof(IAuditable.CreatedBy),
        nameof(IAuditable.LastChangedOn), nameof(IAuditable.LastChangedBy)
    ];

    private readonly List<PendingKey> _pending = [];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (Complete())
        {
            eventData.Context!.SaveChanges();
        }

        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (Complete())
        {
            await eventData.Context!.SaveChangesAsync(cancellationToken);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => _pending.Clear();

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending.Clear();
        return Task.CompletedTask;
    }

    private void Collect(DbContext? context)
    {
        _pending.Clear();
        if (context is null)
        {
            return;
        }

        var pending = new List<PendingKey>();
        var details = new List<AuditEntryDetail>();

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)
                || !entry.Metadata.ClrType.IsDefined(typeof(AuditableAttribute), inherit: true))
            {
                continue;
            }

            var action = GetAction(entry);
            var key = BuildKey(entry, action);
            var entryDetails = new List<AuditEntryDetail>();
            var generated = new List<(AuditEntryDetail Detail, string Property)>();
            var tableName = entry.Metadata.GetTableName() ?? entry.Metadata.ClrType.Name;

            foreach (var property in entry.Properties)
            {
                if (IsExcluded(entry, property))
                {
                    continue;
                }

                var detail = CreateDetail(property, action, tableName, key.Value);
                if (detail is null)
                {
                    continue;
                }

                entryDetails.Add(detail);
                if (action == AuditAction.Add && property.IsTemporary)
                {
                    generated.Add((detail, property.Metadata.Name));
                }
            }

            if (entryDetails.Count == 0)
            {
                continue;
            }

            details.AddRange(entryDetails);
            if (key.IsTemporary || generated.Count > 0)
            {
                pending.Add(new PendingKey(entry, entryDetails, generated));
            }
        }

        if (details.Count == 0)
        {
            return;
        }

        if (context.Model.FindEntityType(typeof(AuditEntry)) is null)
        {
            throw new InvalidOperationException(
                "Auditable entities were changed, but the audit log is not part of the model. Call modelBuilder.ApplyAuditLog() in OnModelCreating.");
        }

        context.Add(new AuditEntry
        {
            TimestampUtc = timeProvider.GetUtcNow(),
            UserId = currentUser.UserId ?? AuditSaveChangesInterceptor.SystemUser,
            CorrelationId = auditContext.CorrelationId,
            OriginIp = auditContext.OriginIp,
            RequestUri = auditContext.RequestUri,
            Details = details
        });

        _pending.AddRange(pending);
    }

    // Fills in database generated keys. Returns true when a second save is needed.
    private bool Complete()
    {
        if (_pending.Count == 0)
        {
            return false;
        }

        var pending = _pending.ToList();
        _pending.Clear();

        foreach (var item in pending)
        {
            var key = BuildKey(item.Entry, AuditAction.Add);
            foreach (var detail in item.Details)
            {
                detail.RecordKey = key.Value;
            }

            foreach (var (detail, property) in item.Generated)
            {
                detail.NewValue = AuditValueFormatter.Format(item.Entry.Property(property).CurrentValue);
            }
        }

        return true;
    }

    private static AuditAction GetAction(EntityEntry entry)
    {
        if (entry.State == EntityState.Added)
        {
            return AuditAction.Add;
        }

        if (entry.State == EntityState.Deleted)
        {
            return AuditAction.Delete;
        }

        // Soft delete is saved as an update, but it is a delete for the audit log.
        if (entry.Entity is ISoftDelete)
        {
            var isDeleted = entry.Property(nameof(ISoftDelete.IsDeleted));
            if (isDeleted is { CurrentValue: true, OriginalValue: false })
            {
                return AuditAction.Delete;
            }
        }

        return AuditAction.Modify;
    }

    private static bool IsExcluded(EntityEntry entry, PropertyEntry property)
    {
        var name = property.Metadata.Name;
        return (entry.Entity is IAuditable && AuditColumns.Contains(name))
               || (entry.Entity is IVersioned && name == nameof(IVersioned.RowVersion))
               || property.Metadata.PropertyInfo?.IsDefined(typeof(AuditIgnoreAttribute), inherit: true) == true;
    }

    private static AuditEntryDetail? CreateDetail(PropertyEntry property, AuditAction action, string tableName, string? recordKey)
    {
        string? oldValue = null;
        string? newValue = null;

        switch (action)
        {
            case AuditAction.Add:
                newValue = AuditValueFormatter.Format(property.CurrentValue);
                if (newValue is null && !property.IsTemporary)
                {
                    return null;
                }

                break;
            case AuditAction.Modify:
                if (!property.IsModified || ValuesEqual(property.OriginalValue, property.CurrentValue))
                {
                    return null;
                }

                oldValue = AuditValueFormatter.Format(property.OriginalValue);
                newValue = AuditValueFormatter.Format(property.CurrentValue);
                break;
            default:
                oldValue = AuditValueFormatter.Format(property.OriginalValue);
                if (oldValue is null)
                {
                    return null;
                }

                break;
        }

        return new AuditEntryDetail
        {
            TableName = tableName,
            RecordKey = recordKey,
            Action = action,
            ColumnName = property.Metadata.Name,
            OldValue = oldValue,
            NewValue = newValue
        };
    }

    private static (string? Value, bool IsTemporary) BuildKey(EntityEntry entry, AuditAction action)
    {
        var keyProperties = entry.Metadata.FindPrimaryKey()?.Properties;
        if (keyProperties is null)
        {
            return (null, false);
        }

        var parts = new List<string?>();
        foreach (var keyProperty in keyProperties)
        {
            var property = entry.Property(keyProperty.Name);
            if (property.IsTemporary)
            {
                return (null, true);
            }

            parts.Add(AuditValueFormatter.Format(action == AuditAction.Delete && entry.State == EntityState.Deleted
                ? property.OriginalValue
                : property.CurrentValue));
        }

        return (string.Join('|', parts), false);
    }

    private static bool ValuesEqual(object? a, object? b) =>
        a is byte[] x && b is byte[] y ? x.AsSpan().SequenceEqual(y) : Equals(a, b);

    private sealed record PendingKey(
        EntityEntry Entry,
        List<AuditEntryDetail> Details,
        List<(AuditEntryDetail Detail, string Property)> Generated);
}

/// <summary>
/// Converts values to text for the audit log: invariant culture, ISO 8601 dates, limited to <see cref="AuditLogSaveChangesInterceptor.MaxValueLength"/> characters.
/// </summary>
public static class AuditValueFormatter
{
    public static string? Format(object? value)
    {
        var text = value switch
        {
            null => null,
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            byte[] bytes => Convert.ToBase64String(bytes),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

        return text is { Length: > AuditLogSaveChangesInterceptor.MaxValueLength }
            ? text[..AuditLogSaveChangesInterceptor.MaxValueLength]
            : text;
    }
}
