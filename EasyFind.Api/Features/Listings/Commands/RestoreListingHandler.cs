using EasyFind.Api.Data;
using EasyFind.Api.Extensions;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Listings.Commands;

// Admin un-deletes a soft-deleted listing by clearing DeletedAt.
//
// Note this only does something once DeleteListingHandler actually soft-deletes;
// today it hard-deletes, so there is never a row left to restore.
public class RestoreListingHandler(
    ApplicationDbContext db,
    ListingAuthorizationService listings,
    IRedisCacheService cache)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken ct = default)
    {
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
