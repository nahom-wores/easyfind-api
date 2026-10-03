using System.Net;
using EasyFind.Api.Data;
using EasyFind.Api.Models.Enum;
using EasyFind.Api.Models.Listings;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EasyFind.IntegrationTests;

public class ListingsEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public void Host_Boots_AndSchemaIsCreated()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Listings.Count().Should().Be(0);
    }

    [Fact]
    public async Task GetListing_WithoutAuth_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/v1/listings/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetListing_WhenNotFound_Returns404()
    {
        var (client, _) = await factory.SignedInUserAsync();
        var response = await client.GetAsync($"/api/v1/listings/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetListing_WhenExists_ReturnsListing()
    {
        var listingId = Guid.NewGuid();
        await factory.SeedAsync(db => db.Listings.Add(new Listing
        {
            Id = listingId,
            Type = ListingType.Job,
            Title = "Test Engineer",
            Organization = "TestCorp",
            CountryCode = "DE",
            Description = "A test listing",
            ApplyUrl = "https://example.com",
            IsActive = true
        }));

        var (client, _) = await factory.SignedInUserAsync();

        var response = await client.GetAsync($"/api/v1/listings/{listingId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Test Engineer");
    }
}
