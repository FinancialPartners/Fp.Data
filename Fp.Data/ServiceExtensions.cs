using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Fp.Data;

public static class ServiceExtensions
{
    /// <summary>
    /// Registers the audit interceptor, <see cref="TimeProvider"/> (system clock), <see cref="ICurrentUser"/> (no user),
    /// <see cref="IUnitOfWork"/> and the open generic <see cref="IRepository{T}"/> for the given context.
    /// Register your own <see cref="ICurrentUser"/> and <see cref="TimeProvider"/> before or after; existing registrations are kept.
    /// </summary>
    /// <remarks>
    /// <typeparamref name="TContext"/> is also registered as <see cref="DbContext"/>, so use this for one context per application.
    /// Activate the interceptor with <see cref="UseFpData"/> inside <c>AddDbContext</c>.
    /// </remarks>
    public static IServiceCollection AddFpData<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser, NoCurrentUser>();
        services.TryAddScoped<AuditSaveChangesInterceptor>();
        services.TryAddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        services.TryAddScoped<IUnitOfWork, UnitOfWork>();
        services.TryAddScoped(typeof(IRepository<>), typeof(Repository<>));
        return services;
    }

    /// <summary>
    /// Adds the audit and soft delete interceptor to a context: <c>AddDbContext&lt;MyContext&gt;((sp, o) =&gt; o.UseSqlServer(...).UseFpData(sp))</c>.
    /// </summary>
    public static DbContextOptionsBuilder UseFpData(this DbContextOptionsBuilder builder, IServiceProvider serviceProvider) =>
        builder.AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>());
}
