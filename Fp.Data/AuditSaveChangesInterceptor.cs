using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Fp.Data;

/// <summary>
/// Sets the audit columns of <see cref="IAuditable"/> entities and turns deletes of <see cref="ISoftDelete"/> entities into updates.
/// </summary>
public class AuditSaveChangesInterceptor(TimeProvider timeProvider, ICurrentUser currentUser) : SaveChangesInterceptor
{
    public const string SystemUser = "system";

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var user = currentUser.UserId ?? SystemUser;

        // ToList: soft delete changes the state of entries while we iterate.
        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDelete softDelete)
            {
                entry.State = EntityState.Modified;
                softDelete.IsDeleted = true;
                softDelete.DeletedOn = now;
                softDelete.DeletedBy = user;
                Touch(entry, now, user);
            }
            else if (entry.State == EntityState.Added && entry.Entity is IAuditable added)
            {
                added.CreatedOn = now;
                added.CreatedBy = user;
                added.LastChangedOn = now;
                added.LastChangedBy = user;
            }
            else if (entry.State == EntityState.Modified)
            {
                Touch(entry, now, user);
            }
        }
    }

    private static void Touch(EntityEntry entry, DateTimeOffset now, string user)
    {
        if (entry.Entity is not IAuditable auditable)
        {
            return;
        }

        auditable.LastChangedOn = now;
        auditable.LastChangedBy = user;

        // The creation data must never be overwritten, even when an entity is attached as a whole.
        entry.Property(nameof(IAuditable.CreatedOn)).IsModified = false;
        entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
    }
}
