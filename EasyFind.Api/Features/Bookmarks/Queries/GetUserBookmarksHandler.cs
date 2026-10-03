using EasyFind.Api.Data;
using EasyFind.Api.Features.Listings;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Enum;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Bookmarks.Queries;

public sealed record GetUserBookmarksQuery(string UserId, int Page, int PageSize);

// The user's saved listings, newest save first.
public class GetUserBookmarksHandler(
    ApplicationDbContext db,
    ListingAuthorizationService listings)
{
    public async Task<Result<PagedResult<ListingFeedItemDto>>> HandleAsync(GetUserBookmarksQuery query, CancellationToken ct = default)
    {
        var (userId, page, pageSize) = query;
        // Joined through the authorization service rather than the b.Listing
        // navigation property, so a withdrawn listing can't resurface here.
        // Inactive listings are kept — an expired job you saved stays visible.
        var saved = db.Bookmarks
            .AsNoTracking()
            .Where(b => b.UserId == userId)
            .Join(listings.AuthorizedListings(activeOnly: false),
                b => b.ListingId, l => l.Id, (b, l) => new { b, l })
            .OrderByDescending(x => x.b.CreatedAt)
            .Select(x => x.l);

        var totalCount = await saved.CountAsync(ct);

        var bookmarked = await saved
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Application statuses for this page in one saved, then O(1) lookups —
        // same N+1-avoidance pattern as the feed.
        var ids = bookmarked.Select(l => l.Id).ToList();
        var appStatuses = await db.UserApplications
            .AsNoTracking()
            .Where(a => a.UserId == userId && ids.Contains(a.ListingId))
            .Select(a => new { a.ListingId, a.Status })
            .ToListAsync(ct);

        // NOTE: mapped inline rather than via ListingMapper.ToFeedItemDto on
        // purpose — the saved list shows Organization ungated and carries no
        // ApplyUrl/IsLocked. Only the feed applies the paywall.
        var items = bookmarked.Select(l => new ListingFeedItemDto
        {
            Id = l.Id,
            Type = l.Type.ToString(),
            Title = l.Title,
            TitleAm = l.TitleAm,
            Organization = l.Organization,
            CountryCode = l.CountryCode,
            Category = l.Type == ListingType.Job ? (int?)l.JobCategory : (int?)l.ScholarshipField,
            Deadline = l.Deadline,
            IsFeatured = l.IsFeatured,
            IsBookmarked = true,   // by definition — these are bookmarks
            ApplicationStatus = appStatuses.FirstOrDefault(a => a.ListingId == l.Id)?.Status.ToString(),
            CreatedAt = l.CreatedAt
        }).ToList();

        return Result<PagedResult<ListingFeedItemDto>>.Success(new PagedResult<ListingFeedItemDto>
        {
            Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize
        });
    }
}
