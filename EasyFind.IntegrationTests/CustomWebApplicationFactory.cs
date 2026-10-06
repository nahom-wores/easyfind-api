using System.Data.Common;
using EasyFind.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using EasyFind.Api.Services.IServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyFind.IntegrationTests;

// Boots the real application against a throwaway SQLite database.
//
// IMPORTANT: this file must NOT declare its own `partial class Program`. Doing so
// creates a second Program type in the test assembly, and WebApplicationFactory
// binds to that empty one instead of the API entry point — which fails with
// "The entry point exited without ever building an IHost" before a single line
// of application code runs.
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private DbConnection _connection = null!;

    // Settings that Program.cs reads from builder.Configuration BEFORE Build()
    // must arrive as environment variables.
    //
    // Under minimal hosting the entry point runs first and reads configuration
    // as it goes, so ConfigureAppConfiguration lands too late for those — which
    // is exactly how Hangfire ended up dialling a real Postgres despite being
    // "disabled" here. Environment variables are in place before the builder
    // exists. ("__" is the separator for ":".)
    static CustomWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("Hangfire__Enabled", "false");

        // Empty Redis connection selects NoOpCacheService, so the feed cache is
        // a no-op and every test sees freshly computed results.
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", "");

        // Must PARSE as an Npgsql string: the health-check registration builds a
        // data source from it eagerly. Nothing ever connects to it — the
        // DbContext is swapped for SQLite below.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection",
            "Host=localhost;Database=easyfind_test;Username=test;Password=test");

        // The per-IP caps are read eagerly to build the rate-limiter policies,
        // so they belong here too. Every test shares one localhost IP, so the
        // appsettings value (10 per 15 min) would be spent by the third test in
        // a class and every later one would see 429s it never asked for.
        //
        // Raised far clear of the per-phone limits, which are what these tests
        // actually assert; those bind lazily and can stay in configuration.
        Environment.SetEnvironmentVariable("OtpThrottle__SendsPerIpPerWindow", "100000");
        Environment.SetEnvironmentVariable("OtpThrottle__VerificationsPerIpPerWindow", "100000");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        // Everything below is read lazily, when a service is constructed, so
        // ordinary test configuration works fine here.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Throttle limits the tests assert against
                ["OtpThrottle:SendsPerWindow"] = "3",
                ["OtpThrottle:SendWindowMinutes"] = "60",
                ["OtpThrottle:MaxFailedVerifications"] = "5",
                ["OtpThrottle:LockoutMinutes"] = "15",
                // NOTE: the per-IP caps are NOT here — see the static constructor.

                ["Subscription:ProPriceEtb"] = "499",
                ["Subscription:DurationDays"] = "30",
                ["Subscription:FreeFeedCap"] = "5",
                ["JwtConfig:Secret"] = "test-secret-key-at-least-32-characters-long-for-testing",

                // Constructor guards on the infrastructure adapters
                ["AWS:S3:BucketName"] = "test-bucket",
                ["AWS:S3:ImageBucketName"] = "test-image-bucket",
                ["Cloudinary:CloudName"] = "test",
                ["Cloudinary:ApiKey"] = "test",
                ["Cloudinary:ApiSecret"] = "test",
                ["AfroMessage:ApiToken"] = "test",
                ["AfroMessage:IdentifierId"] = "test",
                ["Chapa:ApiKey"] = "test",
                ["Chapa:SecretKey"] = "test",
                ["Chapa:WebhookSecret"] = "test",
                ["Chapa:BaseUrl"] = "https://chapa.invalid",
                ["Notifications:QueueUrl"] = "https://sqs.invalid/test-queue",
                ["Gemini:ApiKey"] = "test-key",
                ["Gemini:Model"] = "test-model",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Drop the Npgsql DbContext registrations
            var toRemove = services.Where(d =>
                    d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                    d.ServiceType == typeof(ApplicationDbContext) ||
                    d.ServiceType.Namespace?.StartsWith("Microsoft.EntityFrameworkCore") == true ||
                    d.ImplementationType?.Namespace?.StartsWith("Npgsql") == true)
                .ToList();
            foreach (var d in toRemove) services.Remove(d);

            // One in-memory SQLite connection, held open for the lifetime of the
            // factory — closing it would drop the database.
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(_connection));

            // Sign requests in as a chosen user — see TestAuthHandler.
            //
            // All three defaults must be reset: Program.cs names JwtBearer as
            // the authenticate/challenge scheme explicitly, and those beat a
            // bare DefaultScheme, so [Authorize] would keep returning 401.
            services.AddAuthentication(options =>
                {
                    options.DefaultScheme = TestAuthHandler.SchemeName;
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<TestAuthOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            // No test may reach the real SMS gateway or Chapa. Singletons so a
            // test can arrange them and then inspect what happened.
            services.RemoveAll<ISmsService>();
            services.AddSingleton<FakeSmsService>();
            services.AddSingleton<ISmsService>(sp => sp.GetRequiredService<FakeSmsService>());

            services.RemoveAll<IChapaClient>();
            services.AddSingleton<FakeChapaClient>();
            services.AddSingleton<IChapaClient>(sp => sp.GetRequiredService<FakeChapaClient>());

            // Nor SQS: the real publisher would try to send to AWS.
            services.RemoveAll<INotificationPublisher>();
            services.AddSingleton<FakeNotificationPublisher>();
            services.AddSingleton<INotificationPublisher>(sp => sp.GetRequiredService<FakeNotificationPublisher>());

            // Nor Gemini: a real call costs money and needs a real key.
            services.RemoveAll<IChatModel>();
            services.AddSingleton<FakeChatModel>();
            services.AddSingleton<IChatModel>(sp => sp.GetRequiredService<FakeChatModel>());

            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection?.Dispose();
    }
}
