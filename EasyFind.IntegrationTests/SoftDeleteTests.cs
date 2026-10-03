using System.Net;
using System.Net.Http.Json;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Enum;
using EasyFind.Api.Models.Listings;
using EasyFind.Api.Models.Subscriptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.IntegrationTests;

// Deleting a listing is now a soft delete, which is what makes the restore
// endpoint mean anything — a hard delete left nothing to restore, so
// POST /admin/listings/{id}/restore could never succeed.
//
// It also protects history: UserApplication references listings with
// DeleteBehavior.Restrict, so hard-deleting a listing somebody applied to would
// have failed outright.
public class SoftDeleteTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private async Task<Guid> SeedListingAsync(string title)
    {
        var id = Guid.NewGuid();
        await factory.SeedAsync(db => db.Listings.Add(new Listing
        {
            Id = id,
            Type = ListingType.Job,
            Title = title,
            Organization = "Acme Corp",
            CountryCode = "DE",
            Description = "desc",
            ApplyUrl = "https://apply.example.com",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        }));
        return id;
    }

    private Task<(HttpClient client, ApplicationUser user)> AdminAsync()
        => factory.SignedInUserAsync(role: AppRoles.Admin);

    [Fact]
    public async Task Delete_KeepsTheRow_AndStampsDeletedAt()
    {
        var id = await SeedListingAsync("to-delete-" + Guid.NewGuid());
        var (admin, _) = await AdminAsync();

        (await admin.DeleteAsync($"/api/v1/admin/listings/{id}")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var listing = await factory.WithDbAsync(db => db.Listings
            .AsNoTracking().SingleOrDefaultAsync(l => l.Id == id));

        listing.Should().NotBeNull("a soft delete keeps the row");
        listing!.DeletedAt.Should().NotBeNull();
        listing.IsActive.Should().BeFalse("a withdrawn listing must not stay published");
    }

    [Fact]
    public async Task DeletedListing_DisappearsFromTheFeed()
    {
        var title = "gone-" + Guid.NewGuid();
        var id = await SeedListingAsync(title);
        var (admin, _) = await AdminAsync();

        await admin.DeleteAsync($"/api/v1/admin/listings/{id}");

        var (user, _) = await factory.SignedInUserAsync(SubscriptionTier.Pro);
        var page = await (await user.GetAsync("/api/v1/feed?page=1&pageSize=50"))
            .ReadResultAsync<PagedResponse<FeedItem>>();

        page.Items.Should().NotContain(i => i.Title == title);
    }

    [Fact]
    public async Task DeletedListing_IsGoneFromTheConsumerDetailView()
    {
        var id = await SeedListingAsync("detail-gone-" + Guid.NewGuid());
        var (admin, _) = await AdminAsync();

        await admin.DeleteAsync($"/api/v1/admin/listings/{id}");

        var (user, _) = await factory.SignedInUserAsync(SubscriptionTier.Pro);
        (await user.GetAsync($"/api/v1/listings/{id}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Restore_BringsItBack()
    {
        // The whole point of soft delete. This endpoint previously could not
        // succeed under any circumstances.
        var id = await SeedListingAsync("restore-me-" + Guid.NewGuid());
        var (admin, _) = await AdminAsync();

        await admin.DeleteAsync($"/api/v1/admin/listings/{id}");

        var restored = await admin.PostAsync($"/api/v1/admin/listings/{id}/restore", null);
        restored.StatusCode.Should().Be(HttpStatusCode.OK);

        var listing = await factory.WithDbAsync(db => db.Listings
            .AsNoTracking().SingleAsync(l => l.Id == id));

        listing.DeletedAt.Should().BeNull();
        listing.IsActive.Should().BeFalse(
            "restoring makes it manageable again; publishing is a separate decision");
    }

    [Fact]
    public async Task DeletingTwice_IsHarmless()
    {
        var id = await SeedListingAsync("twice-" + Guid.NewGuid());
        var (admin, _) = await AdminAsync();

        await admin.DeleteAsync($"/api/v1/admin/listings/{id}");
        var first = await factory.WithDbAsync(db => db.Listings
            .AsNoTracking().SingleAsync(l => l.Id == id));

        (await admin.DeleteAsync($"/api/v1/admin/listings/{id}")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var second = await factory.WithDbAsync(db => db.Listings
            .AsNoTracking().SingleAsync(l => l.Id == id));

        second.DeletedAt.Should().Be(first.DeletedAt,
            "re-deleting must not overwrite the original deletion time");
    }

    [Fact]
    public async Task ApplicationTracker_SurvivesTheListingBeingWithdrawn()
    {
        // A hard delete could not even have run here: the FK is Restrict.
        var id = await SeedListingAsync("applied-" + Guid.NewGuid());
        var (user, applicant) = await factory.SignedInUserAsync(SubscriptionTier.Pro);

        var created = await user.PostAsJsonAsync("/api/v1/applications",
            new { listingId = id, status = 1, notes = "applied" });
        created.StatusCode.Should().Be(HttpStatusCode.OK);

        var (admin, _) = await AdminAsync();
        (await admin.DeleteAsync($"/api/v1/admin/listings/{id}")).StatusCode
            .Should().Be(HttpStatusCode.OK, "withdrawing a listing someone applied to must work");

        var stillThere = await factory.WithDbAsync(db => db.UserApplications
            .AsNoTracking().AnyAsync(a => a.UserId == applicant.Id && a.ListingId == id));

        stillThere.Should().BeTrue("the user's own tracker entry is their data");
    }
}
