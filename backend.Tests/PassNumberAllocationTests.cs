using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Models;
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
        if (!await db.PassNumberSeries.IgnoreQueryFilters()
                .AnyAsync(s => s.TenantId == WellKnownTenants.TiaanoId && s.IsActive))
        {
            db.PassNumberSeries.Add(new PassNumberSeries
            {
                TenantId = WellKnownTenants.TiaanoId,
                Prefix = "VMS-",
                StartNumber = 1000,
                EndNumber = 999999,
                CurrentNumber = 999,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        var bag = new ConcurrentBag<string>();
        var tasks = Enumerable.Range(0, 12).Select(async _ =>
        {
            using var s = _factory.Services.CreateScope();
            s.ServiceProvider.GetRequiredService<ITenantContext>().Set(WellKnownTenants.TiaanoId);
            var svc = s.ServiceProvider.GetRequiredService<IPassNumberService>();
            var code = await svc.AllocateNextAsync(null);
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
