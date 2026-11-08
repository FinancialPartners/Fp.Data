using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Fp.Data;

public static class ModelBuilderExtensions
{
    /// <summary>
    /// Hides rows with <see cref="ISoftDelete.IsDeleted"/> from all queries. Use <c>IgnoreQueryFilters()</c> to see them.
    /// Call it at the end of <c>OnModelCreating</c>.
    /// </summary>
    public static ModelBuilder ApplySoftDeleteFilter(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => t.BaseType is null && typeof(ISoftDelete).IsAssignableFrom(t.ClrType)))
        {
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var notDeleted = Expression.Equal(Expression.Property(parameter, nameof(ISoftDelete.IsDeleted)), Expression.Constant(false));
            entityType.SetQueryFilter(Expression.Lambda(notDeleted, parameter));
        }

        return modelBuilder;
    }

    /// <summary>
    /// Configures <see cref="IVersioned.RowVersion"/> as a concurrency token for all entities that implement it.
    /// A concurrent change then throws <see cref="DbUpdateConcurrencyException"/>. Call it in <c>OnModelCreating</c>.
    /// </summary>
    public static ModelBuilder ApplyRowVersion(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => t.BaseType is null && typeof(IVersioned).IsAssignableFrom(t.ClrType)))
        {
            modelBuilder.Entity(entityType.ClrType).Property(nameof(IVersioned.RowVersion)).IsRowVersion();
        }

        return modelBuilder;
    }

    /// <summary>
    /// Adds the audit log tables (<see cref="AuditEntry"/> and <see cref="AuditEntryDetail"/>) to the model.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="schema">Database schema of the tables, or <c>null</c> for the default schema.</param>
    public static ModelBuilder ApplyAuditLog(this ModelBuilder modelBuilder, string? schema = "audit")
    {
        modelBuilder.Entity<AuditEntry>(b =>
        {
            b.ToTable("AuditEntry", schema);
            b.Property(e => e.UserId).HasMaxLength(256);
            b.Property(e => e.CorrelationId).HasMaxLength(128);
            b.Property(e => e.OriginIp).HasMaxLength(64);
            b.Property(e => e.RequestUri).HasMaxLength(2000);
            b.HasIndex(e => e.CorrelationId);
            b.HasIndex(e => new { e.UserId, e.TimestampUtc });
            b.HasMany(e => e.Details).WithOne(d => d.AuditEntry).HasForeignKey(d => d.AuditEntryId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditEntryDetail>(b =>
        {
            b.ToTable("AuditEntryDetail", schema);
            b.Property(e => e.TableName).HasMaxLength(128);
            b.Property(e => e.RecordKey).HasMaxLength(450);
            b.Property(e => e.ColumnName).HasMaxLength(128);
            b.Property(e => e.Action).HasConversion<string>().HasMaxLength(16);
            b.Property(e => e.OldValue).HasMaxLength(AuditLogSaveChangesInterceptor.MaxValueLength);
            b.Property(e => e.NewValue).HasMaxLength(AuditLogSaveChangesInterceptor.MaxValueLength);
            b.HasIndex(e => new { e.TableName, e.RecordKey });
        });

        return modelBuilder;
    }

    /// <summary>
    /// Sensible defaults: <c>decimal(18,2)</c> and a maximum length for strings without explicit configuration.
    /// Call it in <c>ConfigureConventions</c>. Pass <c>null</c> as <paramref name="stringMaxLength"/> to keep EF's default (unlimited).
    /// </summary>
    public static ModelConfigurationBuilder ConfigureFpConventions(
        this ModelConfigurationBuilder builder, int decimalPrecision = 18, int decimalScale = 2, int? stringMaxLength = 256)
    {
        builder.Properties<decimal>().HavePrecision(decimalPrecision, decimalScale);
        builder.Properties<decimal?>().HavePrecision(decimalPrecision, decimalScale);
        if (stringMaxLength is { } max)
        {
            builder.Properties<string>().HaveMaxLength(max);
        }

        return builder;
    }
}
