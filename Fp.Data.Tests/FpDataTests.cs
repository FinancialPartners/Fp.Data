using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Fp.Data.Tests;

[Auditable]
public class Customer : AuditableEntity, ISoftDelete
{
    public string Name { get; set; } = "";
    public string? Notes { get; set; }
    [AuditIgnore] public string? Secret { get; set; }
    public decimal Balance { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedOn { get; set; }
    public string? DeletedBy { get; set; }
}

[Auditable]
public class OrderLine
{
    public string OrderNo { get; set; } = "";
    public int LineNo { get; set; }
    public int Quantity { get; set; }
}

public class Tag : Entity
{
    public string Name { get; set; } = "";
}

// SQLite does not generate row versions, so this entity is only used to verify the model.
public class Invoice : Entity, IVersioned
{
    public byte[] RowVersion { get; set; } = [];
}

public class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Tag> Tags => Set<Tag>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ConfigureFpConventions();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderLine>().HasKey(l => new { l.OrderNo, l.LineNo });
        modelBuilder.ApplyRowVersion().ApplySoftDeleteFilter().ApplyAuditLog();
    }
}

public class TestUser : ICurrentUser
{
    public string? UserId { get; set; }
}

public class TestAuditContext : IAuditContext
{
    public string? CorrelationId { get; set; }
    public string? OriginIp { get; set; }
    public string? RequestUri { get; set; }
}

public sealed class Fixture : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero));
    public TestUser User { get; } = new();
    public TestAuditContext Audit { get; } = new();
    public ServiceProvider Provider { get; }

    public Fixture()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton<ICurrentUser>(User);
        services.AddSingleton<IAuditContext>(Audit);
        services.AddFpAuditLog();
        services.AddDbContext<TestContext>((sp, o) => o.UseSqlite(_connection).UseFpData(sp).UseFpAuditLog(sp));
        Provider = services.BuildServiceProvider();
        using var scope = Provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        Provider.Dispose();
        _connection.Dispose();
    }
}

public class FpDataTests : IDisposable
{
    private readonly Fixture _f = new();

    public void Dispose() => _f.Dispose();

    private (IServiceScope Scope, TestContext Ctx) NewScope()
    {
        var scope = _f.Provider.CreateScope();
        return (scope, scope.ServiceProvider.GetRequiredService<TestContext>());
    }

    [Fact]
    public async Task Add_SetsAuditFields()
    {
        _f.User.UserId = "joost";
        var (scope, ctx) = NewScope();
        using (scope)
        {
            var c = new Customer { Name = "A" };
            ctx.Customers.Add(c);
            await ctx.SaveChangesAsync();
            Assert.Equal(_f.Time.GetUtcNow(), c.CreatedOn);
            Assert.Equal("joost", c.CreatedBy);
            Assert.Equal("joost", c.LastChangedBy);
        }
    }

    [Fact]
    public async Task NoUser_UsesSystem()
    {
        var (scope, ctx) = NewScope();
        using (scope)
        {
            var c = new Customer { Name = "A" };
            ctx.Customers.Add(c);
            await ctx.SaveChangesAsync();
            Assert.Equal("system", c.CreatedBy);
        }
    }

    [Fact]
    public async Task Modify_KeepsCreatedAndUpdatesLastChanged()
    {
        _f.User.UserId = "a";
        var (scope, ctx) = NewScope();
        using (scope)
        {
            var c = new Customer { Name = "A" };
            ctx.Customers.Add(c);
            await ctx.SaveChangesAsync();
            var created = c.CreatedOn;

            _f.Time.Advance(TimeSpan.FromHours(1));
            _f.User.UserId = "b";
            c.Name = "B";
            await ctx.SaveChangesAsync();

            Assert.Equal(created, c.CreatedOn);
            Assert.Equal("a", c.CreatedBy);
            Assert.Equal(created.AddHours(1), c.LastChangedOn);
            Assert.Equal("b", c.LastChangedBy);
        }
    }

    [Fact]
    public async Task Delete_IsSoftAndFiltered()
    {
        _f.User.UserId = "joost";
        var (scope, ctx) = NewScope();
        using (scope)
        {
            var c = new Customer { Name = "A" };
            ctx.Customers.Add(c);
            await ctx.SaveChangesAsync();

            ctx.Customers.Remove(c);
            await ctx.SaveChangesAsync();

            Assert.Empty(await ctx.Customers.ToListAsync());
            var all = await ctx.Customers.IgnoreQueryFilters().AsNoTracking().SingleAsync();
            Assert.True(all.IsDeleted);
            Assert.Equal("joost", all.DeletedBy);
            Assert.Equal(_f.Time.GetUtcNow(), all.DeletedOn);
        }
    }

    [Fact]
    public void Model_HasRowVersionAndConventions()
    {
        var (scope, ctx) = NewScope();
        using (scope)
        {
            Assert.True(ctx.Model.FindEntityType(typeof(Invoice))!.FindProperty(nameof(Invoice.RowVersion))!.IsConcurrencyToken);
            var type = ctx.Model.FindEntityType(typeof(Customer))!;
            Assert.Equal(18, type.FindProperty(nameof(Customer.Balance))!.GetPrecision());
            Assert.Equal(2, type.FindProperty(nameof(Customer.Balance))!.GetScale());
            Assert.Equal(256, type.FindProperty(nameof(Customer.Name))!.GetMaxLength());
        }
    }

    [Fact]
    public async Task ToPaged_Works()
    {
        var (scope, ctx) = NewScope();
        using (scope)
        {
            ctx.Customers.AddRange(Enumerable.Range(1, 25).Select(i => new Customer { Name = $"C{i:00}" }));
            await ctx.SaveChangesAsync();

            var page = await ctx.Customers.OrderBy(x => x.Name).ToPagedAsync(3, 10);
            Assert.Equal(5, page.Items.Count);
            Assert.Equal(25, page.TotalCount);
            Assert.Equal(3, page.TotalPages);
            Assert.False(page.HasNextPage);
            Assert.True(page.HasPreviousPage);
            Assert.Equal("C21", page.Items[0].Name);
        }
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 1001)]
    public async Task ToPaged_InvalidArguments_Throw(int page, int size)
    {
        var (scope, ctx) = NewScope();
        using (scope)
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ctx.Customers.OrderBy(x => x.Id).ToPagedAsync(page, size));
        }
    }
}
