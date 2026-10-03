using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Subscriptions;
using EasyFind.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Listings.Queries;

// Which listing, and who is asking (their tier decides what is gated).
public sealed record GetListingDetailQuery(Guid ListingId, string UserId);

// One listing, consumer view. Free users get the listing with the company name
// and apply link stripped out — see SubscriptionGate.
public class GetListingDetailHandler(
    ListingAuthorizationService listings,
    UserManager<ApplicationUser> userManager,
    SubscriptionGate gate)
{
    public async Task<Result<ListingDetailDto>> HandleAsync(GetListingDetailQuery query, CancellationToken ct = default)
    {
        var (listingId, userId) = query;
        var listing = await listings.AuthorizedListings()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == listingId && l.IsActive, ct);

        if (listing is null)
            return Result<ListingDetailDto>.NotFound("Listing not found.");

        var user = await userManager.FindByIdAsync(userId);
        var tier = user?.SubscriptionTier ?? SubscriptionTier.Free;

        return Result<ListingDetailDto>.Success(listing.ToDetailDto(gate, tier));
    }
}
