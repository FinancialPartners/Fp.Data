using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fp.Data.Tests;

public class AuditLogTests : IDisposable
{
    private readonly Fixture _f = new();

    public void Dispose() => _f.Dispose();

    private (IServiceScope Scope, TestContext Ctx) NewScope()
    {
        var scope = _f.Provider.CreateScope();
        return (scope, scope.ServiceProvider.GetRequiredService<TestContext>());
    }

    private async Task<List<AuditEntryDetail>> Details()
    {
        var (scope, ctx) = NewScope();
        using (scope)
        {
            return await ctx.Set<AuditEntryDetail>().AsNoTracking().OrderBy(d => d.Id).ToListAsync();
        }
    }

    private async Task<int> EntryCount()
    {
        var (scope, ctx) = NewScope();
        using (scope)
        {
            return await ctx.Set<AuditEntry>().CountAsync();
        }
    }

    [Fact]
    public async Task Add_LogsColumnsAndGeneratedKey()
    {
        var (scope, ctx) = NewScope();
        using (scope)
        {
            var c = new Customer { Name = "A", Secret = "geheim" };
            ctx.Customers.Add(c);
            await ctx.SaveChangesAsync();

            var details = await Details();
            Assert.All(details, d =>
            {
                Assert.Equal(AuditAction.Add, d.Action);
                Assert.Equal(c.Id.ToString(), d.RecordKey);
                Assert.Equal("Customers", d.TableName);
                Assert.Null(d.OldValue);
            });
            Assert.Equal("A", details.Single(d => d.ColumnName == "Name").NewValue);
            Assert.Equal(c.Id.ToString(), details.Single(d => d.ColumnName == "Id").NewValue);
            Assert.DoesNotContain(details, d => d.ColumnName is "Secret" or "CreatedBy" or "LastChangedOn");
            Assert.Equal(1, await EntryCount());
        }
    }

    [Fact]
    public async Task Add_AsyncAndSync_BothWork()
    {
        var (scope, ctx) = NewScope();
        using (scope)
        {
            var c = new Customer { Name = "A" };
            ctx.Customers.Add(c);
            ctx.SaveChanges();

            Assert.All(await Details(), d => Assert.Equal(c.Id.ToString(), d.RecordKey));
        }
    }

    [Fact]
    public async Task Modify_LogsOnlyChangedColumns()
    {
        var (scope, ctx) = NewScope();
        using (scope)
        {
            var c = new Customer { Name = "A", Notes = "n" };
            ctx.Customers.Add(c);
            await ctx.SaveChangesAsync();
            var before = (await Details()).Count;

            c.Name = "B";
            c.Secret = "x";
            await ctx.SaveChangesAsync();

            var added = (await Details()).Skip(before).ToList();
            var detail = Assert.Single(added);
            Assert.Equal(AuditAction.Modify, detail.Action);
            Assert.Equal("Name", detail.ColumnName);
            Assert.Equal("A", detail.OldValue);
            Assert.Equal("B", detail.NewValue);
            Assert.Equal(c.Id.ToString(), detail.RecordKey);
        }
    }

    [Fact]
    public async Task SoftDelete_IsLoggedAsDelete()
    {
        var (scope, ctx) = NewScope();
        using (scope)
        {
            var c = new Customer { Name = "A" };
            ctx.Customers.Add(c);
            await ctx.SaveChangesAsync();
            var before = (await Details()).Count;

            ctx.Customers.Remove(c);
            await ctx.SaveChangesAsync();

            var added = (await Details()).Skip(before).ToList();
            Assert.NotEmpty(added);
            Assert.All(added, d => Assert.Equal(AuditAction.Delete, d.Action));
            Assert.Equal("A", added.Single(d => d.ColumnName == "Name").OldValue);
            Assert.All(added, d => Assert.Null(d.NewValue));
        }
    }

    [Fact]
    public async Task NonAuditableChange_WritesNoEntry()
    {
        var (scope, ctx) = NewScope();
        using (scope)
        {
            ctx.Tags.Add(new Tag { Name = "t" });
            await ctx.SaveChangesAsync();
        }

        Assert.Equal(0, await EntryCount());
    }

    [Fact]
    public async Task Entry_ContainsUserTimeAndRequestContext()
    {
        _f.User.UserId = "joost";
        _f.Audit.CorrelationId = "req-1";
        _f.Audit.OriginIp = "10.0.0.1";
        _f.Audit.RequestUri = "https://app/api/customers";
        var (scope, ctx) = NewScope();
        using (scope)
        {
            ctx.Customers.Add(new Customer { Name = "A" });
            await ctx.SaveChangesAsync();

            var entry = await ctx.Set<AuditEntry>().AsNoTracking().SingleAsync();
            Assert.Equal("joost", entry.UserId);
            Assert.Equal("req-1", entry.CorrelationId);
            Assert.Equal("10.0.0.1", entry.OriginIp);
            Assert.Equal("https://app/api/customers", entry.RequestUri);
            Assert.Equal(_f.Time.GetUtcNow(), entry.TimestampUtc);
        }
    }

    [Fact]
    public async Task CompositeKey_IsJoined()
    {
        var (scope, ctx) = NewScope();
        using (scope)
        {
            ctx.OrderLines.Add(new OrderLine { OrderNo = "A1", LineNo = 2, Quantity = 5 });
            await ctx.SaveChangesAsync();

            Assert.All(await Details(), d => Assert.Equal("A1|2", d.RecordKey));
        }
    }

    [Fact]
    public async Task FailedSave_LeavesNoLog()
    {
        var (first, ctx1) = NewScope();
        using (first)
        {
            ctx1.OrderLines.Add(new OrderLine { OrderNo = "A", LineNo = 1 });
            await ctx1.SaveChangesAsync();
        }

        var before = await EntryCount();
        var (scope, ctx) = NewScope();
        using (scope)
        {
            ctx.Customers.Add(new Customer { Name = "ok" });
            ctx.OrderLines.Add(new OrderLine { OrderNo = "A", LineNo = 1 });
            await Assert.ThrowsAnyAsync<Exception>(() => ctx.SaveChangesAsync());
        }

        Assert.Equal(before, await EntryCount());
        Assert.DoesNotContain(await Details(), d => d.TableName == "Customers");
    }

    [Fact]
    public async Task Values_UseInvariantCultureAndAreTruncated()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("nl-BE");
        try
        {
            var (scope, ctx) = NewScope();
            using (scope)
            {
                ctx.Customers.Add(new Customer { Name = "A", Balance = 1234.5m, Notes = new string('x', 5000) });
                await ctx.SaveChangesAsync();
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        var details = await Details();
        Assert.Equal("1234.5", details.Single(d => d.ColumnName == "Balance").NewValue);
        Assert.Equal(4000, details.Single(d => d.ColumnName == "Notes").NewValue!.Length);
    }

    [Fact]
    public async Task ForRecord_ReturnsHistory()
    {
        var (scope, ctx) = NewScope();
        using (scope)
        {
            var a = new Customer { Name = "A" };
            var b = new Customer { Name = "B" };
            ctx.Customers.AddRange(a, b);
            await ctx.SaveChangesAsync();
            a.Name = "A2";
            await ctx.SaveChangesAsync();

            var history = await ctx.Set<AuditEntryDetail>().ForRecord<Customer>(ctx, a.Id).ToListAsync();
            Assert.All(history, d => Assert.Equal(a.Id.ToString(), d.RecordKey));
            Assert.Contains(history, d => d.NewValue == "A2");
            Assert.DoesNotContain(history, d => d.NewValue == "B");
        }
    }

    [Fact]
    public async Task MissingModel_GivesClearError()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();
        var services = new ServiceCollection();
        services.AddFpAuditLog();
        services.AddDbContext<NoAuditModelContext>((sp, o) => o.UseSqlite(connection).UseFpAuditLog(sp));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<NoAuditModelContext>();
        ctx.Database.EnsureCreated();
        ctx.Items.Add(new AuditedItem { Name = "x" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
        Assert.Contains("ApplyAuditLog", ex.Message);
    }
}

[Auditable]
public class AuditedItem : Entity
{
    public string Name { get; set; } = "";
}

public class NoAuditModelContext(DbContextOptions<NoAuditModelContext> options) : DbContext(options)
{
    public DbSet<AuditedItem> Items => Set<AuditedItem>();
}
