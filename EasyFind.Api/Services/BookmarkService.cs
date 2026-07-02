using EasyFind.Api.Data;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Enum;
using EasyFind.Api.Models.Listings;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Services;

public class BookmarkService(ApplicationDbContext db, SubscriptionGate gate) : IBookmarkService
{
    
    public async Task<Result> AddAsync(string userId, Guid listingId, CancellationToken ct = default)
    {
        var tier = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.SubscriptionTier)
            .FirstOrDefaultAsync(ct);

        if (!gate.IsPaid(tier))
            return Result.Forbidden("Upgrade to a paid plan to save listings.");
        
        // Verify the listing exists and is active (query filter excludes soft-deleted)
        var exists = await db.Listings
            .AnyAsync(l => l.Id == listingId && l.IsActive, ct);
        if (!exists) return Result.NotFound("Listing not found");
        
        // The unique index (UserId, ListingId) is our real guard against duplicates.
        // We check first for a friendly message, but catch the race below.
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
            // Unique index violation — two requests raced. Treat as success (idempotent).
            return Result.Conflict("Already bookmarked");
        }

        return Result.Success();
    }

    public async Task<Result> RemoveAsync(string userId, Guid listingId, CancellationToken ct = default)
    {
        var bookmark = await db.Bookmarks
            .FirstOrDefaultAsync(b => b.UserId == userId && b.ListingId == listingId, ct);

        if (bookmark == null) return Result.NotFound("Bookmark not found");

        db.Bookmarks.Remove(bookmark);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<PagedResult<ListingFeedItemDto>> GetUserBookmarksAsync(string userId,
        int page, int pageSize, CancellationToken ct = default)
    {
        // Bookmarks joined to their listings, newest bookmark first
        var query = db.Bookmarks
            .AsNoTracking()
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => b.Listing);

        var totalCount = await query.CountAsync(ct);

        var listings = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Application statuses for these listings (same N+1-avoidance pattern as the feed)
        var ids = listings.Select(l => l.Id).ToList();
        var appStatuses = await db.UserApplications
            .AsNoTracking()
            .Where(a => a.UserId == userId && ids.Contains(a.ListingId))
            .Select(a => new { a.ListingId, a.Status })
            .ToListAsync(ct);

        var items = listings.Select(l => new ListingFeedItemDto
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

        return new PagedResult<ListingFeedItemDto>
        {
            Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize
        };
    }
}