using Microsoft.EntityFrameworkCore;

namespace Fp.Data;

public static class AuditQueryExtensions
{
    /// <summary>History of one record, for example <c>db.Set&lt;AuditEntryDetail&gt;().ForRecord("Customers", 42)</c>.</summary>
    public static IQueryable<AuditEntryDetail> ForRecord(this IQueryable<AuditEntryDetail> query, string tableName, object key)
    {
        var recordKey = AuditValueFormatter.Format(key);
        return query.Where(d => d.TableName == tableName && d.RecordKey == recordKey);
    }

    /// <summary>History of one record of entity type <typeparamref name="TEntity"/>; the table name comes from the model.</summary>
    public static IQueryable<AuditEntryDetail> ForRecord<TEntity>(this IQueryable<AuditEntryDetail> query, DbContext context, object key)
    {
        var entityType = context.Model.FindEntityType(typeof(TEntity))
                         ?? throw new ArgumentException($"{typeof(TEntity).Name} is not part of the model.", nameof(context));
        return query.ForRecord(entityType.GetTableName() ?? typeof(TEntity).Name, key);
    }

    public static IQueryable<AuditEntry> ForUser(this IQueryable<AuditEntry> query, string userId) =>
        query.Where(e => e.UserId == userId);

    public static IQueryable<AuditEntry> Between(this IQueryable<AuditEntry> query, DateTimeOffset from, DateTimeOffset to) =>
        query.Where(e => e.TimestampUtc >= from && e.TimestampUtc < to);
}
