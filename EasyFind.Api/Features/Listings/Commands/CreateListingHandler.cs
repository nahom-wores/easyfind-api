using EasyFind.Api.Data;
using EasyFind.Api.Extensions;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Listings;
using EasyFind.Api.Services.IServices;

namespace EasyFind.Api.Features.Listings.Commands;

// Admin-supplied listing to create.
public sealed record CreateListingCommand(CreateListingDto Listing);

// Admin creates a listing. Every new listing changes what the feed should
// return, so the cached feeds are dropped.
public class CreateListingHandler(ApplicationDbContext db, IRedisCacheService cache)
{
    public async Task<Result<AdminListingDto>> HandleAsync(CreateListingCommand command, CancellationToken ct = default)
    {
        var dto = command.Listing;
        var listing = new Listing();
        dto.ApplyTo(listing);

        // Lifecycle fields the request doesn't get to set
        listing.IsActive = true;
        listing.Source = dto.Source ?? "Manual";

        db.Listings.Add(listing);
        await db.SaveChangesAsync(ct);

        await cache.InvalidateFeedsAsync();

        return Result<AdminListingDto>.Success(listing.ToAdminDto());
    }
}
