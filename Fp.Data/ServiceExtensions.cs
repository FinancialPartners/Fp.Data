using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Fp.Data;

public static class ServiceExtensions
{
    /// <summary>
    /// Registers the audit interceptor, <see cref="TimeProvider"/> (system clock) and <see cref="ICurrentUser"/> (no user).
    /// Existing registrations of <see cref="TimeProvider"/> and <see cref="ICurrentUser"/> are kept.
    /// Activate the interceptor with <see cref="UseFpData"/> inside <c>AddDbContext</c>.
    /// </summary>
    public static IServiceCollection AddFpData(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser, NoCurrentUser>();
        services.TryAddScoped<AuditSaveChangesInterceptor>();
        return services;
    }

    /// <summary>
    /// Registers the audit log interceptor and <see cref="IAuditContext"/> (no request information) and everything <see cref="AddFpData"/> registers.
    /// Register your own <see cref="IAuditContext"/> to log correlation id, IP and URI.
    /// </summary>
    public static IServiceCollection AddFpAuditLog(this IServiceCollection services)
    {
        services.AddFpData();
        services.TryAddScoped<IAuditContext, NoAuditContext>();
        services.TryAddScoped<AuditLogSaveChangesInterceptor>();
        return services;
    }

    /// <summary>
    /// Adds the audit log interceptor to a context. Call it after <see cref="UseFpData"/>.
    /// </summary>
    public static DbContextOptionsBuilder UseFpAuditLog(this DbContextOptionsBuilder builder, IServiceProvider serviceProvider) =>
        builder.AddInterceptors(serviceProvider.GetRequiredService<AuditLogSaveChangesInterceptor>());

    /// <summary>
    /// Adds the audit and soft delete interceptor to a context: <c>AddDbContext&lt;MyContext&gt;((sp, o) =&gt; o.UseSqlServer(...).UseFpData(sp))</c>.
    /// </summary>
    public static DbContextOptionsBuilder UseFpData(this DbContextOptionsBuilder builder, IServiceProvider serviceProvider) =>
        builder.AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>());
}
