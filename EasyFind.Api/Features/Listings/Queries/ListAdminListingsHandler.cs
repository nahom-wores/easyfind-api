using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Listings.Queries;

public sealed record ListAdminListingsQuery(AdminListingFilterDto Filter);

// The admin listing table: filter, search, page.
public class ListAdminListingsHandler(ListingAuthorizationService listings)
{
    public async Task<Result<PagedResult<AdminListingDto>>> HandleAsync(ListAdminListingsQuery query, CancellationToken ct = default)
    {
        var filter = query.Filter;
        // Start from everything this admin may manage, then narrow.
        var listingsQuery = listings.ManageableListings().AsNoTracking();

        // Soft-deleted rows are hidden unless explicitly requested.
        if (!filter.IncludeDeleted)
            listingsQuery = listingsQuery.Where(l => l.DeletedAt == null);

        if (filter.Type.HasValue)
            listingsQuery = listingsQuery.Where(l => l.Type == filter.Type.Value);

        if (filter.IsActive.HasValue)
            listingsQuery = listingsQuery.Where(l => l.IsActive == filter.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(filter.CountryCode))
        {
            var cc = filter.CountryCode.ToUpperInvariant();
            listingsQuery = listingsQuery.Where(l => l.CountryCode == cc);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            // ILike is Postgres-only (case-insensitive LIKE).
            listingsQuery = listingsQuery.Where(l =>
                EF.Functions.ILike(l.Title, $"%{term}%") ||
                EF.Functions.ILike(l.Organization, $"%{term}%"));
        }

        // Count before paging, so TotalCount reflects the whole filtered set.
        var totalCount = await listingsQuery.CountAsync(ct);

        var items = await listingsQuery
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
