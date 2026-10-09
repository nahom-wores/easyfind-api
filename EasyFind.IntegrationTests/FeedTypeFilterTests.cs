using EasyFind.Api.Extensions;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Enum;
using EasyFind.Api.Models.Listings;
using EasyFind.Api.Models.Subscriptions;
using FluentAssertions;

namespace EasyFind.IntegrationTests;

// GET /feed?type= used to be accepted and silently ignored: FeedRequestDto had
// the field, GetFeedHandler never read it, and the cache key left it out.
//
// Each test filters on its own country so it only sees its own rows. The
// harness uses NoOpCacheService, so cache-key collisions can't show up over
// HTTP; the key is asserted directly instead.
public class FeedTypeFilterTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private async Task SeedOneOfEachAsync(string country)
    {
        await factory.SeedAsync(db => db.Listings.AddRange(
            Listing(country, ListingType.Job, "job"),
            Listing(country, ListingType.Scholarship, "scholarship")));
    }

    private static Listing Listing(string country, ListingType type, string title) => new()
    {
        Type = type,
        Title = title,
        Organization = "Acme",
        CountryCode = country,
        Description = "desc",
        ApplyUrl = "https://apply.example.com",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task<List<FeedItem>> FeedAsync(string query)
    {
        var (client, _) = await factory.SignedInUserAsync(SubscriptionTier.Pro);
        var response = await client.GetAsync($"/api/v1/feed?{query}");
        response.EnsureSuccessStatusCode();
        return (await response.ReadResultAsync<PagedResponse<FeedItem>>()).Items;
    }

    [Theory]
    [InlineData("Scholarship")]
    [InlineData("Job")]
    public async Task Type_ReturnsOnlyThatType(string type)
    {
        var country = type == "Job" ? "YA" : "YB";
        await SeedOneOfEachAsync(country);

        var items = await FeedAsync($"countryCode={country}&type={type}");

        items.Select(i => i.Type).Should().Equal(type);
    }

    [Fact]
    public async Task NoType_ReturnsBoth()
    {
        await SeedOneOfEachAsync("YC");

        var items = await FeedAsync("countryCode=YC");

        items.Select(i => i.Type).Should().BeEquivalentTo(["Job", "Scholarship"]);
    }

    [Fact]
    public void CacheKey_DiffersByType()
    {
        string Key(ListingType? type) =>
            new FeedRequestDto { Type = type }.ToFeedCacheKey("user-1", SubscriptionTier.Free);

        new[] { Key(null), Key(ListingType.Job), Key(ListingType.Scholarship) }
            .Should().OnlyHaveUniqueItems("a jobs page and a scholarships page must not share a cache entry");
    }
}
