using System.Net;
using EasyFind.Api.Data;
using EasyFind.Api.Models.Enum;
using EasyFind.Api.Models.Listings;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;

namespace EasyFind.IntegrationTests;
public class ListingsEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory factory;
    public ListingsEndpointTests(CustomWebApplicationFactory factory)
    {
        this.factory = factory;
    }
    
    
    [Fact]
    public async Task GetListing_WhenNotFound_Returns404()
    {
        // Arrange — a client that talks to the in-memory app
        var client = factory.CreateClient();

        // Act — request a listing that doesn't exist
        var response = await client.GetAsync($"/api/v1/listings/{Guid.NewGuid()}");

        // Assert — should be 404 (or 401 if auth blocks it first — see note below)
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
    [Fact]
    public async Task GetListing_WhenExists_ReturnsListing()
    {
        // Arrange — seed a listing into the in-memory database
        var listingId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Listings.Add(new Listing
            {
                Id = listingId,
                Type = ListingType.Job,
                Title = "Test Engineer",
                Organization = "TestCorp",
                CountryCode = "DE",
                Description = "A test listing",
                ApplyUrl = "https://example.com",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/v1/listings/{listingId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Test Engineer");
    }
    
}
