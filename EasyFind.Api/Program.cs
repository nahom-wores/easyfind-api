using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Amazon.S3;
using Asp.Versioning;
using EasyFind.Api;
using EasyFind.Api.Data;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Options;
using Microsoft.AspNetCore.HttpOverrides;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Services.Jobs;
using Hangfire;
using Hangfire.PostgreSql;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using StackExchange.Redis;
using Amazon.S3;
using EasyFind.Api.Services;
using EasyFind.Api.Services.IServices;

//AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
//postgreSQL and redis connection string variables
// 1. Get the JSON string from Environment Variables
var npgSqlConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");

builder.Services.AddHealthChecks()
    .AddNpgSql(npgSqlConnectionString, name: "postgres");
//.AddRedis(redisConnectionString, name: "redis");


var redisAvailable = !string.IsNullOrWhiteSpace(redisConnectionString)
                     && !redisConnectionString.Contains("localhost");
if (redisAvailable)
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(
        ConnectionMultiplexer.Connect(redisConnectionString!));
    builder.Services.AddScoped<IRedisCacheService, RedisCacheService>();
}
else
{
    builder.Services.AddScoped<IRedisCacheService, NoOpCacheService>();
}
#region Versioning

builder.Services.AddApiVersioning(options =>
{
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
}).AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

#endregion
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 100 * 1024 * 1024; // 100MB
});
builder.Services.AddLocalization();
var supportedCultures = new[] { "en-US", "am-ET" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture(supportedCultures[0])
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

builder.WebHost.UseKestrel(option =>
{
    option.AddServerHeader = false;
    option.Limits.MaxRequestBodySize = 100 * 1024 * 1024; // 100MB
});
builder.Services.AddMemoryCache();
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonS3>();

#region service registrations

builder.Services.AddLifetimeServices();

#endregion

#region Advanced Redis Caching
//
// builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
// {
//     var configuration = ConfigurationOptions.Parse(redisConnectionString!, true);
//     configuration.AbortOnConnectFail = false; // Don't crash if Redis is down
//     configuration.ConnectTimeout = 5000; // 5 second timeout
//     configuration.SyncTimeout = 5000;
//     configuration.ConnectRetry = 3; // Retry 3 times
//     configuration.KeepAlive = 60; // Keep connection alive
//     configuration.DefaultDatabase = 0; // Use database 0
//     return ConnectionMultiplexer.Connect(configuration);
// });

#endregion

#region PostgreSQL Database

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(npgSqlConnectionString));

#endregion

builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

#region Jwt Token

var key = builder.Configuration.GetValue<string>("JwtConfig:Secret");

builder.Services.AddAuthentication(x =>
    {
        x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(x =>
    {
        x.RequireHttpsMetadata = false;
        x.SaveToken = true;
        x.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(key)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero, // don't accept expired token even 1 sec ago
        };
        x.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/chat"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

#endregion

#region RateLimiter

// Per-IP backstop only. The real limits are per phone number and live in
// Postgres (IOtpThrottle) — these counters are in-process, so with N ECS tasks
// the effective cap is N times what is configured. Set them loose enough that
// legitimate users sharing a NAT never trip them; the per-phone limits do the
// precise work.
//
// HISTORY: the previous version partitioned on an "X-Phone" header that no
// client ever sends. A missing header yields "" (not null), so every caller in
// the world shared a single 3-per-hour bucket. Partition on something the
// request actually carries.
var otpThrottleOptions = builder.Configuration
    .GetSection(OtpThrottleOptions.SectionName).Get<OtpThrottleOptions>() ?? new OtpThrottleOptions();

builder.Services.AddRateLimiter(rateLimitOptions =>
{
    rateLimitOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Answer in the same envelope as everything else, and say when to retry —
    // otherwise the client cannot tell throttling from an outage.
    rateLimitOptions.OnRejected = async (context, ct) =>
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
            ? wait
            : TimeSpan.FromMinutes(otpThrottleOptions.IpWindowMinutes);

        context.HttpContext.Response.Headers.RetryAfter =
            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        var body = new ApiResponse { IsSuccess = false };
        body.Errors.Add("Too many attempts. Please try again later.");
        await context.HttpContext.Response.WriteAsJsonAsync(body, ct);
    };

    // Sending a code: costs money and rings someone's phone.
    rateLimitOptions.AddPolicy("otp-send", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientIpOf(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = otpThrottleOptions.SendsPerIpPerWindow,
                Window = otpThrottleOptions.IpWindow
            }));

    // Checking a code: the brute-force surface.
    rateLimitOptions.AddPolicy("otp-verify", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientIpOf(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = otpThrottleOptions.VerificationsPerIpPerWindow,
                Window = otpThrottleOptions.IpWindow
            }));
});

// Behind the ALB every request appears to come from the load balancer, so
// without UseForwardedHeaders (configured below) this would be one global
// bucket. Falling back to a single constant key would do the same, so an
// unknown IP is treated as its own partition per connection.
static string ClientIpOf(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString()
    ?? "unknown-" + context.Connection.Id;

#endregion

#region Logging Config

// Information, not Warning: at Warning the log says nothing about what the API
// was doing before a problem, which is exactly the context needed to diagnose
// one. Framework namespaces stay at Warning so request noise does not bury it.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/easyfind_api_log.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();
builder.Host.UseSerilog(); // use Serilog for logging

#endregion

#region CORS (Cross-Origin Resource Sharing) - Essential for Frontends (React/Mobile)

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        // policy.WithOrigins("http://localhost:8080")
        //     .AllowAnyMethod()
        //     .AllowAnyHeader()
        //     .AllowCredentials();

        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

#endregion

builder.Services.AddHttpContextAccessor();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// UseExceptionHandler() refuses to start without a fallback registered, even
// when a handler always handles. GlobalExceptionHandler returns true for every
// exception, so this fallback is unreachable in practice — it exists to satisfy
// the middleware and to leave something sane if the handler itself ever throws.
builder.Services.AddProblemDetails();

builder.Services.AddControllers(options =>
{
    // Enforces the FluentValidation validators registered in AddLifetimeServices.
    options.Filters.Add<ValidationFilter>();
}).AddJsonOptions(options =>
{
    //Ignore circular reference
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

// [ApiController] answers a DataAnnotation failure with ProblemDetails by
// default, which is a third response shape on top of ApiResponse and the
// exception handler's. Same envelope as everything else instead.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var response = new ApiResponse { IsSuccess = false };
        foreach (var error in context.ModelState.Values.SelectMany(v => v.Errors))
            response.Errors.Add(string.IsNullOrWhiteSpace(error.ErrorMessage)
                ? "Invalid request."
                : error.ErrorMessage);

        return new BadRequestObjectResult(response);
    };
});
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options => { options.AddDocumentTransformer<BearerSecuritySchemeTransformer>(); });

#region hangfire background service

// Hangfire needs a live Postgres at startup, so it is switchable: integration
// tests run against SQLite and turn it off, and it can be disabled on an
// instance that should not run background jobs. Defaults to on.
var hangfireEnabled = builder.Configuration.GetValue("Hangfire:Enabled", true);

if (hangfireEnabled)
{
    builder.Services.AddHangfire(config => config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(options =>
            options.UseNpgsqlConnection(builder.Configuration
                .GetConnectionString("DefaultConnection"))));
    builder.Services.AddHangfireServer();
}

#endregion

// subscription options 
builder.Services
    .AddOptions<SubscriptionOptions>()
    .Bind(builder.Configuration.GetSection(SubscriptionOptions.SectionName))
    .Validate(o => o.ProPriceEtb > 0,
        "Subscription prices must be greater than zero.")
    .Validate(o => o.DurationDays > 0,
        "Subscription duration must be positive.")
    .ValidateOnStart();
builder.Services.Configure<DocumentUploadOptions>(
    builder.Configuration.GetSection(DocumentUploadOptions.SectionName));

builder.Services
    .AddOptions<OtpThrottleOptions>()
    .Bind(builder.Configuration.GetSection(OtpThrottleOptions.SectionName))
    .Validate(o => o.SendsPerWindow > 0 && o.SendWindowMinutes > 0,
        "OTP send limits must be positive.")
    .Validate(o => o.MaxFailedVerifications > 0 && o.LockoutMinutes > 0,
        "OTP verification limits must be positive.")
    .ValidateOnStart();

// The ALB terminates the connection, so without this every request looks like it
// came from the load balancer and any per-IP limit becomes a global one.
//
// ForwardLimit = 1 takes the entry the ALB appended (the real client) and
// ignores any X-Forwarded-For the caller supplied, so it cannot be spoofed.
// KnownNetworks/Proxies are cleared because the ALB's private IP is not stable.
// Off by default so local development is unaffected.
if (builder.Configuration.GetValue("BehindReverseProxy", false))
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });
}
//-------------------------request pipeline-----------------------------//
var app = builder.Build();

// Configure the HTTP request pipeline.
if (hangfireEnabled)
{
    app.UseHangfireDashboard("/hangfire");
    RecurringJob.AddOrUpdate<SubscriptionExpiryJob>(
        "subscription-expiry", // unique job id
        job => job.RunAsync(), // what to call
        Cron.Daily(2)); // when: every day at 02:00 UTC
}

if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "EasyFind API";
        options.Theme = ScalarTheme.BluePlanet;
        options.AddDocuments(["v1", "v2"]);

        // Simply tell Scalar to use the "BearerAuth" scheme defined in your OpenAPI doc
        options.AddPreferredSecuritySchemes("BearerAuth");
    });
}

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
// using (var scope = app.Services.CreateScope())
// {
//     var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
//     await db.Database.MigrateAsync();
// }

// ── Seed Identity roles ──────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    foreach (var role in new[] { AppRoles.Admin, AppRoles.User, AppRoles.SuperAdmin })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }
}

// First in the pipeline, so it catches anything thrown further down.
app.UseExceptionHandler();

// Must run before anything that reads the client IP — the rate limiter above all.
if (app.Configuration.GetValue("BehindReverseProxy", false))
    app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseCors("AllowAll");
app.UseResponseCaching();
app.UseAuthentication();
app.UseAuthorization();
app.UseRequestLocalization(localizationOptions);
app.MapControllers();
app.Run();
public partial class Program { }