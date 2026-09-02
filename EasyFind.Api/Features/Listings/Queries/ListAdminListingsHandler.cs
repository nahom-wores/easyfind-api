using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Listings.Queries;

// The admin listing table: filter, search, page.
public class ListAdminListingsHandler(ListingAuthorizationService listings)
{
    public async Task<Result<PagedResult<AdminListingDto>>> HandleAsync(
        AdminListingFilterDto filter, CancellationToken ct = default)
    {
        // Start from everything this admin may manage, then narrow.
        var query = listings.ManageableListings().AsNoTracking();

        // Soft-deleted rows are hidden unless explicitly requested.
        if (!filter.IncludeDeleted)
            query = query.Where(l => l.DeletedAt == null);

        if (filter.Type.HasValue)
            query = query.Where(l => l.Type == filter.Type.Value);

        if (filter.IsActive.HasValue)
            query = query.Where(l => l.IsActive == filter.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(filter.CountryCode))
        {
            var cc = filter.CountryCode.ToUpperInvariant();
            query = query.Where(l => l.CountryCode == cc);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            // ILike is Postgres-only (case-insensitive LIKE).
            query = query.Where(l =>
                EF.Functions.ILike(l.Title, $"%{term}%") ||
                EF.Functions.ILike(l.Organization, $"%{term}%"));
        }

        // Count before paging, so TotalCount reflects the whole filtered set.
        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(l => l.ToAdminDto())
            .ToListAsync(ct);

        return Result<PagedResult<AdminListingDto>>.Success(new PagedResult<AdminListingDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize
        });
    }
}
