using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tiaano.Vms.Api.Data;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

public class MasterImportTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public MasterImportTests(TestApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("departments", "Name *")]
    [InlineData("employees", "Full Name *")]
    [InlineData("purposes", "Name *")]
    [InlineData("locations", "Name *")]
    [InlineData("feedback-questions", "Question *")]
    public async Task Template_Download_Returns_Xlsx_With_Header_Row(string kind, string firstHeader)
    {
        var client = await LoginAsync("superadmin");
        var response = await client.GetAsync($"/api/masters/import/{kind}/template");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
        using var wb = new XLWorkbook(await response.Content.ReadAsStreamAsync());
        Assert.Equal(firstHeader, wb.Worksheet("Data").Cell(1, 1).GetString());
    }

    [Fact]
    public async Task Department_Upload_Creates_Then_Updates_Without_Duplicates_And_Reports_Bad_Rows()
    {
        var client = await LoginAsync("admin");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var rows = new[]
        {
            new[] { $"Import Dept A {suffix}", $"IDA{suffix}", "101", "", "1", "Yes" },
            new[] { $"Import Dept B {suffix}", $"IDB{suffix}", "", "", "2", "" },
            new[] { $"Import Dept C {suffix}", "", "", "No Such Location", "", "" },
        };
        var headers = new[] { "Name *", "Code", "Intercom", "Location", "Sort Order", "Active" };

        var first = await UploadAsync(client, "departments", headers, rows);
        Assert.Equal(2, first.GetProperty("created").GetInt32());
        Assert.Equal(1, first.GetProperty("failed").GetInt32());
        var error = first.GetProperty("errors")[0];
        Assert.Equal(4, error.GetProperty("row").GetInt32());
        Assert.Contains("No Such Location", error.GetProperty("message").GetString());

        rows[1][2] = "202";
        var second = await UploadAsync(client, "departments", headers, rows.Take(2).ToArray());
        Assert.Equal(0, second.GetProperty("created").GetInt32());
        Assert.Equal(2, second.GetProperty("updated").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var imported = await db.Departments.IgnoreQueryFilters()
            .Where(d => d.Name.EndsWith(suffix)).ToListAsync();
        Assert.Equal(2, imported.Count);
        Assert.Equal("202", imported.Single(d => d.Code == $"IDB{suffix}").Intercom);
    }

    [Fact]
    public async Task Employee_Upload_Resolves_Department_By_Name_And_Validates_Phone()
    {
        var client = await LoginAsync("superadmin");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var deptName = $"Import Emp Dept {suffix}";
        await UploadAsync(client, "departments", new[] { "Name *" }, new[] { new[] { deptName } });

        var headers = new[] { "Full Name *", "Employee Code", "Designation", "Department *", "Location", "Phone", "Email", "Intercom", "Active" };
        var result = await UploadAsync(client, "employees", headers, new[]
        {
            new[] { $"Import Emp One {suffix}", $"E1{suffix}", "Manager", deptName, "", "9876543210", "", "", "Yes" },
            new[] { $"Import Emp Two {suffix}", $"E2{suffix}", "", deptName, "", "12345", "", "", "" },
            new[] { $"Import Emp Three {suffix}", "", "", "Missing Dept", "", "", "", "", "" },
        });

        Assert.Equal(1, result.GetProperty("created").GetInt32());
        Assert.Equal(2, result.GetProperty("failed").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emp = await db.Employees.IgnoreQueryFilters().Include(e => e.Department)
            .SingleAsync(e => e.Code == $"E1{suffix}");
        Assert.Equal(deptName, emp.Department.Name);
        Assert.Equal("9876543210", emp.Phone);
    }

    [Fact]
    public async Task Upload_Missing_Required_Column_Is_Rejected()
    {
        var client = await LoginAsync("admin");
        var response = await PostWorkbookAsync(client, "departments", new[] { "Code" }, new[] { new[] { "X1" } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Missing required column", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admin_Cannot_Import_Employees()
    {
        var client = await LoginAsync("admin");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/masters/import/employees/template")).StatusCode);
    }

    [Fact]
    public async Task Reception_Cannot_Download_Or_Upload()
    {
        var client = await LoginAsync("reception");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/masters/import/departments/template")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PostWorkbookAsync(client, "departments", new[] { "Name *" }, new[] { new[] { "Nope" } })).StatusCode);
    }

    private async Task<JsonElement> UploadAsync(HttpClient client, string kind, string[] headers, string[][] rows)
    {
        var response = await PostWorkbookAsync(client, kind, headers, rows);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("data");
    }

    private static async Task<HttpResponseMessage> PostWorkbookAsync(HttpClient client, string kind, string[] headers, string[][] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Data");
        for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                ws.Cell(r + 2, c + 1).Value = rows[r][c];
        using var ms = new MemoryStream();
        wb.SaveAs(ms);

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(ms.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(file, "file", $"{kind}.xlsx");
        return await client.PostAsync($"/api/masters/import/{kind}", content);
    }

    private async Task<HttpClient> LoginAsync(string username)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username, password = TestSecrets.SeedPassword, rememberMe = false });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var token = json.GetProperty("data").GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
