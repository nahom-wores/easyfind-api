using EasyFind.Api.Data;
using EasyFind.Api.Features.Listings;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Applications.Queries;

// The user's application tracker, most recently touched first.
public class GetUserApplicationsHandler(
    ApplicationDbContext db,
    ListingAuthorizationService listings)
{
    public async Task<Result<PagedResult<ApplicationItemDto>>> HandleAsync(
        string userId, int page, int pageSize, CancellationToken ct = default)
    {
        // Joined through the authorization service rather than the a.Listing
        // navigation property, so a withdrawn listing can't resurface here.
        // Inactive listings are kept — a job you applied to stays in your tracker.
        //
        // Projected in SQL (not via ApplicationMapper) so paging happens in the
        // database rather than in memory.
        var query = db.UserApplications
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .Join(listings.AuthorizedListings(activeOnly: false),
                a => a.ListingId, l => l.Id, (a, l) => new { a, l })
            .OrderByDescending(x => x.a.UpdatedAt)
            .Select(x => new ApplicationItemDto
            {
                Id = x.a.Id,
                ListingId = x.a.ListingId,
                ListingTitle = x.l.Title,
                Organization = x.l.Organization,
                CountryCode = x.l.CountryCode,
                Status = x.a.Status.ToString(),
                Notes = x.a.Notes,
                AppliedAt = x.a.AppliedAt,
                Deadline = x.l.Deadline,
                CreatedAt = x.a.CreatedAt,
                UpdatedAt = x.a.UpdatedAt
            });

        var totalCount = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return Result<PagedResult<ApplicationItemDto>>.Success(new PagedResult<ApplicationItemDto>
        {
            Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize
        });
    }
}
