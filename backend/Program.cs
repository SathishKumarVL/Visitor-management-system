using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Tiaano.Vms.Api.Configuration;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Maintenance;
using Tiaano.Vms.Api.Middleware;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Security;
using Tiaano.Vms.Api.Services;
using Tiaano.Vms.Api.Services.Face;

var builder = WebApplication.CreateBuilder(args);

AppTime.Configure(builder.Configuration["App:TimeZone"]);

// Fail closed before wiring auth/crypto.
SecretConfiguration.ValidateSmtpSecurity(builder.Configuration, builder.Environment);
var jwtKey = SecretConfiguration.GetRequiredJwtSigningKey(builder.Configuration, builder.Environment);
_ = SecretConfiguration.GetRequiredEncryptionKey(builder.Configuration, builder.Environment);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql =>
        {
            sql.CommandTimeout(30);
            sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
            sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        })
        // Snapshot already includes location-scoped mail/pass; SQL applied via AddPassMailBrandLocation.sql.
        .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddErrorDescriber<FriendlyIdentityErrorDescriber>()
    .AddDefaultTokenProviders();

const string authSelectorScheme = "JwtOrApiKey";
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = authSelectorScheme;
    options.DefaultAuthenticateScheme = authSelectorScheme;
    options.DefaultChallengeScheme = authSelectorScheme;
})
.AddPolicyScheme(authSelectorScheme, authSelectorScheme, options =>
{
    // API keys are only honoured under /integration, and JWTs only everywhere else.
    options.ForwardDefaultSelector = ctx =>
        ctx.Request.Path.StartsWithSegments(ApiKeyDefaults.PathPrefix)
            ? ApiKeyDefaults.Scheme
            : JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = SecretConfiguration.CreateJwtValidationParameters(builder.Configuration, jwtKey);
    options.MapInboundClaims = false;
})
.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
    ApiKeyDefaults.Scheme, _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(ApiKeyDefaults.ReportsPolicy, p => p
        .AddAuthenticationSchemes(ApiKeyDefaults.Scheme)
        .RequireClaim(ApiKeyDefaults.ScopeClaim, IntegrationScopes.Reports));
    options.AddPolicy(ApiKeyDefaults.EmployeesPolicy, p => p
        .AddAuthenticationSchemes(ApiKeyDefaults.Scheme)
        .RequireClaim(ApiKeyDefaults.ScopeClaim, IntegrationScopes.Employees));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new Tiaano.Vms.Api.DTOs.ApiResponse<object>(false, null, "Too many attempts. Please wait a minute and try again."), ct);
    };
    if (RateLimitPolicy.IsLoginRateLimitRelaxed(builder.Environment.EnvironmentName))
    {
        options.AddPolicy(RateLimitPolicy.LoginPolicyName, _ => RateLimitPartition.GetNoLimiter("dev"));
        options.AddPolicy(RateLimitPolicy.RefreshPolicyName, _ => RateLimitPartition.GetNoLimiter("dev"));
        options.AddPolicy(RateLimitPolicy.PublicInvitePolicyName, _ => RateLimitPartition.GetNoLimiter("dev"));
    }
    else
    {
        options.AddPolicy(RateLimitPolicy.LoginPolicyName, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = RateLimitPolicy.LoginPermitLimit,
                    Window = RateLimitPolicy.LoginWindow,
                    QueueLimit = RateLimitPolicy.LoginQueueLimit
                }));
        options.AddPolicy(RateLimitPolicy.RefreshPolicyName, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = RateLimitPolicy.RefreshPermitLimit,
                    Window = RateLimitPolicy.LoginWindow,
                    QueueLimit = 0
                }));
        options.AddPolicy(RateLimitPolicy.PublicInvitePolicyName, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = RateLimitPolicy.PublicInvitePermitLimit,
                    Window = RateLimitPolicy.LoginWindow,
                    QueueLimit = 0
                }));
    }
});

// Persisted so password-reset links stay valid across IIS app-pool recycles.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")))
    .SetApplicationName("Tiaano.Vms");
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(1));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<EmailNotificationProvider>();
builder.Services.AddScoped<IEmailNotificationProvider>(sp => sp.GetRequiredService<EmailNotificationProvider>());
builder.Services.AddScoped<INotificationDeliveryProvider>(sp => sp.GetRequiredService<EmailNotificationProvider>());
builder.Services.AddScoped<IWebPushService, WebPushService>();
builder.Services.AddScoped<INotificationDeliveryProvider, PushNotificationProvider>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IEmployeeNotificationService, EmployeeNotificationService>();
builder.Services.AddScoped<IVisitMailService, VisitMailService>();
builder.Services.AddScoped<IPassNumberService, PassNumberService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserFaceLoginService, UserFaceLoginService>();
builder.Services.AddScoped<IEmployeeMasterService, EmployeeMasterService>();
builder.Services.AddScoped<IMeetingHallAllocator, MeetingHallAllocator>();
builder.Services.AddScoped<IVisitorService, VisitorService>();
builder.Services.AddScoped<IVisitorInvitationService, VisitorInvitationService>();
builder.Services.AddScoped<IFeedbackService, FeedbackService>();
builder.Services.AddScoped<IMasterDataService, MasterDataService>();
builder.Services.AddScoped<IMasterImportService, MasterImportService>();
builder.Services.AddScoped<IMasterClearService, MasterClearService>();
builder.Services.AddScoped<IIntegrationKeyService, IntegrationKeyService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IUserAdminService, UserAdminService>();
builder.Services.AddScoped<IMediaStorageService, MediaStorageService>();
builder.Services.AddScoped<IEntitlementService, EntitlementService>();
builder.Services.AddScoped<IEmergencyService, EmergencyService>();
builder.Services.AddScoped<ILookupService, LookupService>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient(LookupService.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(10),
        ConnectCallback = OutboundUrlGuard.ConnectCallback(
            builder.Configuration.GetValue<bool>("Lookups:AllowPrivateNetworks")),
    });
builder.Services.AddHostedService<AppointmentReminderWorker>();
builder.Services.AddHostedService<MomReminderWorker>();
builder.Services.AddHostedService<StaleVisitAutoCheckOutWorker>();
builder.Services.AddHostedService<EmailRetryWorker>();

// Singleton: the ONNX sessions hold ~180 MB of weights and are thread-safe for concurrent inference.
builder.Services.Configure<FaceRecognitionOptions>(
    builder.Configuration.GetSection(FaceRecognitionOptions.SectionName));
builder.Services.AddSingleton<IFaceEmbeddingService, InsightFaceService>();

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // Same JSON shape as every other error so the UI can show the real reason.
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage)
                .Distinct()
                .ToList();
            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(
                new Tiaano.Vms.Api.DTOs.ApiResponse<object>(false, null, string.Join(" ", errors), errors));
        };
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "TIAANO Visitor Management API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
    c.AddSecurityDefinition(ApiKeyDefaults.Scheme, new OpenApiSecurityScheme
    {
        Description = $"Integration API key for /integration endpoints, sent in the {ApiKeyDefaults.HeaderName} header.",
        Name = ApiKeyDefaults.HeaderName,
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = ApiKeyDefaults.Scheme }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod();
        }
        else
        {
            var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
        }
    });
});

var app = builder.Build();

// Hosted under https://visitors.tiaanosoft.com/api (IIS application or reverse proxy).
// Controllers are routed without a second "api" segment; PathBase supplies the /api prefix.
var pathBase = builder.Configuration["ASPNETCORE_PATHBASE"]
    ?? Environment.GetEnvironmentVariable("ASPNETCORE_PATHBASE")
    ?? "/api";
if (!string.IsNullOrWhiteSpace(pathBase))
    app.UsePathBase(pathBase.TrimEnd('/'));

// Operator commands run against the built container and exit without serving traffic.
if (ResetPasswordCommand.ShouldRun(args))
    return await ResetPasswordCommand.RunAsync(app.Services, args);
if (RetryEmailsCommand.ShouldRun(args))
    return await RetryEmailsCommand.RunAsync(app.Services, args);
if (SendTestEmailCommand.ShouldRun(args))
    return await SendTestEmailCommand.RunAsync(app.Services, args);

var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
var brandingRoot = Path.Combine(webRoot, "branding");
Directory.CreateDirectory(brandingRoot);
Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data", "media", "visitors"));

app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}

// Branding only — visitor media is NOT publicly served from wwwroot.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(brandingRoot),
    RequestPath = "/branding"
});

app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<MustChangePasswordMiddleware>();
app.UseAuthorization();
app.MapControllers();

await DbSeeder.SeedAsync(app.Services);

app.Run();
return 0;

public partial class Program { }
