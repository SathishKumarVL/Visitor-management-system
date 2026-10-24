using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;
using Tiaano.Vms.Api.Models.Product;
using Tiaano.Vms.Api.Services;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>
/// "Clear all" on the master pages, run in a tenant of its own because it empties a master tenant-wide.
/// </summary>
public class MasterClearTests : IClassFixture<TestApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid TenantCId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string TenantCAdmin = "tenantc-admin";
    private const string TenantCPassword = "TenantC_ClearPass1!";
    private readonly TestApiFactory _factory;

    public MasterClearTests(TestApiFactory factory) => _factory = factory;

    private sealed record Seeded(
        Guid Location, Guid Department, Guid SharedDepartment, Guid Host, Guid OtherHost, Guid Purpose, Guid Others,
        Guid Hall, Guid Question, Guid Visitor, Guid VisitAtLocation, Guid VisitElsewhere, string HostLogin, string ReceptionLogin);

    [Fact]
    public async Task Only_A_Super_Admin_Can_Clear_And_Must_Type_Delete()
    {
        var seeded = await SeedAsync();
        var admin = await LoginAsync("admin", TestSecrets.SeedPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/masters/employees/clear-preview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PostAsJsonAsync("/api/masters/employees/clear", new { confirm = "DELETE" })).StatusCode);

        var superAdmin = await LoginAsync(TenantCAdmin, TenantCPassword);
        var unconfirmed = await superAdmin.PostAsJsonAsync("/api/masters/employees/clear", new { confirm = "delete" });
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await superAdmin.PostAsJsonAsync("/api/masters/visitors/clear", new { confirm = "DELETE" })).StatusCode);

        await using var scope = await DbAsync();
        Assert.True(await scope.Db.Employees.IgnoreQueryFilters().AnyAsync(e => e.Id == seeded.Host));
    }

    [Fact]
    public async Task Clearing_Locations_Takes_Everything_At_Those_Locations()
    {
        var seeded = await SeedAsync();
        var tenantACounts = await TenantACountsAsync();
        var superAdmin = await LoginAsync(TenantCAdmin, TenantCPassword);

        var preview = await DataAsync(await superAdmin.GetAsync("/api/masters/locations/clear-preview"));
        Assert.True(Count(preview, "locations") >= 1);
        Assert.True(Count(preview, "visits and bookings") >= 1);
        Assert.True(Count(preview, "logins") >= 2);

        var cleared = await DataAsync(await superAdmin.PostAsJsonAsync("/api/masters/locations/clear", new { confirm = "DELETE" }));
        Assert.Equal(Count(preview, "locations"), Count(cleared, "locations"));

        await using var scope = await DbAsync();
        var db = scope.Db;
        Assert.False(await db.Locations.IgnoreQueryFilters().AnyAsync(l => l.TenantId == TenantCId));
        Assert.False(await db.Departments.IgnoreQueryFilters().AnyAsync(d => d.Id == seeded.Department));
        Assert.False(await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.Id == seeded.Host));
        Assert.False(await db.VisitPurposes.IgnoreQueryFilters().AnyAsync(p => p.Id == seeded.Purpose));
        Assert.False(await db.MeetingHalls.IgnoreQueryFilters().AnyAsync(h => h.Id == seeded.Hall));
        Assert.False(await db.FeedbackQuestions.IgnoreQueryFilters().AnyAsync(q => q.Id == seeded.Question));
        Assert.False(await db.VisitorVisits.IgnoreQueryFilters().AnyAsync(v => v.Id == seeded.VisitAtLocation));
        Assert.False(await db.VisitFeedbacks.IgnoreQueryFilters().AnyAsync(f => f.VisitorVisitId == seeded.VisitAtLocation));
        Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == seeded.HostLogin || u.Id == seeded.ReceptionLogin));

        // Shared records and the visitor's other visit are not at the location, so they stay.
        Assert.True(await db.Departments.IgnoreQueryFilters().AnyAsync(d => d.Id == seeded.SharedDepartment));
        Assert.True(await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.Id == seeded.OtherHost));
        Assert.True(await db.VisitorVisits.IgnoreQueryFilters().AnyAsync(v => v.Id == seeded.VisitElsewhere));
        Assert.True(await db.Visitors.IgnoreQueryFilters().AnyAsync(v => v.Id == seeded.Visitor));
        Assert.True(await db.VisitPurposes.IgnoreQueryFilters().AnyAsync(p => p.Id == seeded.Others));
        Assert.True(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.UserName == TenantCAdmin));
        Assert.Equal(tenantACounts, await TenantACountsAsync());
    }

    [Fact]
    public async Task Clearing_Employees_Removes_Their_Visits_Visitors_And_Host_Logins()
    {
        var seeded = await SeedAsync();
        var tenantACounts = await TenantACountsAsync();
        var superAdmin = await LoginAsync(TenantCAdmin, TenantCPassword);

        var cleared = await DataAsync(await superAdmin.PostAsJsonAsync("/api/masters/employees/clear", new { confirm = "DELETE" }));
        Assert.True(Count(cleared, "employees") >= 2);
        Assert.True(Count(cleared, "visitors (with their photos and face data)") >= 1);

        await using var scope = await DbAsync();
        var db = scope.Db;
        Assert.False(await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.TenantId == TenantCId));
        Assert.False(await db.VisitorVisits.IgnoreQueryFilters().AnyAsync(v => v.TenantId == TenantCId));
        Assert.False(await db.Visitors.IgnoreQueryFilters().AnyAsync(v => v.Id == seeded.Visitor));
        Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == seeded.HostLogin));

        Assert.True(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == seeded.ReceptionLogin));
        Assert.True(await db.Departments.IgnoreQueryFilters().AnyAsync(d => d.Id == seeded.Department));
        Assert.True(await db.Locations.IgnoreQueryFilters().AnyAsync(l => l.Id == seeded.Location));
        Assert.True(await db.FeedbackQuestions.IgnoreQueryFilters().AnyAsync(q => q.Id == seeded.Question));
        Assert.Equal(tenantACounts, await TenantACountsAsync());
    }

    [Fact]
    public async Task Clearing_Purposes_Keeps_Others_And_The_Visits_That_Only_Used_It()
    {
        var seeded = await SeedAsync();
        var superAdmin = await LoginAsync(TenantCAdmin, TenantCPassword);

        await DataAsync(await superAdmin.PostAsJsonAsync("/api/masters/purposes/clear", new { confirm = "DELETE" }));

        await using var scope = await DbAsync();
        var db = scope.Db;
        Assert.False(await db.VisitPurposes.IgnoreQueryFilters().AnyAsync(p => p.TenantId == TenantCId && p.Id != seeded.Others));
        Assert.True(await db.VisitPurposes.IgnoreQueryFilters().AnyAsync(p => p.Id == seeded.Others));
        Assert.False(await db.VisitorVisits.IgnoreQueryFilters().AnyAsync(v => v.Id == seeded.VisitAtLocation));
        Assert.True(await db.VisitorVisits.IgnoreQueryFilters().AnyAsync(v => v.Id == seeded.VisitElsewhere));
        Assert.True(await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.Id == seeded.Host));
    }

    [Fact]
    public async Task Departments_Halls_Questions_And_Designations_Can_Each_Be_Cleared()
    {
        var seeded = await SeedAsync();
        await using (var scope = await DbAsync())
        {
            scope.Db.Designations.Add(new Designation { TenantId = TenantCId, Name = $"Manager {Guid.NewGuid():N}"[..20] });
            await scope.Db.SaveChangesAsync();
        }
        var superAdmin = await LoginAsync(TenantCAdmin, TenantCPassword);

        foreach (var type in new[] { "feedback-questions", "meeting-halls", "designations", "departments" })
            await DataAsync(await superAdmin.PostAsJsonAsync($"/api/masters/{type}/clear", new { confirm = "DELETE" }));

        await using var check = await DbAsync();
        var db = check.Db;
        Assert.False(await db.FeedbackQuestions.IgnoreQueryFilters().AnyAsync(q => q.TenantId == TenantCId));
        Assert.False(await db.VisitFeedbackAnswers.AnyAsync(a => a.FeedbackQuestionId == seeded.Question));
        Assert.False(await db.MeetingHalls.IgnoreQueryFilters().AnyAsync(h => h.TenantId == TenantCId));
        Assert.False(await db.Designations.IgnoreQueryFilters().AnyAsync(d => d.TenantId == TenantCId));
        Assert.False(await db.Departments.IgnoreQueryFilters().AnyAsync(d => d.TenantId == TenantCId));
        Assert.False(await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.TenantId == TenantCId));
        Assert.False(await db.VisitorVisits.IgnoreQueryFilters().AnyAsync(v => v.TenantId == TenantCId));
        Assert.True(await db.Locations.IgnoreQueryFilters().AnyAsync(l => l.Id == seeded.Location));
        Assert.True(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == seeded.ReceptionLogin));
    }

    [Fact]
    public async Task Deleting_Selected_Employees_Leaves_The_Rest()
    {
        var seeded = await SeedAsync();
        var superAdmin = await LoginAsync(TenantCAdmin, TenantCPassword);
        var ids = new[] { seeded.Host };

        var preview = await DataAsync(await superAdmin.PostAsJsonAsync("/api/masters/employees/clear-preview", new { ids }));
        Assert.Equal(1, Count(preview, "employees"));
        Assert.Equal(1, Count(preview, "visits and bookings"));
        Assert.Equal(0, Count(preview, "visitors (with their photos and face data)"));

        Assert.Equal(HttpStatusCode.BadRequest,
            (await superAdmin.PostAsJsonAsync("/api/masters/employees/clear", new { confirm = "DELETE", ids = Array.Empty<Guid>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await superAdmin.PostAsJsonAsync("/api/masters/purposes/clear", new { confirm = "DELETE", ids = new[] { seeded.Others } })).StatusCode);

        var deleted = await DataAsync(await superAdmin.PostAsJsonAsync("/api/masters/employees/clear", new { confirm = "DELETE", ids }));
        Assert.Equal(1, Count(deleted, "employees"));

        await using var scope = await DbAsync();
        var db = scope.Db;
        Assert.False(await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.Id == seeded.Host));
        Assert.False(await db.VisitorVisits.IgnoreQueryFilters().AnyAsync(v => v.Id == seeded.VisitAtLocation));
        Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == seeded.HostLogin));
        Assert.True(await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.Id == seeded.OtherHost));
        Assert.True(await db.VisitorVisits.IgnoreQueryFilters().AnyAsync(v => v.Id == seeded.VisitElsewhere));
        Assert.True(await db.Visitors.IgnoreQueryFilters().AnyAsync(v => v.Id == seeded.Visitor));
        Assert.True(await db.VisitPurposes.IgnoreQueryFilters().AnyAsync(p => p.Id == seeded.Others));
    }

    // ---------------------------------------------------------------- helpers

    private static int Count(JsonElement summary, string label) =>
        summary.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("label").GetString() == label)
            .Select(i => i.GetProperty("count").GetInt32())
            .FirstOrDefault();

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("data").Clone();
    }

    private async Task<HttpClient> LoginAsync(string username, string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password, rememberMe = false });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", json.GetProperty("data").GetProperty("token").GetString());
        return client;
    }

    private sealed class DbScope : IAsyncDisposable
    {
        private readonly AsyncServiceScope _scope;
        public DbScope(AsyncServiceScope scope) => _scope = scope;
        public ApplicationDbContext Db => _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        public UserManager<ApplicationUser> Users => _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        public ValueTask DisposeAsync() => _scope.DisposeAsync();
    }

    private Task<DbScope> DbAsync() => Task.FromResult(new DbScope(_factory.Services.CreateAsyncScope()));

    private async Task<string> TenantACountsAsync()
    {
        await using var scope = await DbAsync();
        var db = scope.Db;
        var tenantA = WellKnownTenants.TiaanoId;
        return string.Join(",",
            await db.Locations.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantA),
            await db.Departments.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantA),
            await db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantA),
            await db.VisitPurposes.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantA),
            await db.Users.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantA));
    }

    private async Task<string> EnsureUserAsync(UserManager<ApplicationUser> users, string userName, string role, Guid? locationId)
    {
        var user = new ApplicationUser
        {
            UserName = userName,
            Email = $"{userName}@example.test",
            EmailConfirmed = true,
            FullName = userName,
            TenantId = TenantCId,
            LocationId = locationId,
            IsActive = true,
            MustChangePassword = false,
        };
        var created = await users.CreateAsync(user, TenantCPassword);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, role);
        return user.Id;
    }

    private async Task<Seeded> SeedAsync()
    {
        await using var scope = await DbAsync();
        var db = scope.Db;
        var users = scope.Users;
        var sfx = Guid.NewGuid().ToString("N")[..8];

        if (!await db.Tenants.AnyAsync(t => t.Id == TenantCId))
        {
            db.Tenants.Add(new Tenant { Id = TenantCId, Code = "TENANTC", Name = "Tenant C Clear", IsActive = true });
            db.TenantLicenses.Add(new TenantLicense
            {
                TenantId = TenantCId, Edition = "Enterprise", MaxLocations = 50, MaxUsers = 500,
                IsActive = true, Status = "Active", GraceDaysAfterExpiry = 0,
            });
            foreach (var key in new[] { ModuleKeys.VisitorManagement, ModuleKeys.EmergencyManagement })
                db.TenantModuleEntitlements.Add(new TenantModuleEntitlement { TenantId = TenantCId, ModuleKey = key, IsEnabled = true });
            await db.SaveChangesAsync();
        }

        var admin = await users.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.UserName == TenantCAdmin);
        if (admin is null)
        {
            await EnsureUserAsync(users, TenantCAdmin, AppRoles.SuperAdmin, null);
        }
        else
        {
            var token = await users.GeneratePasswordResetTokenAsync(admin);
            await users.ResetPasswordAsync(admin, token, TenantCPassword);
            admin.IsActive = true;
            admin.MustChangePassword = false;
            await users.UpdateAsync(admin);
        }

        var others = await VisitPurposeDefaults.EnsureOthersPurposeAsync(db, TenantCId);
        var location = new Location { TenantId = TenantCId, Name = $"Plant {sfx}", Code = $"P{sfx}" };
        db.Locations.Add(location);
        var department = new Department { TenantId = TenantCId, Name = $"Stores {sfx}", LocationId = location.Id };
        var sharedDepartment = new Department { TenantId = TenantCId, Name = $"Head office {sfx}" };
        db.Departments.AddRange(department, sharedDepartment);
        await db.SaveChangesAsync();

        var hostLogin = await EnsureUserAsync(users, $"tc-host-{sfx}", AppRoles.Host, location.Id);
        var receptionLogin = await EnsureUserAsync(users, $"tc-desk-{sfx}", AppRoles.Reception, location.Id);

        var host = new Employee { TenantId = TenantCId, FullName = $"Host {sfx}", DepartmentId = department.Id, LocationId = location.Id, UserId = hostLogin };
        var otherHost = new Employee { TenantId = TenantCId, FullName = $"Other host {sfx}", DepartmentId = sharedDepartment.Id };
        var purpose = new VisitPurpose { TenantId = TenantCId, Name = $"Delivery {sfx}", LocationId = location.Id };
        var hall = new MeetingHall { TenantId = TenantCId, Code = $"H{sfx}", Name = $"Hall {sfx}", SeatingCapacity = 4, LocationId = location.Id };
        var question = new FeedbackQuestion { TenantId = TenantCId, Prompt = $"How was it {sfx}?", LocationId = location.Id };
        var visitor = new Visitor { TenantId = TenantCId, VisitorNumber = $"TC-{sfx}", FullName = $"Visitor {sfx}", CompanyName = "Acme" };
        db.AddRange(host, otherHost, purpose, hall, question, visitor);
        await db.SaveChangesAsync();

        var atLocation = new VisitorVisit
        {
            TenantId = TenantCId, VisitorId = visitor.Id, VisitNumber = $"TC-{sfx}-1",
            VisitDate = DateOnly.FromDateTime(DateTime.Today), VisitTime = new TimeOnly(10, 0),
            DepartmentId = department.Id, HostEmployeeId = host.Id, MeetingHallId = hall.Id,
            Status = VisitStatus.CheckedOut, CheckedInByUserId = receptionLogin,
        };
        var elsewhere = new VisitorVisit
        {
            TenantId = TenantCId, VisitorId = visitor.Id, VisitNumber = $"TC-{sfx}-2",
            VisitDate = DateOnly.FromDateTime(DateTime.Today), VisitTime = new TimeOnly(11, 0),
            DepartmentId = sharedDepartment.Id, HostEmployeeId = otherHost.Id, Status = VisitStatus.CheckedOut,
        };
        db.VisitorVisits.AddRange(atLocation, elsewhere);
        await db.SaveChangesAsync();
        db.VisitorVisitPurposes.AddRange(
            new VisitorVisitPurpose { VisitorVisitId = atLocation.Id, VisitPurposeId = purpose.Id },
            new VisitorVisitPurpose { VisitorVisitId = elsewhere.Id, VisitPurposeId = others.Id });
        db.VisitorVisitLocations.Add(new VisitorVisitLocation { VisitorVisitId = atLocation.Id, LocationId = location.Id });
        db.VisitFeedbacks.Add(new VisitFeedback
        {
            TenantId = TenantCId, VisitorVisitId = atLocation.Id, VisitorId = visitor.Id,
            VisitorName = visitor.FullName, CompanyName = visitor.CompanyName, VisitNumber = atLocation.VisitNumber,
            Answers = { new VisitFeedbackAnswer { FeedbackQuestionId = question.Id, QuestionText = question.Prompt, Rating = 5 } },
        });
        await db.SaveChangesAsync();

        return new Seeded(location.Id, department.Id, sharedDepartment.Id, host.Id, otherHost.Id, purpose.Id, others.Id,
            hall.Id, question.Id, visitor.Id, atLocation.Id, elsewhere.Id, hostLogin, receptionLogin);
    }
}
