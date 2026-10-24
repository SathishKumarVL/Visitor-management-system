using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Services;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>Purpose fields that search an external API configured in Settings; the API itself is stubbed.</summary>
[Collection("CoreWorkflow")]
public class LookupSourceApiTests : IClassFixture<TestApiFactory>
{
    private const string ApiKey = "school-key-123";
    private const string GuardianMobile = "9000000077";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly SchoolApiStub _school = new();
    private readonly WebApplicationFactory<Program> _factory;

    public LookupSourceApiTests(TestApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.AddHttpClient(LookupService.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new SchoolApiHandler(_school))));
    }

    [Fact]
    public async Task Sources_Are_SuperAdmin_Only_Keep_The_Key_Secret_And_Refuse_Unsafe_Urls()
    {
        var reception = await LoginAsync("reception");
        Assert.Equal(HttpStatusCode.Forbidden, (await reception.GetAsync("/api/settings/lookup-sources")).StatusCode);

        var admin = await LoginAsync("superadmin");
        var unsafeUrl = await admin.PostAsJsonAsync("/api/settings/lookup-sources", SourceBody($"Unsafe {Guid.NewGuid():N}", url: "https://127.0.0.1/s?q={query}"));
        Assert.Equal(HttpStatusCode.BadRequest, unsafeUrl.StatusCode);
        Assert.Contains("private network", await unsafeUrl.Content.ReadAsStringAsync());

        var sourceId = await CreateSourceAsync(admin, block: false);
        try
        {
            var list = await admin.GetStringAsync("/api/settings/lookup-sources");
            Assert.DoesNotContain(ApiKey, list);
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.LookupSources.IgnoreQueryFilters().FirstAsync(s => s.Id == sourceId);
            Assert.NotNull(row.ApiKeyEncrypted);
            Assert.DoesNotContain(ApiKey, row.ApiKeyEncrypted);

            var test = await admin.PostAsJsonAsync("/api/settings/lookup-sources/test", new
            {
                id = sourceId,
                source = SourceBody("Test run", apiKey: null),
                query = "arj",
            });
            Assert.True(test.IsSuccessStatusCode, await test.Content.ReadAsStringAsync());
            var result = (await test.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
            Assert.True(result.GetProperty("ok").GetBoolean(), result.ToString());
            Assert.Equal(2, result.GetProperty("recordCount").GetInt32());
            Assert.Contains("students", result.GetProperty("rawSample").GetString());
            var first = result.GetProperty("matches")[0];
            Assert.Equal("Arjun K - Class 7-B", first.GetProperty("display").GetString());
            Assert.Contains(first.GetProperty("matchValues").EnumerateArray(), v => v.GetString()!.Contains("90000 00077"));
            Assert.Equal(ApiKey, _school.LastApiKey);
        }
        finally
        {
            await DeleteSourceAsync(admin, sourceId);
        }
    }

    [Fact]
    public async Task Picked_Record_Is_Saved_Verified_And_Typed_Value_Is_Not()
    {
        var admin = await LoginAsync("superadmin");
        var reception = await LoginAsync("reception");
        var sourceId = await CreateSourceAsync(admin, block: false);
        var (purposeId, fieldId, purposeName) = await CreatePurposeAsync(admin, sourceId);
        try
        {
            var search = await reception.GetAsync($"/api/lookups/{sourceId}/search?q=arj");
            var searchBody = await search.Content.ReadAsStringAsync();
            Assert.True(search.IsSuccessStatusCode, searchBody);
            Assert.DoesNotContain(GuardianMobile[^5..], searchBody);
            Assert.Equal("arj", _school.LastQuery);
            var matches = JsonDocument.Parse(searchBody).RootElement.GetProperty("data");
            Assert.Equal(2, matches.GetArrayLength());
            var token = matches[0].GetProperty("token").GetString()!;
            Assert.True(matches[0].GetProperty("hasMatchCheck").GetBoolean());

            Assert.Empty((await reception.GetFromJsonAsync<JsonElement>($"/api/lookups/{sourceId}/search?q=ar", JsonOptions)).GetProperty("data").EnumerateArray());

            var check = await reception.PostAsJsonAsync("/api/lookups/check", new { token, telephone = GuardianMobile });
            Assert.True((await check.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data").GetProperty("matched").GetBoolean());

            var picked = await RegisterAsync(reception, purposeId, GuardianMobile, new { fieldId, value = "Arjun K - Class 7-B", lookupToken = token });
            var value = Assert.Single(picked.GetProperty("purposeFields").EnumerateArray());
            Assert.Equal(purposeName, value.GetProperty("purpose").GetString());
            Assert.Equal("Arjun K - Class 7-B", value.GetProperty("value").GetString());
            Assert.True(value.GetProperty("verified").GetBoolean());
            Assert.True(value.GetProperty("matched").GetBoolean());
            Assert.Equal("A101", value.GetProperty("recordId").GetString());
            Assert.Contains(value.GetProperty("details").EnumerateArray(), d => d.GetProperty("label").GetString() == "Class" && d.GetProperty("value").GetString() == "7");

            var typed = await RegisterAsync(reception, purposeId, "9000000078", new { fieldId, value = "Some Student" });
            var typedValue = Assert.Single(typed.GetProperty("purposeFields").EnumerateArray());
            Assert.False(typedValue.GetProperty("verified").GetBoolean());

            var forged = await reception.PostAsJsonAsync("/api/visitors", await RegisterBodyAsync(purposeId, GuardianMobile, new { fieldId, value = "X", lookupToken = token[..^4] + "AAAA" }));
            Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
            Assert.Contains("expired", await forged.Content.ReadAsStringAsync());
        }
        finally
        {
            await RetirePurposeAsync(purposeId);
            await DeleteSourceAsync(admin, sourceId);
        }
    }

    [Fact]
    public async Task Blocking_Check_Refuses_A_Visitor_Whose_Mobile_Is_Not_On_The_Record()
    {
        var admin = await LoginAsync("superadmin");
        var reception = await LoginAsync("reception");
        var sourceId = await CreateSourceAsync(admin, block: true);
        var (purposeId, fieldId, _) = await CreatePurposeAsync(admin, sourceId);
        try
        {
            var matches = (await reception.GetFromJsonAsync<JsonElement>($"/api/lookups/{sourceId}/search?q=arj", JsonOptions)).GetProperty("data");
            var token = matches[0].GetProperty("token").GetString()!;

            var refused = await reception.PostAsJsonAsync("/api/visitors", await RegisterBodyAsync(purposeId, "9000000079", new { fieldId, lookupToken = token }));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("not on the record", await refused.Content.ReadAsStringAsync());

            await RegisterAsync(reception, purposeId, GuardianMobile, new { fieldId, lookupToken = token });
        }
        finally
        {
            await RetirePurposeAsync(purposeId);
            await DeleteSourceAsync(admin, sourceId);
        }
    }

    [Fact]
    public async Task Post_Source_Sends_A_Json_Body_With_The_Typed_Name_And_Other_Field_Values()
    {
        var admin = await LoginAsync("superadmin");
        var reception = await LoginAsync("reception");

        var badBody = await admin.PostAsJsonAsync("/api/settings/lookup-sources", new
        {
            name = $"Bad body {Guid.NewGuid():N}",
            searchUrl = "https://api.school.example.test/api/Student/StudentBasicInfo",
            httpMethod = "POST",
            bodyTemplate = """{ "fullName": {query} }""",
            idPath = "admissionNo",
            displayTemplate = "{fullName}",
            minChars = 3,
            timeoutSeconds = 5,
            isActive = true,
        });
        Assert.Equal(HttpStatusCode.BadRequest, badBody.StatusCode);
        Assert.Contains("not valid JSON", await badBody.Content.ReadAsStringAsync());

        var create = await admin.PostAsJsonAsync("/api/settings/lookup-sources", new
        {
            name = $"Students {Guid.NewGuid():N}",
            searchUrl = "https://api.school.example.test/api/Student/StudentBasicInfo",
            httpMethod = "POST",
            bodyTemplate = """{ "class": "{field:Class}", "section": "{field:Section}", "fullName": "{query}" }""",
            apiKeyHeader = "Authorization",
            apiKey = "Bearer " + ApiKey,
            resultsPath = "data",
            idPath = "admissionNo",
            displayTemplate = "{fullName} - Class {class}-{section} (Adm {admissionNo})",
            minChars = 3,
            timeoutSeconds = 5,
            isActive = true,
        });
        var created = await create.Content.ReadAsStringAsync();
        Assert.True(create.IsSuccessStatusCode, created);
        var source = JsonDocument.Parse(created).RootElement.GetProperty("data");
        Assert.Equal("POST", source.GetProperty("httpMethod").GetString());
        var sourceId = source.GetProperty("id").GetGuid();
        try
        {
            var search = await reception.PostAsJsonAsync($"/api/lookups/{sourceId}/search", new
            {
                query = "rak",
                context = new Dictionary<string, string> { ["Class"] = "1st", ["Section"] = "A", ["Unused"] = "x" },
            });
            var body = await search.Content.ReadAsStringAsync();
            Assert.True(search.IsSuccessStatusCode, body);
            Assert.Equal("Bearer " + ApiKey, _school.LastAuthorization);
            using (var sent = JsonDocument.Parse(_school.LastBody!))
            {
                Assert.Equal("rak", sent.RootElement.GetProperty("fullName").GetString());
                Assert.Equal("1st", sent.RootElement.GetProperty("class").GetString());
                Assert.Equal("A", sent.RootElement.GetProperty("section").GetString());
            }
            var match = Assert.Single(JsonDocument.Parse(body).RootElement.GetProperty("data").EnumerateArray());
            Assert.Equal("RAKSHAN - Class 1st-A (Adm 94623)", match.GetProperty("display").GetString());
            Assert.Equal("94623", match.GetProperty("recordId").GetString());
        }
        finally
        {
            await DeleteSourceAsync(admin, sourceId);
        }
    }

    [Fact]
    public async Task Returning_Visitor_Gets_Previous_Answers_And_The_Pick_Is_Checked_Again()
    {
        var admin = await LoginAsync("superadmin");
        var reception = await LoginAsync("reception");
        var sourceId = await CreatePostSourceAsync(admin);
        var (purposeId, fields) = await CreateClassSectionStudentPurposeAsync(admin, sourceId);
        try
        {
            var search = await reception.PostAsJsonAsync($"/api/lookups/{sourceId}/search", new
            {
                query = "rak",
                context = new Dictionary<string, string> { ["Class"] = "1st", ["Section"] = "A" },
            });
            var token = JsonDocument.Parse(await search.Content.ReadAsStringAsync()).RootElement.GetProperty("data")[0].GetProperty("token").GetString()!;
            var visit = await RegisterAsync(reception, purposeId, "9000000081",
                new { fieldId = fields["Class"], value = "1st" },
                new { fieldId = fields["Section"], value = "A" },
                new { fieldId = fields["Student"], lookupToken = token });
            var visitorId = visit.GetProperty("visitorId").GetGuid();

            var host = await LoginAsync("host");
            Assert.Equal(HttpStatusCode.Forbidden, (await host.GetAsync($"/api/visitors/previous-purpose-answers?visitorId={visitorId}")).StatusCode);

            var previous = (await reception.GetFromJsonAsync<JsonElement>($"/api/visitors/previous-purpose-answers?visitorId={visitorId}", JsonOptions)).GetProperty("data");
            var set = Assert.Single(previous.EnumerateArray());
            Assert.Equal(purposeId, set.GetProperty("purposeId").GetGuid());
            var answers = set.GetProperty("fields").EnumerateArray().ToDictionary(f => f.GetProperty("label").GetString()!);
            Assert.Equal("1st", answers["Class"].GetProperty("value").GetString());
            var student = answers["Student"];
            Assert.Equal("RAKSHAN - Class 1st-A (Adm 94623)", student.GetProperty("value").GetString());
            Assert.Equal(sourceId, student.GetProperty("lookupSourceId").GetGuid());
            Assert.Equal("94623", student.GetProperty("recordId").GetString());
            Assert.Equal("rak", student.GetProperty("query").GetString());

            var recheck = await reception.PostAsJsonAsync($"/api/lookups/{sourceId}/recheck", new
            {
                query = "rak",
                context = new Dictionary<string, string> { ["Class"] = "1st", ["Section"] = "A" },
                recordId = "94623",
            });
            var fresh = (await recheck.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
            Assert.Equal("94623", fresh.GetProperty("recordId").GetString());
            var freshToken = fresh.GetProperty("token").GetString()!;
            Assert.NotEmpty(freshToken);

            var promoted = await reception.PostAsJsonAsync($"/api/lookups/{sourceId}/recheck", new
            {
                query = "rak",
                context = new Dictionary<string, string> { ["Class"] = "2nd", ["Section"] = "A" },
                recordId = "94623",
            });
            Assert.True(promoted.IsSuccessStatusCode, await promoted.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Null, (await promoted.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data").ValueKind);

            var again = await RegisterAsync(reception, purposeId, "9000000082",
                new { fieldId = fields["Class"], value = "1st" },
                new { fieldId = fields["Section"], value = "A" },
                new { fieldId = fields["Student"], lookupToken = freshToken });
            var saved = again.GetProperty("purposeFields").EnumerateArray().Single(f => f.GetProperty("label").GetString() == "Student");
            Assert.True(saved.GetProperty("verified").GetBoolean());
        }
        finally
        {
            await RetirePurposeAsync(purposeId);
            await DeleteSourceAsync(admin, sourceId);
        }
    }

    [Fact]
    public async Task A_New_Person_Coming_For_A_Student_Is_Flagged()
    {
        var admin = await LoginAsync("superadmin");
        var reception = await LoginAsync("reception");
        var sourceId = await CreatePostSourceAsync(admin);
        var (purposeId, fields) = await CreateClassSectionStudentPurposeAsync(admin, sourceId);
        try
        {
            async Task<string> PickAsync()
            {
                var search = await reception.PostAsJsonAsync($"/api/lookups/{sourceId}/search", new
                {
                    query = "rak",
                    context = new Dictionary<string, string> { ["Class"] = "1st", ["Section"] = "A" },
                });
                return JsonDocument.Parse(await search.Content.ReadAsStringAsync()).RootElement.GetProperty("data")[0].GetProperty("token").GetString()!;
            }
            object[] Answers(string token) =>
            [
                new { fieldId = fields["Class"], value = "1st" },
                new { fieldId = fields["Section"], value = "A" },
                new { fieldId = fields["Student"], lookupToken = token },
            ];
            string RecordVisitorsUrl(Guid? visitorId) =>
                $"/api/visitors/record-visitors?sourceId={sourceId}&recordId=94623" + (visitorId is Guid id ? $"&visitorId={id}" : "");

            var none = (await reception.GetFromJsonAsync<JsonElement>(RecordVisitorsUrl(null), JsonOptions)).GetProperty("data");
            Assert.False(none.GetProperty("isNewPerson").GetBoolean());
            Assert.Empty(none.GetProperty("visitors").EnumerateArray());

            var first = await RegisterAsync(reception, purposeId, "9000000083", Answers(await PickAsync()));
            var firstStudent = first.GetProperty("purposeFields").EnumerateArray().Single(f => f.GetProperty("label").GetString() == "Student");
            Assert.NotEqual(JsonValueKind.True, firstStudent.GetProperty("newPerson").ValueKind);
            var father = first.GetProperty("visitorId").GetGuid();

            var notCheckedIn = (await reception.GetFromJsonAsync<JsonElement>(RecordVisitorsUrl(null), JsonOptions)).GetProperty("data");
            Assert.Empty(notCheckedIn.GetProperty("visitors").EnumerateArray());

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var visitId = first.GetProperty("visitId").GetGuid();
                var visit = await db.VisitorVisits.IgnoreQueryFilters().FirstAsync(v => v.Id == visitId);
                visit.CheckInAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            var known = (await reception.GetFromJsonAsync<JsonElement>(RecordVisitorsUrl(father), JsonOptions)).GetProperty("data");
            Assert.False(known.GetProperty("isNewPerson").GetBoolean());
            var earlier = Assert.Single(known.GetProperty("visitors").EnumerateArray());
            Assert.Equal(father, earlier.GetProperty("visitorId").GetGuid());
            Assert.Equal(1, earlier.GetProperty("visits").GetInt32());

            var stranger = (await reception.GetFromJsonAsync<JsonElement>(RecordVisitorsUrl(null), JsonOptions)).GetProperty("data");
            Assert.True(stranger.GetProperty("isNewPerson").GetBoolean());

            var host = await LoginAsync("host");
            Assert.Equal(HttpStatusCode.Forbidden, (await host.GetAsync(RecordVisitorsUrl(null))).StatusCode);

            var second = await RegisterAsync(reception, purposeId, "9000000084", Answers(await PickAsync()));
            var secondStudent = second.GetProperty("purposeFields").EnumerateArray().Single(f => f.GetProperty("label").GetString() == "Student");
            Assert.True(secondStudent.GetProperty("newPerson").GetBoolean());
        }
        finally
        {
            await RetirePurposeAsync(purposeId);
            await DeleteSourceAsync(admin, sourceId);
        }
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<Guid> CreatePostSourceAsync(HttpClient admin)
    {
        var create = await admin.PostAsJsonAsync("/api/settings/lookup-sources", new
        {
            name = $"Students {Guid.NewGuid():N}",
            searchUrl = "https://api.school.example.test/api/Student/StudentBasicInfo",
            httpMethod = "POST",
            bodyTemplate = """{ "class": "{field:Class}", "section": "{field:Section}", "fullName": "{query}" }""",
            apiKeyHeader = "Authorization",
            apiKey = "Bearer " + ApiKey,
            resultsPath = "data",
            idPath = "admissionNo",
            displayTemplate = "{fullName} - Class {class}-{section} (Adm {admissionNo})",
            minChars = 3,
            timeoutSeconds = 5,
            isActive = true,
        });
        var body = await create.Content.ReadAsStringAsync();
        Assert.True(create.IsSuccessStatusCode, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("id").GetGuid();
    }

    private async Task<(Guid PurposeId, Dictionary<string, Guid> Fields)> CreateClassSectionStudentPurposeAsync(HttpClient admin, Guid sourceId)
    {
        Guid locationId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            locationId = await db.Locations.IgnoreQueryFilters()
                .Where(l => l.IsActive && l.TenantId == WellKnownTenants.TiaanoId)
                .Select(l => l.Id)
                .FirstAsync();
        }
        var create = await admin.PostAsJsonAsync("/api/masters/purposes", new
        {
            name = $"Parent visit {Guid.NewGuid():N}",
            sortOrder = 9999,
            isActive = true,
            locationId,
            fields = new object[]
            {
                new { label = "Class", isRequired = true, type = "text" },
                new { label = "Section", isRequired = true, type = "text" },
                new { label = "Student", isRequired = true, type = "lookup", lookupSourceId = sourceId },
            },
        });
        Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync());
        var purpose = (await create.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        var fields = purpose.GetProperty("fields").EnumerateArray()
            .ToDictionary(f => f.GetProperty("label").GetString()!, f => f.GetProperty("id").GetGuid());
        return (purpose.GetProperty("id").GetGuid(), fields);
    }

    private static object SourceBody(string name, string url = "https://school.example.test/api/students?search={query}",
        string? apiKey = ApiKey, bool block = false) => new
    {
        name,
        searchUrl = url,
        apiKeyHeader = "X-Api-Key",
        apiKey,
        resultsPath = "data.students",
        idPath = "admissionNo",
        displayTemplate = "{name} - Class {class}-{section}",
        savedFields = new[] { new { path = "class", label = "Class" }, new { path = "section", label = "Section" } },
        matchPath = "guardians.mobile",
        matchAgainst = "mobile",
        blockOnMismatch = block,
        minChars = 3,
        timeoutSeconds = 5,
        isActive = true,
    };

    private async Task<Guid> CreateSourceAsync(HttpClient admin, bool block)
    {
        var response = await admin.PostAsJsonAsync("/api/settings/lookup-sources", SourceBody($"School {Guid.NewGuid():N}", block: block));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        var data = JsonDocument.Parse(body).RootElement.GetProperty("data");
        Assert.True(data.GetProperty("hasApiKey").GetBoolean());
        Assert.DoesNotContain(ApiKey, body);
        return data.GetProperty("id").GetGuid();
    }

    private async Task<(Guid PurposeId, Guid FieldId, string Name)> CreatePurposeAsync(HttpClient admin, Guid sourceId)
    {
        Guid locationId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            locationId = await db.Locations.IgnoreQueryFilters()
                .Where(l => l.IsActive && l.TenantId == WellKnownTenants.TiaanoId)
                .Select(l => l.Id)
                .FirstAsync();
        }
        var name = $"Student visit {Guid.NewGuid():N}";
        var create = await admin.PostAsJsonAsync("/api/masters/purposes", new
        {
            name,
            sortOrder = 9999,
            isActive = true,
            locationId,
            fields = new[] { new { label = "Student", isRequired = true, type = "lookup", lookupSourceId = sourceId } },
        });
        Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync());
        var purpose = (await create.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        var field = Assert.Single(purpose.GetProperty("fields").EnumerateArray());
        Assert.Equal("lookup", field.GetProperty("type").GetString());
        Assert.Equal(sourceId, field.GetProperty("lookupSourceId").GetGuid());
        return (purpose.GetProperty("id").GetGuid(), field.GetProperty("id").GetGuid(), name);
    }

    private async Task<object> RegisterBodyAsync(Guid purposeId, string telephone, params object[] fields)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var host = await db.Employees.IgnoreQueryFilters()
            .Where(e => e.IsActive && e.TenantId == WellKnownTenants.TiaanoId)
            .OrderBy(e => e.FullName)
            .FirstAsync();
        return new
        {
            visitorName = "Lookup Parent",
            gender = 1,
            companyName = "Parent",
            telephone,
            email = $"{Guid.NewGuid():N}@example.test",
            departmentId = host.DepartmentId,
            hostEmployeeId = host.Id,
            purposeIds = new[] { purposeId },
            purposeFields = fields,
            locationIds = Array.Empty<Guid>(),
            numberOfPersons = 1,
            isWalkIn = true,
            skipBookingMatch = true,
            idTypeName = "Aadhaar",
            idNumber = "123456789012",
            photoBase64 = TestPhotos.Base64($"lookup-{Guid.NewGuid():N}"),
        };
    }

    private async Task<JsonElement> RegisterAsync(HttpClient reception, Guid purposeId, string telephone, params object[] fields)
    {
        var saved = await reception.PostAsJsonAsync("/api/visitors", await RegisterBodyAsync(purposeId, telephone, fields));
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var visitId = (await saved.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data").GetProperty("visitId").GetGuid();
        return (await reception.GetFromJsonAsync<JsonElement>($"/api/visitors/{visitId}", JsonOptions)).GetProperty("data");
    }

    private async Task RetirePurposeAsync(Guid purposeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.VisitPurposes.IgnoreQueryFilters().FirstAsync(p => p.Id == purposeId);
        row.IsActive = false;
        row.FieldsJson = null;
        await db.SaveChangesAsync();
    }

    private static async Task DeleteSourceAsync(HttpClient admin, Guid sourceId)
    {
        var response = await admin.DeleteAsync($"/api/settings/lookup-sources/{sourceId}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<HttpClient> LoginAsync(string username)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.Users.IgnoreQueryFilters().FirstAsync(u => u.UserName == username);
            var reset = await users.GeneratePasswordResetTokenAsync(user);
            await users.ResetPasswordAsync(user, reset, TestSecrets.SeedPassword);
            user.MustChangePassword = false;
            user.IsActive = true;
            await users.UpdateAsync(user);
        }
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password = TestSecrets.SeedPassword, rememberMe = false });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("data").GetProperty("token").GetString());
        return client;
    }

    private sealed class SchoolApiStub
    {
        public string? LastApiKey { get; set; }
        public string? LastQuery { get; set; }
        public string? LastAuthorization { get; set; }
        public string? LastBody { get; set; }
    }

    private sealed class SchoolApiHandler(SchoolApiStub stub) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post)
            {
                stub.LastAuthorization = request.Headers.TryGetValues("Authorization", out var auth) ? auth.FirstOrDefault() : null;
                stub.LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
                if (stub.LastAuthorization != "Bearer " + ApiKey || request.Content?.Headers.ContentType?.MediaType != "application/json")
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                using var sent = JsonDocument.Parse(stub.LastBody ?? "{}");
                var inFirstClass = !sent.RootElement.TryGetProperty("class", out var cls) || cls.GetString() is "" or "1st";
                var students = inFirstClass
                    ? """{ "message": "success", "data": [ { "fullName": "RAKSHAN", "class": "1st", "section": "A", "admissionNo": "94623" } ] }"""
                    : """{ "message": "success", "data": [] }""";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(students, Encoding.UTF8, "application/json") };
            }
            return HandleGet(request);
        }

        private HttpResponseMessage HandleGet(HttpRequestMessage request)
        {
            stub.LastApiKey = request.Headers.TryGetValues("X-Api-Key", out var keys) ? keys.FirstOrDefault() : null;
            stub.LastQuery = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["search"];
            if (stub.LastApiKey != ApiKey)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            const string json = """
                { "data": { "students": [
                    { "admissionNo": "A101", "name": "Arjun K", "class": 7, "section": "B",
                      "guardians": [ { "name": "Father", "mobile": "+91 90000 00077" } ] },
                    { "admissionNo": "A102", "name": "Arjun S", "class": 9, "section": "A", "guardians": [] }
                ] } }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }
}
