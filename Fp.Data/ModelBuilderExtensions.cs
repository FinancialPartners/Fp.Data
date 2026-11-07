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
}
