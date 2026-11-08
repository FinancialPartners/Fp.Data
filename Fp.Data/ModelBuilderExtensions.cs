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
