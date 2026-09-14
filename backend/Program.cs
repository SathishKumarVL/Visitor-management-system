using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Tiaano.Vms.Api.Configuration;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Middleware;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Services;

var builder = WebApplication.CreateBuilder(args);

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
        }));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 6;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.TokenValidationParameters = SecretConfiguration.CreateJwtValidationParameters(builder.Configuration, jwtKey);
});

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IVisitorService, VisitorService>();
builder.Services.AddScoped<IMasterDataService, MasterDataService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IUserAdminService, UserAdminService>();

builder.Services.AddControllers();
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

Directory.CreateDirectory(Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "uploads"));
Directory.CreateDirectory(Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "branding"));

app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await DbSeeder.SeedAsync(app.Services);

app.Run();

public partial class Program { }
