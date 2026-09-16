using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Tiaano.Vms.Api.Services.Face;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Tiaano.Vms.Api.Configuration;
using Tiaano.Vms.Api.Models;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

internal static class TestSecrets
{
    // Test-only material — never use in production.
    public const string JwtKey = "IntegrationTestJwtSigningKey_MustBe32BytesMin!";
    public const string EncryptionKey = "IntegrationTestDataProtectionKey_Local!";
    public const string SeedPassword = "IntegrationTest_SeedPass1!";
}

public class TestApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Jwt:Key", TestSecrets.JwtKey);
        builder.UseSetting("Jwt:Issuer", "Tiaano.Vms");
        builder.UseSetting("Jwt:Audience", "Tiaano.Vms.Clients");
        builder.UseSetting("Security:DataProtectionKey", TestSecrets.EncryptionKey);
        builder.UseSetting("Seed:DefaultPassword", TestSecrets.SeedPassword);
        builder.UseSetting("Smtp:Enabled", "false");
        builder.UseSetting("Smtp:IgnoreSslErrors", "false");

        // Recognition quality is proven against the real weights in ArcFacePipelineTests; the HTTP
        // tests only need embedding to be deterministic and not cost 180 MB of model loading.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFaceEmbeddingService>();
            services.AddSingleton<IFaceEmbeddingService, StubFaceEmbeddingService>();
        });
    }

    public async Task EnsureReceptionPasswordAsync()
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByNameAsync("reception");
        if (user is null) return;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        await users.ResetPasswordAsync(user, token, TestSecrets.SeedPassword);
        user.MustChangePassword = false;
        await users.UpdateAsync(user);
    }
}

public class WorkflowTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public WorkflowTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_Rejects_Invalid_Credentials()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "nope", password = "bad", rememberMe = false });
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_Reception_Succeeds()
    {
        await _factory.EnsureReceptionPasswordAsync();
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "reception",
            password = TestSecrets.SeedPassword,
            rememberMe = true
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("data").GetProperty("token").GetString()));
    }

    [Fact]
    public async Task Authorized_Endpoints_Require_Token()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/visitors/inside");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reception_Can_Load_Masters()
    {
        await _factory.EnsureReceptionPasswordAsync();
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "reception",
            password = TestSecrets.SeedPassword,
            rememberMe = true
        });
        var loginJson = await login.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var token = loginJson.GetProperty("data").GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var depts = await client.GetAsync("/api/masters/departments");
        depts.EnsureSuccessStatusCode();
        var purposes = await client.GetAsync("/api/masters/purposes");
        purposes.EnsureSuccessStatusCode();
        var locations = await client.GetAsync("/api/masters/locations");
        locations.EnsureSuccessStatusCode();
    }
}

public class SecretConfigurationTests
{
    [Fact]
    public void Missing_Jwt_Key_Fails_Closed()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Production };
        Assert.Throws<InvalidOperationException>(() => SecretConfiguration.GetRequiredJwtSigningKey(config, env));
    }

    [Fact]
    public void Missing_Encryption_Key_Fails_Closed()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Production };
        Assert.Throws<InvalidOperationException>(() => SecretConfiguration.GetRequiredEncryptionKey(config, env));
    }

    [Fact]
    public void Production_Rejects_IgnoreSslErrors()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Smtp:IgnoreSslErrors"] = "true"
        }).Build();
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Production };
        Assert.Throws<InvalidOperationException>(() => SecretConfiguration.ValidateSmtpSecurity(config, env));
    }

    [Fact]
    public void Short_Jwt_Key_Rejected()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "too-short"
        }).Build();
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Development };
        Assert.Throws<InvalidOperationException>(() => SecretConfiguration.GetRequiredJwtSigningKey(config, env));
    }

    [Fact]
    public void Seed_Password_CreateOnly_Ignores_Placeholder()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seed:DefaultPassword"] = "REPLACE_AND_FORCE_CHANGE"
        }).Build();
        Assert.Null(SecretConfiguration.GetSeedPasswordForCreateOnly(config));
    }
}

public class TrackedConfigSecurityTests
{
    private static string RepoFile(params string[] parts) =>
        Path.GetFullPath(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(parts).ToArray()));

    [Fact]
    public void Production_Appsettings_Has_No_Plaintext_Secret_Keys()
    {
        var path = RepoFile("backend", "appsettings.Production.json");
        Assert.True(File.Exists(path), $"Missing {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        if (root.TryGetProperty("Jwt", out var jwt))
            Assert.False(jwt.TryGetProperty("Key", out _), "Jwt:Key must not be in Production appsettings.");
        Assert.False(root.TryGetProperty("Security", out _), "Security section must not be tracked in Production appsettings.");
        Assert.False(root.TryGetProperty("Seed", out _), "Seed section must not be tracked in Production appsettings.");
        if (root.TryGetProperty("Smtp", out var smtp))
            Assert.False(smtp.TryGetProperty("Password", out _), "Smtp:Password must not be tracked.");
    }

    [Fact]
    public void Base_Appsettings_Has_No_Secret_Values()
    {
        var path = RepoFile("backend", "appsettings.json");
        Assert.True(File.Exists(path));
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        if (root.TryGetProperty("Jwt", out var jwt))
            Assert.False(jwt.TryGetProperty("Key", out _));
        Assert.False(root.TryGetProperty("Security", out _));
        Assert.False(root.TryGetProperty("Seed", out _));
        if (root.TryGetProperty("Smtp", out var smtp))
            Assert.False(smtp.TryGetProperty("Password", out _));
    }

    [Fact]
    public void Source_Has_No_Hardcoded_Encryption_Fallback()
    {
        var root = RepoFile("backend");
        foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("TiaanoLocalDevKey-ChangeInProduction!", text);
            Assert.DoesNotContain("?? \"admin123\"", text);
        }
    }

    [Fact]
    public void Login_Page_Has_No_Credential_Hints()
    {
        var path = RepoFile("frontend", "src", "pages", "LoginPage.tsx");
        Assert.True(File.Exists(path));
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("admin123", text);
        Assert.DoesNotContain("ChangeMe@", text);
        Assert.DoesNotContain("Seed users", text);
    }

    [Fact]
    public void Seeder_Does_Not_Reset_Existing_Passwords()
    {
        var path = RepoFile("backend", "Data", "DbSeeder.cs");
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("ResetPasswordAsync", text);
        Assert.Contains("never reset passwords", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Hardcoded_Encryption_Fallback_Absent()
    {
        var path = RepoFile("backend", "Services", "VisitorService.cs");
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("TiaanoLocalDevKey-ChangeInProduction!", text);
        Assert.DoesNotContain("?? \"admin123\"", text);
    }

    [Fact]
    public void Visitor_Uploads_Not_Publicly_Mapped_In_Program()
    {
        var path = RepoFile("backend", "Program.cs");
        var text = File.ReadAllText(path);
        Assert.Contains("RequestPath = \"/branding\"", text);
        Assert.DoesNotContain("UseStaticFiles();", text.Replace("UseStaticFiles(new StaticFileOptions", "STATIC_BRANDING"));
        Assert.Contains("App_Data", text);
    }
}

public class ApplicationSecurityTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ApplicationSecurityTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Settings_Full_Requires_Auth()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/settings");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Settings_Branding_Is_Public()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/settings/branding");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.True(json.GetProperty("data").TryGetProperty("companyName", out _));
        Assert.False(json.GetProperty("data").TryGetProperty("approvalRequired", out _));
    }

    [Fact]
    public async Task Media_Requires_Auth()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/media/does-not-exist.jpg");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_Returns_Refresh_Token()
    {
        await _factory.EnsureReceptionPasswordAsync();
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "reception",
            password = TestSecrets.SeedPassword,
            rememberMe = false
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("data").GetProperty("refreshToken").GetString()));
    }

    [Fact]
    public async Task Public_Uploads_Path_Not_Served()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/uploads/anything.jpg");
        Assert.True(
            response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Unauthorized,
            $"Unexpected status {response.StatusCode}");
    }
}

file class FakeHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Production;
    public string ApplicationName { get; set; } = "Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
        = new Microsoft.Extensions.FileProviders.NullFileProvider();
}
