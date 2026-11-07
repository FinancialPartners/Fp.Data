# Fp.Data

[![build](https://github.com/FinancialPartners/Fp.Data/actions/workflows/build.yml/badge.svg)](https://github.com/FinancialPartners/Fp.Data/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/Fp.Data.svg)](https://www.nuget.org/packages/Fp.Data)

Building blocks for Entity Framework Core:

- Base entities (`Entity`, `AuditableEntity`) with `long` or custom keys
- Automatic auditing (`CreatedOn/By`, `LastChangedOn/By`) via a `SaveChangesInterceptor`
- Soft delete (`ISoftDelete`) with a global query filter
- A thin, optional repository with paging and a unit of work

No base `DbContext` is required, and the connection string stays in your own configuration.

## Usage

```csharp
public class Customer : AuditableEntity, ISoftDelete
{
    public string Name { get; set; } = "";
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedOn { get; set; }
    public string? DeletedBy { get; set; }
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ... your configuration
        modelBuilder.ApplySoftDeleteFilter();
    }
}

builder.Services.AddFpData<AppDbContext>();
builder.Services.AddDbContext<AppDbContext>((sp, o) => o.UseSqlServer(connectionString).UseFpData(sp));
builder.Services.AddScoped<ICurrentUser, MyCurrentUser>(); // optional; default is user "system"
```

`TimeProvider` (system clock by default) and `ICurrentUser` can be replaced, e.g. with `FakeTimeProvider` in tests.

```csharp
var page = await repository.PagedAsync(1, 20, q => q.OrderBy(c => c.Name), c => c.Name.StartsWith("A"));
await unitOfWork.SaveChangesAsync();
```

Deleted rows are hidden by default; use `IgnoreQueryFilters()` to see them.

## License

MIT
