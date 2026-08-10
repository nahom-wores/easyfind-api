using System.Data.Common;
using EasyFind.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using EasyFind.Api;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace EasyFind.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private DbConnection _connection = null!;
    protected override IHost CreateHost(IHostBuilder builder)
    {
        try
        {
            return base.CreateHost(builder);
        }
        catch (Exception ex)
        {
            throw new Exception("REAL STARTUP ERROR: " + ex.ToString(), ex);
        }
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        // provide any config your startup validates
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Subscription:ProPriceEtb"] = "499",
                ["Subscription:DurationDays"] = "30",
                ["Subscription:FreeFeedCap"] = "5",
                ["ConnectionStrings:Redis"] = "",
                ["ConnectionStrings:DefaultConnection"] = "DataSource=:memory:",
                ["JwtConfig:Secret"] = "test-secret-key-at-least-32-characters-long-for-testing",
            });
        });

        builder.ConfigureServices(services =>
        {
            // remove the real Npgsql DbContext registrations
            var toRemove = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                d.ServiceType == typeof(ApplicationDbContext) ||
                d.ServiceType.Namespace?.StartsWith("Microsoft.EntityFrameworkCore") == true ||
                d.ImplementationType?.Namespace?.StartsWith("Npgsql") == true)
                .ToList();
            foreach (var d in toRemove) services.Remove(d);

            // open ONE SQLite in-memory connection and keep it alive
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlite(_connection));

            // create the schema in the SQLite database
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection?.Dispose();   // close the connection when tests finish
    }
}
public partial class Program { }