using EasyFind.Api.Data;
using EasyFind.Api.Extensions;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Listings.Commands;

public sealed record DeleteListingCommand(Guid ListingId);

// Admin withdraws a listing.
//
// SOFT delete: the row stays, DeletedAt is stamped and IsActive cleared. This is
// what makes POST /admin/listings/{id}/restore mean anything — a hard delete
// left nothing to restore, so that endpoint could never succeed.
//
// It also protects history. UserApplication references listings with
// DeleteBehavior.Restrict, so hard-deleting a listing somebody had applied to
// would fail outright; and a user's tracker entry should survive the listing
// being withdrawn.
//
// The read side already honours this: ListingAuthorizationService filters
// DeletedAt for consumers and keeps it visible to staff.
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

        // Already withdrawn — nothing to do, and re-stamping would lose the
        // original deletion time.
        if (listing.DeletedAt is not null)
            return Result.Success();

        listing.DeletedAt = DateTimeOffset.UtcNow;
        listing.IsActive = false;
        listing.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        await cache.InvalidateFeedsAsync();

        return Result.Success();
    }
}
