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
using Hangfire.Dashboard;
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
using Microsoft.Extensions.DependencyInjection.Extensions;

//AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
//postgreSQL and redis connection string variables
// 1. Get the JSON string from Environment Variables
var npgSqlConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");

// Tagged "ready", not left untagged, so it lands on /health/ready only. The
// load balancer probes /health, which checks nothing but that the process is
// answering — see the mapping further down for why that separation matters.
var healthChecks = builder.Services.AddHealthChecks();
if (!string.IsNullOrWhiteSpace(npgSqlConnectionString))
    healthChecks.AddNpgSql(npgSqlConnectionString, name: "postgres", tags: ["ready"]);
//.AddRedis(redisConnectionString, name: "redis", tags: ["ready"]);


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

// Development only: OTPs (and any other SMS) are written to the console instead
// of sent, so sign-in can be tested without AfroMessage credit. Every other
// environment keeps the real sender; see ConsoleSmsService.
if (builder.Environment.IsDevelopment())
{
    builder.Services.RemoveAll<ISmsService>();
    builder.Services.AddScoped<ISmsService, ConsoleSmsService>();
}

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

                // The chat hub handshake cannot send an Authorization header, so
                // it takes the token on the query string. Nothing else does:
                // a token in a URL is recorded in ALB access logs, browser
                // history and Referer headers. The Hangfire dashboard has its
                // own cookie session for this reason — see HangfireDashboardAuth.
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/chat"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

// SuperAdmin includes Admin. See AppPolicies for why this isn't done with roles.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AppPolicies.AdminAccess, p => p.RequireRole(AppRoles.Admin, AppRoles.SuperAdmin))
    .AddPolicy(AppPolicies.SuperAdminAccess, p => p.RequireRole(AppRoles.SuperAdmin));

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
// No default and no environment exemption: an unset queue URL fails the boot
// rather than surfacing later as payment texts that silently never send.
builder.Services
    .AddOptions<NotificationOptions>()
    .Bind(builder.Configuration.GetSection(NotificationOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.QueueUrl),
        "Notifications:QueueUrl is required (env var Notifications__QueueUrl).")
    .ValidateOnStart();
// Model and ApiKey are [Required]: a missing key fails the boot rather than
// every chat request. Production needs Gemini__ApiKey in the task definition.
builder.Services
    .AddOptions<GeminiOptions>()
    .Bind(builder.Configuration.GetSection(GeminiOptions.SectionName))
    .ValidateDataAnnotations()
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

// Registering the recurring job is separate from exposing the dashboard: the
// job must run wherever Hangfire is enabled, the dashboard is opt-in. The
// dashboard itself is mapped further down, after authentication — see there.
//
// Resolved from DI (IRecurringJobManager) rather than called through the static
// RecurringJob.AddOrUpdate. The static API reads JobStorage.Current, which is
// only populated as a SIDE EFFECT of something else resolving Hangfire's
// services — UseHangfireDashboard used to do it, purely because it happened to
// sit on the line above. Moving the dashboard below UseAuthentication left
// nothing to initialise storage and the app crashed on boot with "Current
// JobStorage instance has not been initialized yet". Going through DI does not
// care what order the pipeline is assembled in.
if (hangfireEnabled)
{
    using var scope = app.Services.CreateScope();

    scope.ServiceProvider.GetRequiredService<IRecurringJobManager>()
        .AddOrUpdate<SubscriptionExpiryJob>(
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

// ── Health: liveness and readiness are NOT the same question ──────────────
//
// LIVENESS (/health) — "is this process answering?" and nothing more. This is
// what the ALB target group probes. It must not touch Postgres: a dependency
// check here fails on every task at once during an RDS blip, the ALB drains the
// whole service, and a recoverable database hiccup becomes a full outage that
// outlives it. A task that cannot reach the database is still the task you want
// kept alive to reconnect.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// READINESS (/health/ready) — "are this task's dependencies actually reachable?"
// For dashboards, alerting and post-deploy smoke checks. Point monitoring here,
// never the load balancer.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// Migrations are applied by hand — `dotnet ef database update` — deliberately.
// There is no startup MigrateAsync: a deploy must not be able to alter the
// schema on its own. That means a migration is a SEPARATE, MANUAL step that has
// to land BEFORE the image that needs it, or the new tasks start against an old
// schema and fail on first use.

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

// ── Hangfire dashboard ────────────────────────────────────────────────────
//
// Mapped HERE, below UseAuthentication, and not up with the rest of the
// pipeline configuration: the dashboard's authorization filter reads
// HttpContext.User, which is still anonymous until authentication has run.
// Mapped any earlier it would reject every caller, admins included.
//
// It exposes job arguments and can enqueue, requeue and delete jobs, so an
// unauthenticated /hangfire behind the ALB is remote control of the background
// queue. An admin signs in at /hangfire/login by pasting an access token, which
// is exchanged for a session cookie — see HangfireDashboardAuth for why it
// cannot simply read the bearer token.
//
// Still switchable (Hangfire:DashboardEnabled) so an instance can be deployed
// without it at all.
if (hangfireEnabled && app.Configuration.GetValue("Hangfire:DashboardEnabled", true))
{
    // Order is the whole design here:
    //   1. the login branch, before Hangfire can 404 an unknown /hangfire path
    //   2. the cookie -> HttpContext.User step, before the filter reads User
    //   3. the dashboard itself
    app.MapHangfireLogin();
    app.UseHangfireDashboardAuth();

    app.UseHangfireDashboard(HangfireDashboardAuth.DashboardPath, new DashboardOptions
    {
        Authorization = [new HangfireDashboardAuthorizationFilter()],

        // Hangfire treats every remote request as read-only unless told
        // otherwise; the filter above has already established this is an admin.
        IsReadOnlyFunc = _ => false
    });
}

app.UseRequestLocalization(localizationOptions);
app.MapControllers();
app.Run();
public partial class Program { }