using EasyFind.Api.Data;
using EasyFind.Api.Features.Listings;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Listings;
using EasyFind.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Bookmarks.Commands;

public sealed record AddBookmarkCommand(string UserId, Guid ListingId);

// Save a listing. Paid feature.
public class AddBookmarkHandler(
    ApplicationDbContext db,
    ListingAuthorizationService listings,
    SubscriptionGate gate)
{
    public async Task<Result> HandleAsync(AddBookmarkCommand command, CancellationToken ct = default)
    {
        var (userId, listingId) = command;
        var tier = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.SubscriptionTier)
            .FirstOrDefaultAsync(ct);

        if (!gate.IsPaid(tier))
            return Result.Forbidden("Upgrade to a paid plan to save listings.");

        // The listing must exist and be one this user is allowed to see
        var exists = await listings.AuthorizedListings()
            .AnyAsync(l => l.Id == listingId && l.IsActive, ct);
        if (!exists) return Result.NotFound("Listing not found");

        // The unique index (UserId, ListingId) is the real guard against
        // duplicates. We check first for a friendly answer, but catch the race.
        var already = await db.Bookmarks
            .AnyAsync(b => b.UserId == userId && b.ListingId == listingId, ct);
        if (already) return Result.Success();

        db.Bookmarks.Add(new Bookmark { UserId = userId, ListingId = listingId });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Unique index violation — two requests raced.
            return Result.Conflict("Already bookmarked");
        }

        return Result.Success();
    }
}
