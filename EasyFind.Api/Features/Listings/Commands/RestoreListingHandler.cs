using EasyFind.Api.Data;
using EasyFind.Api.Extensions;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Listings.Commands;

public sealed record RestoreListingCommand(Guid ListingId);

// Admin brings a withdrawn listing back by clearing DeletedAt.
//
// Deliberately does NOT set IsActive: restoring makes the listing manageable
// again, publishing it is a separate decision made with PATCH .../active.
public class RestoreListingHandler(
    ApplicationDbContext db,
    ListingAuthorizationService listings,
    IRedisCacheService cache)
{
    public async Task<Result> HandleAsync(RestoreListingCommand command, CancellationToken ct = default)
    {
        var id = command.ListingId;
        var listing = await listings.ManageableListings()
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (listing is null)
            return Result.NotFound("Listing not found.");

        listing.DeletedAt = null;
        await db.SaveChangesAsync(ct);
        await cache.InvalidateFeedsAsync();

        return Result.Success();
    }
}
