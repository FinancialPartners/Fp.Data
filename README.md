# Fp.Data

[![build](https://github.com/FinancialPartners/Fp.Data/actions/workflows/build.yml/badge.svg)](https://github.com/FinancialPartners/Fp.Data/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/Fp.Data.svg)](https://www.nuget.org/packages/Fp.Data)

Building blocks for Entity Framework Core:

- Base entities (`Entity`, `AuditableEntity`) with `long` or custom keys
- Automatic auditing (`CreatedOn/By`, `LastChangedOn/By`) via a `SaveChangesInterceptor`
- Soft delete (`ISoftDelete`) with a global query filter
- Optimistic concurrency (`IVersioned` with `RowVersion`)
- `ToPagedAsync` paging with `PagedResult<T>`
- Model conventions: `decimal(18,2)` and a default string length

No base `DbContext` or repository is required; use your `DbContext` directly. The connection string stays in your own configuration.

## Usage

```csharp
public class Customer : AuditableEntity, ISoftDelete, IVersioned
{
    public string Name { get; set; } = "";
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedOn { get; set; }
    public string? DeletedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder builder) =>
        builder.ConfigureFpConventions(); // decimal(18,2), strings max 256 (configurable)

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ... your configuration
        modelBuilder.ApplyRowVersion().ApplySoftDeleteFilter();
    }
}

builder.Services.AddFpData();
builder.Services.AddDbContext<AppDbContext>((sp, o) => o.UseSqlServer(connectionString).UseFpData(sp));
builder.Services.AddScoped<ICurrentUser, MyCurrentUser>(); // optional; default is user "system"
```

`TimeProvider` (system clock by default) and `ICurrentUser` can be replaced, e.g. with `FakeTimeProvider` in tests.

```csharp
var page = await db.Customers.OrderBy(c => c.Name).ToPagedAsync(1, 20);
```

Deleted rows are hidden by default; use `IgnoreQueryFilters()` to see them. A concurrent change throws `DbUpdateConcurrencyException`.
Note: `ExecuteUpdate`/`ExecuteDelete` bypass the interceptor, so they do not set audit fields or soft delete.

## License

MIT
