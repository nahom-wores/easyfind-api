using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Listings.Queries;

// One listing, admin view: ungated, and includes soft-deleted rows.
public class GetAdminListingHandler(ListingAuthorizationService listings)
{
    public async Task<Result<AdminListingDto>> HandleAsync(Guid id, CancellationToken ct = default)
    {
        var listing = await listings.ManageableListings()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        return listing is null
            ? Result<AdminListingDto>.NotFound("Listing not found.")
            : Result<AdminListingDto>.Success(listing.ToAdminDto());
    }
}
