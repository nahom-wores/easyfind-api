using EasyFind.Api.Data;
using EasyFind.Api.Extensions;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Listings.Commands;

// Admin edits a listing. The admin sends the full corrected listing, so every
// editable field is overwritten.
public class UpdateListingHandler(
    ApplicationDbContext db,
    ListingAuthorizationService listings,
    IRedisCacheService cache)
{
    public async Task<Result<AdminListingDto>> HandleAsync(
        Guid id, UpdateListingDto dto, CancellationToken ct = default)
    {
        // ManageableListings() includes soft-deleted rows — an admin can fix a
        // listing before restoring it.
        var listing = await listings.ManageableListings()
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (listing is null)
            return Result<AdminListingDto>.NotFound("Listing not found.");

        dto.ApplyTo(listing);
        listing.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        await cache.InvalidateFeedsAsync();

        return Result<AdminListingDto>.Success(listing.ToAdminDto());
    }
}
