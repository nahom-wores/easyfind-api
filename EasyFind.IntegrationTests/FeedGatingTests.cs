using EasyFind.Api.Models.Enum;
using EasyFind.Api.Models.Listings;
using EasyFind.Api.Models.Subscriptions;
using FluentAssertions;

namespace EasyFind.IntegrationTests;

// The paywall. A bug here either gives away paid content or hides content from
// people who paid for it, so these assert the gate from both sides.
//
// FreeFeedCap is 5 in the test configuration.
public class FeedGatingTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private const int FreeFeedCap = 5;

    private static Listing ActiveListing(string title) => new()
    {
        Id = Guid.NewGuid(),
        Type = ListingType.Job,
        Title = title,
        Organization = "Acme Corp",
        CountryCode = "DE",
        Description = "desc",
        ApplyUrl = "https://apply.example.com/" + title,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task SeedListingsAsync(int count, string prefix)
    {
        await factory.SeedAsync(db =>
        {
            for (var i = 0; i < count; i++)
                db.Listings.Add(ActiveListing($"{prefix}-{i}"));
        });
    }

    [Fact]
    public async Task FreeUser_GetsCappedResults_WithOrganizationAndApplyUrlStripped()
    {
        await SeedListingsAsync(8, nameof(FreeUser_GetsCappedResults_WithOrganizationAndApplyUrlStripped));
        var (client, _) = await factory.SignedInUserAsync(SubscriptionTier.Free);

        var response = await client.GetAsync("/api/v1/feed?page=1&pageSize=20");
        response.EnsureSuccessStatusCode();

        var page = await response.ReadResultAsync<PagedResponse<FeedItem>>();

        page.Items.Should().HaveCountLessThanOrEqualTo(FreeFeedCap,
            "a free user must never receive more than the free cap");
        page.TotalCount.Should().BeLessThanOrEqualTo(FreeFeedCap);

        page.Items.Should().OnlyContain(i => i.IsLocked,
            "every item a free user sees is locked");
        page.Items.Should().OnlyContain(i => i.Organization == null,
            "the employer name is paid-only");
        page.Items.Should().OnlyContain(i => i.ApplyUrl == null,
            "the apply link is paid-only");
    }

    [Fact]
    public async Task PaidUser_GetsFullResults_WithOrganizationVisible()
    {
        await SeedListingsAsync(8, nameof(PaidUser_GetsFullResults_WithOrganizationVisible));
        var (client, _) = await factory.SignedInUserAsync(SubscriptionTier.Pro);

        var response = await client.GetAsync("/api/v1/feed?page=1&pageSize=20");
        response.EnsureSuccessStatusCode();

        var page = await response.ReadResultAsync<PagedResponse<FeedItem>>();

        page.Items.Should().HaveCountGreaterThan(FreeFeedCap,
            "a paid user is not subject to the free cap");
        page.Items.Should().OnlyContain(i => !i.IsLocked);
        page.Items.Should().OnlyContain(i => i.Organization == "Acme Corp",
            "paid users see the employer");
    }

    [Fact]
    public async Task InactiveListings_NeverReachTheFeed()
    {
        var hiddenTitle = "hidden-" + Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            var inactive = ActiveListing(hiddenTitle);
            inactive.IsActive = false;
            db.Listings.Add(inactive);
        });

        // Paid, so nothing is capped and the only reason to miss it is IsActive.
        var (client, _) = await factory.SignedInUserAsync(SubscriptionTier.Pro);

        var response = await client.GetAsync("/api/v1/feed?page=1&pageSize=50");
        response.EnsureSuccessStatusCode();

        var page = await response.ReadResultAsync<PagedResponse<FeedItem>>();
        page.Items.Should().NotContain(i => i.Title == hiddenTitle);
    }

    [Fact]
    public async Task SoftDeletedListings_NeverReachTheFeed()
    {
        // Guards ListingAuthorizationService: the global EF query filter is
        // disabled, so DeletedAt is only honoured because that service applies
        // it explicitly. If someone queries db.Listings directly, this fails.
        var deletedTitle = "deleted-" + Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            var deleted = ActiveListing(deletedTitle);
            deleted.DeletedAt = DateTimeOffset.UtcNow;
            db.Listings.Add(deleted);
        });

        var (client, _) = await factory.SignedInUserAsync(SubscriptionTier.Pro);

        var response = await client.GetAsync("/api/v1/feed?page=1&pageSize=50");
        response.EnsureSuccessStatusCode();

        var page = await response.ReadResultAsync<PagedResponse<FeedItem>>();
        page.Items.Should().NotContain(i => i.Title == deletedTitle);
    }

    [Fact]
    public async Task FreeUser_DetailEndpoint_AlsoStripsPaidFields()
    {
        var listing = ActiveListing("detail-" + Guid.NewGuid());
        await factory.SeedAsync(db => db.Listings.Add(listing));

        var (freeClient, _) = await factory.SignedInUserAsync(SubscriptionTier.Free);
        var (paidClient, _) = await factory.SignedInUserAsync(SubscriptionTier.Pro);

        var free = await (await freeClient.GetAsync($"/api/v1/listings/{listing.Id}"))
            .ReadResultAsync<FeedItem>();
        var paid = await (await paidClient.GetAsync($"/api/v1/listings/{listing.Id}"))
            .ReadResultAsync<FeedItem>();

        free.IsLocked.Should().BeTrue();
        free.Organization.Should().BeNull();
        free.ApplyUrl.Should().BeNull();

        paid.IsLocked.Should().BeFalse();
        paid.Organization.Should().Be("Acme Corp");
        paid.ApplyUrl.Should().NotBeNull("paid users get the apply link on the detail view");
    }
}
