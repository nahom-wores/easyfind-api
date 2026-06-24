using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using EasyFind.Api;
using EasyFind.Api.Data;
using EasyFind.Api.Models.Auth;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using StackExchange.Redis;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
//postgreSQL and redis connection string variables
// 1. Get the JSON string from Environment Variables
var npgSqlConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
builder.Services.AddHealthChecks()
    .AddRedis(redisConnectionString, name: "redis")
    .AddNpgSql(npgSqlConnectionString, name: "postgres");

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
// remove server header so that no one knows what tech stack you are using its for security
builder.WebHost.UseKestrel(option =>
{
    option.AddServerHeader = false;
    option.Limits.MaxRequestBodySize = 100 * 1024 * 1024; // 100MB
});
builder.Services.AddAutoMapper(cfg => { }, typeof(MappingConfig));
builder.Services.AddMemoryCache();

#region service registrations

builder.Services.AddLifetimeServices();

#endregion

#region Advanced Redis Caching

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = ConfigurationOptions.Parse(redisConnectionString!, true);
    configuration.AbortOnConnectFail = false; // Don't crash if Redis is down
    configuration.ConnectTimeout = 5000; // 5 second timeout
    configuration.SyncTimeout = 5000;
    configuration.ConnectRetry = 3; // Retry 3 times
    configuration.KeepAlive = 60; // Keep connection alive
    configuration.DefaultDatabase = 0; // Use database 0
    return ConnectionMultiplexer.Connect(configuration);
});

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
        x.RequireHttpsMetadata = true;
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
builder.Services.AddRateLimiter(rateLimitOptions =>
{
    rateLimitOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    rateLimitOptions.AddPolicy("otp", context =>
    {
        var phone = context.Request.Headers["X-Phone"].ToString();
        
        return RateLimitPartition.GetFixedWindowLimiter(
            phone ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromHours(1)
            });
    });
    
    // Auth rule
    rateLimitOptions.AddPolicy("auth", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(
            ip, _ => new SlidingWindowRateLimiterOptions()
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                SegmentsPerWindow = 5
            });
    });
    
    
});
#endregion

#region Logging Config
// Log configuration using Serilog
Log.Logger = new LoggerConfiguration().MinimumLevel.Warning()
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
        policy.WithOrigins("http://localhost:8080")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});
#endregion
builder.Services.AddHttpContextAccessor();

builder.Services.AddControllers().AddJsonOptions(options =>
{
    //Ignore circular reference
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.

if (app.Environment.IsDevelopment())
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

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseCors("AllowAll");
app.UseResponseCaching();
app.UseAuthentication();
app.UseAuthorization();
app.UseRequestLocalization(localizationOptions);
app.MapControllers();
app.Run();