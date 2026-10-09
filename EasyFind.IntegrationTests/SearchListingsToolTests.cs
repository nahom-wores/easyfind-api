using System.Text.Json;
using EasyFind.Api.Data;
using EasyFind.Api.Features.Listings;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Enum;
using EasyFind.Api.Models.Listings;
using EasyFind.Api.Services.Assistant.Tools;
using EasyFind.Api.Services.IServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EasyFind.IntegrationTests;

// The assistant's search reads listings through ListingAuthorizationService,
// the one place allowed to touch db.Listings. It uses PublishedListings, not
// AuthorizedListings: the assistant is a consumer surface, so staff must see
// what the public sees there rather than every closed and deleted listing.
//
// Each test filters on its own country code so the results are only its own
// rows. (The free-text "query" argument uses Postgres ILike, which SQLite can't
// run, so it isn't exercised here.)
public class SearchListingsToolTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private sealed class StubCurrentUser(params string[] roles) : ICurrentUser
    {
        public string? UserId => "stub-user";
        public bool IsInRole(string role) => roles.Contains(role);
    }

    // Three rows in one country: published, closed, and deleted. The deleted one
    // is still IsActive, so only the DeletedAt check can keep it out.
    private async Task SeedAsync(string country)
    {
        await factory.SeedAsync(db => db.Listings.AddRange(
            Listing(country, "Published", isActive: true),
            Listing(country, "Closed", isActive: false),
            Listing(country, "Deleted", isActive: true, deletedAt: DateTimeOffset.UtcNow)));
    }

    private static Listing Listing(string country, string title, bool isActive, DateTimeOffset? deletedAt = null) => new()
    {
        Type = ListingType.Job,
        Title = title,
        Organization = "Acme",
        CountryCode = country,
        Description = "desc",
        ApplyUrl = "https://apply.example.com",
        IsActive = isActive,
        DeletedAt = deletedAt,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task<List<string>> SearchAsync(string country, params string[] roles)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tool = new SearchListingsTool(new ListingAuthorizationService(db, new StubCurrentUser(roles)));

        var args = JsonSerializer.SerializeToElement(new { countryCode = country });
        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(args, CancellationToken.None));

        return result.TryGetProperty("listings", out var listings)
            ? listings.EnumerateArray().Select(l => l.GetProperty("title").GetString()!).ToList()
            : [];
    }

    [Fact]
    public async Task User_FindsOnlyPublishedListings()
    {
        await SeedAsync("XA");

        (await SearchAsync("XA", AppRoles.User)).Should().Equal("Published");
    }

    // The reason for PublishedListings: AuthorizedListings would hand an admin
    // all three rows here.
    [Fact]
    public async Task Admin_SeesTheSameAsAUser_NotClosedOrDeletedListings()
    {
        await SeedAsync("XB");

        (await SearchAsync("XB", AppRoles.Admin)).Should().Equal("Published");
        (await SearchAsync("XB", AppRoles.SuperAdmin)).Should().Equal("Published");
    }

    [Fact]
    public async Task NoRole_FindsNothing()
    {
        await SeedAsync("XC");

        (await SearchAsync("XC")).Should().BeEmpty();
    }
}
