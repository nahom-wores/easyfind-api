using EasyFind.Api.Data;
using EasyFind.Api.Extensions;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Listings.Commands;

// Publish (true) or unpublish (false) a listing.
public sealed record SetListingActiveCommand(Guid ListingId, bool IsActive);

// Admin publishes or unpublishes a listing. Inactive listings disappear from the
// feed but stay visible in a user's bookmarks and application tracker.
public class SetListingActiveHandler(
    ApplicationDbContext db,
    ListingAuthorizationService listings,
    IRedisCacheService cache)
{
    public async Task<Result> HandleAsync(SetListingActiveCommand command, CancellationToken ct = default)
    {
        var (id, isActive) = command;
        var listing = await listings.ManageableListings()
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (listing is null)
            return Result.NotFound("Listing not found.");

        listing.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        await cache.InvalidateFeedsAsync();

        return Result.Success();
    }
}
