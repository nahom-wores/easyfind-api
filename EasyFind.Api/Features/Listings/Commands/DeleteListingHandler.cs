using EasyFind.Api.Data;
using EasyFind.Api.Extensions;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Listings.Commands;

public sealed record DeleteListingCommand(Guid ListingId);

// Admin removes a listing.
//
// This is a HARD delete: the row is gone and RestoreListingHandler cannot bring
// it back. The entity has a DeletedAt column for soft delete, and the read side
// already honours it (ListingAuthorizationService filters DeletedAt), so
// switching is a two-line change here:
//     listing.DeletedAt = DateTimeOffset.UtcNow;
//     listing.IsActive  = false;
// in place of db.Listings.Remove(listing).
public class DeleteListingHandler(
    ApplicationDbContext db,
    ListingAuthorizationService listings,
    IRedisCacheService cache)
{
    public async Task<Result> HandleAsync(DeleteListingCommand command, CancellationToken ct = default)
    {
        var id = command.ListingId;
        var listing = await listings.ManageableListings()
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (listing is null)
            return Result.NotFound("Listing not found.");

        db.Listings.Remove(listing);
        await db.SaveChangesAsync(ct);
        await cache.InvalidateFeedsAsync();

        return Result.Success();
    }
}
