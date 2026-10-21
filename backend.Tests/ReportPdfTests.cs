using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using UglyToad.PdfPig;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>The PDF export is a landscape table with a header row on every page and page numbers.</summary>
public class ReportPdfTests : IClassFixture<TestApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly TestApiFactory _factory;

    public ReportPdfTests(TestApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("daterange", new[] { "Visit", "Company", "Person", "Check-in", "Status", "Feedback" })]
    [InlineData("defaulters", new[] { "Visit", "Phone", "Duration", "Check-out", "issue" })]
    public async Task Pdf_Is_A_Landscape_Table_With_Page_Numbers(string reportType, string[] headers)
    {
        var client = await LoginAsync("superadmin");
        var response = await client.GetAsync(
            $"/api/reports/visitors?reportType={reportType}&format=pdf" +
            $"&dateFrom={DateTime.Today.AddDays(-30):yyyy-MM-dd}&dateTo={DateTime.Today:yyyy-MM-dd}");
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var previewDir = Environment.GetEnvironmentVariable("VMS_PDF_PREVIEW_DIR");
        if (!string.IsNullOrWhiteSpace(previewDir))
            await File.WriteAllBytesAsync(Path.Combine(previewDir, $"report-{reportType}.pdf"), bytes);

        using var pdf = PdfDocument.Open(bytes);
        var first = pdf.GetPage(1);
        Assert.True(first.Width > first.Height, "The report page should be landscape.");

        var text = ReportText.FromPdf(bytes);
        foreach (var header in headers)
            Assert.Contains(header, text);
        Assert.Contains($"Page 1 of {pdf.NumberOfPages}", text);
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
