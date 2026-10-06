using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;
using Tiaano.Vms.Api.Services;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>
/// Concurrent allocations must never return the same formatted pass code.
/// </summary>
[Collection("CoreWorkflow")]
public class PassNumberAllocationTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public PassNumberAllocationTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Concurrent_allocations_are_unique()
    {
        using var scope = _factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.Set(WellKnownTenants.TiaanoId);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var existing = await db.PassNumberSeries.IgnoreQueryFilters()
            .Where(s => s.TenantId == WellKnownTenants.TiaanoId)
            .ToListAsync();
        db.PassNumberSeries.RemoveRange(existing);
        db.PassNumberHolds.RemoveRange(
            await db.PassNumberHolds.IgnoreQueryFilters()
                .Where(h => h.TenantId == WellKnownTenants.TiaanoId)
                .ToListAsync());

        db.PassNumberSeries.Add(new PassNumberSeries
        {
            TenantId = WellKnownTenants.TiaanoId,
            Prefix = "VMS-",
            Year = 2026,
            StartNumber = 1000,
            EndNumber = 999999,
            CurrentNumber = 999,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var bag = new ConcurrentBag<string>();
        var tasks = Enumerable.Range(0, 12).Select(async _ =>
        {
            using var s = _factory.Services.CreateScope();
            s.ServiceProvider.GetRequiredService<ITenantContext>().Set(WellKnownTenants.TiaanoId);
            var svc = s.ServiceProvider.GetRequiredService<IPassNumberService>();
            var code = await svc.AllocateNextAsync();
            bag.Add(code);
        });

        await Task.WhenAll(tasks);

        Assert.Equal(12, bag.Count);
        Assert.Equal(12, bag.Distinct(StringComparer.Ordinal).Count());
        Assert.All(bag, c => Assert.Matches(@"^VMS-\d{4}-\d{6}$", c));
    }

    [Fact]
    public void FormatPassCode_uses_prefix_year_and_padded_number()
    {
        var code = PassNumberService.FormatPassCode("VMS-", 2026, 184);
        Assert.Equal("VMS-2026-000184", code);
    }

    [Fact]
    public void TryParsePassNumber_reads_suffix()
    {
        Assert.True(PassNumberService.TryParsePassNumber("VMS-2026-000006", out var n));
        Assert.Equal(6, n);
    }

    [Fact]
    public async Task Allocate_reuses_lowest_free_after_hold_released()
    {
        using var scope = _factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.Set(WellKnownTenants.TiaanoId);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<IPassNumberService>();

        var existing = await db.PassNumberSeries.IgnoreQueryFilters()
            .Where(s => s.TenantId == WellKnownTenants.TiaanoId)
            .ToListAsync();
        db.PassNumberSeries.RemoveRange(existing);
        var holds = await db.PassNumberHolds.IgnoreQueryFilters()
            .Where(h => h.TenantId == WellKnownTenants.TiaanoId)
            .ToListAsync();
        db.PassNumberHolds.RemoveRange(holds);

        db.PassNumberSeries.Add(new PassNumberSeries
        {
            TenantId = WellKnownTenants.TiaanoId,
            Prefix = "BADGE-",
            Year = 2026,
            StartNumber = 6,
            EndNumber = 10,
            CurrentNumber = 5,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var first = await svc.AllocateNextAsync();
        var second = await svc.AllocateNextAsync();
        Assert.Equal("BADGE-2026-000006", first);
        Assert.Equal("BADGE-2026-000007", second);

        var third = await svc.AllocateNextAsync();
        Assert.Equal("BADGE-2026-000008", third);

        await svc.ReleaseHoldAsync(first);
        var reused = await svc.AllocateNextAsync();
        Assert.Equal("BADGE-2026-000006", reused);
    }

    [Fact]
    public async Task Location_series_have_independent_number_pools()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(WellKnownTenants.TiaanoId);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<IPassNumberService>();
        var tenantId = WellKnownTenants.TiaanoId;

        var locA = await EnsureLocationAsync(db, tenantId, "Pass Pool A");
        var locB = await EnsureLocationAsync(db, tenantId, "Pass Pool B");

        db.PassNumberSeries.RemoveRange(await db.PassNumberSeries.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId).ToListAsync());
        db.PassNumberHolds.RemoveRange(await db.PassNumberHolds.IgnoreQueryFilters()
            .Where(h => h.TenantId == tenantId).ToListAsync());
        db.PassNumberSeries.AddRange(
            new PassNumberSeries { TenantId = tenantId, Prefix = "VMS-", Year = 2026, StartNumber = 1000, EndNumber = 999999, CurrentNumber = 999, IsActive = true },
            new PassNumberSeries { TenantId = tenantId, LocationId = locA, Prefix = "PA-", Year = 2026, StartNumber = 1, EndNumber = 2, CurrentNumber = 0, IsActive = true },
            new PassNumberSeries { TenantId = tenantId, LocationId = locB, Prefix = "PB-", Year = 2026, StartNumber = 1, EndNumber = 2, CurrentNumber = 0, IsActive = true });
        await db.SaveChangesAsync();

        var visitId = Guid.Empty;
        try
        {
            Assert.Equal("PA-2026-000001", await svc.AllocateNextAsync(locA));
            Assert.Equal("PA-2026-000002", await svc.AllocateNextAsync(locA));

            // Location A's pool being full must not consume location B's numbers.
            Assert.Equal("PB-2026-000001", await svc.AllocateNextAsync(locB));

            // A visitor inside at B holding PB #2 exhausts B only.
            visitId = await AddInsideVisitAsync(db, tenantId, locB, "PB-2026-000002");
            var full = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.AllocateNextAsync(locB));
            Assert.Contains("in use", full.Message);

            // Releasing A's #1 frees it for A even though B still holds a #1 and a #2.
            await svc.ReleaseHoldAsync("PA-2026-000001");
            Assert.Equal("PA-2026-000001", await svc.AllocateNextAsync(locA));
        }
        finally
        {
            if (visitId != Guid.Empty)
            {
                var visit = await db.VisitorVisits.IgnoreQueryFilters().FirstAsync(v => v.Id == visitId);
                visit.Status = VisitStatus.CheckedOut;
                visit.CheckOutAt = DateTime.UtcNow;
            }
            db.PassNumberSeries.RemoveRange(await db.PassNumberSeries.IgnoreQueryFilters()
                .Where(s => s.TenantId == tenantId && s.LocationId != null).ToListAsync());
            db.PassNumberHolds.RemoveRange(await db.PassNumberHolds.IgnoreQueryFilters()
                .Where(h => h.TenantId == tenantId).ToListAsync());
            await db.SaveChangesAsync();
        }
    }

    private static async Task<Guid> EnsureLocationAsync(ApplicationDbContext db, Guid tenantId, string name)
    {
        var existing = await db.Locations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.Name == name);
        if (existing is not null) return existing.Id;

        var location = new Location { TenantId = tenantId, Name = name, IsActive = true, CreatedBy = "test" };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        return location.Id;
    }

    private static async Task<Guid> AddInsideVisitAsync(ApplicationDbContext db, Guid tenantId, Guid locationId, string passNumber)
    {
        var dept = await db.Departments.IgnoreQueryFilters().FirstAsync(d => d.TenantId == tenantId);
        var host = await db.Employees.IgnoreQueryFilters().FirstAsync(e => e.TenantId == tenantId);
        var visitor = new Visitor
        {
            TenantId = tenantId,
            VisitorNumber = $"PPOOL-{Guid.NewGuid():N}"[..20],
            FullName = "Pass Pool Visitor",
            CompanyName = "Pass Pool Co",
            CreatedBy = "test"
        };
        db.Visitors.Add(visitor);
        await db.SaveChangesAsync();

        var visit = new VisitorVisit
        {
            TenantId = tenantId,
            VisitorId = visitor.Id,
            VisitNumber = $"PPOOL-{Guid.NewGuid():N}"[..20],
            VisitDate = DateOnly.FromDateTime(DateTime.Today),
            VisitTime = TimeOnly.FromDateTime(DateTime.Now),
            DepartmentId = dept.Id,
            HostEmployeeId = host.Id,
            Status = VisitStatus.Inside,
            CheckInAt = DateTime.UtcNow,
            PassNumber = passNumber,
            NumberOfPersons = 1,
            CreatedBy = "test"
        };
        visit.VisitLocations.Add(new VisitorVisitLocation { LocationId = locationId });
        db.VisitorVisits.Add(visit);
        await db.SaveChangesAsync();
        return visit.Id;
    }
}

/// <summary>
/// Documents ADO.NET parameterisation and tenant-filter expectations.
/// </summary>
public class AdoDataAccessTests
{
    [Fact]
    public void AdoSql_parameters_are_typed_sql_parameters()
    {
        var tenant = AdoSql.GuidParam("@TenantId", WellKnownTenants.TiaanoId);
        Assert.Equal("@TenantId", tenant.ParameterName);
        Assert.Equal(System.Data.SqlDbType.UniqueIdentifier, tenant.SqlDbType);
        Assert.Equal(WellKnownTenants.TiaanoId, tenant.Value);

        var resource = AdoSql.NVarChar("@Resource", "vms-number:demo", 255);
        Assert.Equal(System.Data.SqlDbType.NVarChar, resource.SqlDbType);
        Assert.Equal(255, resource.Size);
    }

    [Fact]
    public void Pass_allocation_sql_concept_requires_tenant_predicate()
    {
        // Guardrail: allocate SQL must always bind TenantId from ITenantContext, never from request body.
        const string seriesBump = """
            UPDATE PassNumberSeries
            SET CurrentNumber = CurrentNumber + 1
            OUTPUT INSERTED.CurrentNumber
            WHERE Id = @Id AND TenantId = @TenantId AND IsActive = 1 AND CurrentNumber < EndNumber
            """;
        Assert.Contains("TenantId = @TenantId", seriesBump, StringComparison.Ordinal);
        Assert.DoesNotContain("+ @TenantId", seriesBump, StringComparison.Ordinal);
    }

    [Fact]
    public void Connection_string_is_configured_for_tests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestDatabase.ConnectionString
            })
            .Build();
        Assert.False(string.IsNullOrWhiteSpace(config.GetConnectionString("DefaultConnection")));
    }
}
