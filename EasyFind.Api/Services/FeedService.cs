using EasyFind.Api.Data;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Enum;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Services;

public class FeedService(ApplicationDbContext db) : IFeedService
{
    private readonly ApplicationDbContext _db = db;
    
    // Scoring weights — central, tunable. Change here, whole feed re-ranks.
    private const int CountryWeight  = 50;
    private const int CategoryWeight = 30;
    private const int DegreeWeight   = 15;
    private const int FeaturedWeight = 10;

    public async Task<PagedResult<ListingFeedItemDto>> GetPersonalizedFeedAsync(string userId,
        FeedRequestDto request, CancellationToken ct = default)
    {
        // 1. Load the user's profile (small, single row). We pull the preference
        //    values into locals so EF can embed them as query parameters.
        var profile = await _db.UserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
            
        var targetCountries = profile?.TargetCountries ?? [];
        var jobCats   = profile?.PreferredJobCategories.Select(c => (int)c).ToList() ?? [];
        var schFields = profile?.PreferredScholarshipFields.Select(f => (int)f).ToList() ?? [];
        var targetDegree = (int?)profile?.TargetDegreeLevel;
        
        // 2. Start from listings. Query filter already excludes soft-deleted.
        //    Stay in IQueryable — nothing materializes yet.
        var query = _db.Listings
            .AsNoTracking()
            .Where(l => l.IsActive);
        
        // 3. EXPLICIT filters (these remove rows) ───────────────────
        if (request.Type.HasValue)
            query = query.Where(l => l.Type == request.Type.Value);

        if (!string.IsNullOrWhiteSpace(request.CountryCode))
            query = query.Where(l => l.CountryCode == request.CountryCode);
    
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(l =>
                EF.Functions.ILike(l.Title, $"%{term}%") ||
                EF.Functions.ILike(l.Organization, $"%{term}%"));
        }
        
        // 4. Count AFTER filters, BEFORE paging (for pagination metadata)
        var totalCount = await query.CountAsync(ct);

        // 5. SCORING + projection. Profile prefs only ADD to score.
        //    EF translates this into SQL CASE expressions.
        var scored = query.Select(l => new
        {
            Listing = l,
            Score =
                (targetCountries.Contains(l.CountryCode) ? CountryWeight : 0) +
                (l.Type == ListingType.Job && l.JobCategory != null
                                           && jobCats.Contains((int)l.JobCategory) ? CategoryWeight : 0) +
                (l.Type == ListingType.Scholarship && l.ScholarshipField != null
                                                   && schFields.Contains((int)l.ScholarshipField) ? CategoryWeight : 0) +
                (l.Type == ListingType.Scholarship && targetDegree != null
                                                   && l.DegreeLevel != null && (int)l.DegreeLevel == targetDegree ? DegreeWeight : 0) +
                (l.IsFeatured ? FeaturedWeight : 0)
        });
        // 6. Order by score, then recency. Page. THIS is where SQL runs.
        var pageItems = await scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Listing.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);
        
        // 7. Enrich with this user's bookmark + application state.
        //    Only for the listings on THIS page — small, targeted queries.
        var pageListingIds = pageItems.Select(x => x.Listing.Id).ToList();

        var bookmarkedIds = await _db.Bookmarks
            .AsNoTracking()
            .Where(b => b.UserId == userId && pageListingIds.Contains(b.ListingId))
            .Select(b => b.ListingId)
            .ToListAsync(ct);
        
        var appStatuses = await _db.UserApplications
            .AsNoTracking()
            .Where(a => a.UserId == userId && pageListingIds.Contains(a.ListingId))
            .Select(a => new { a.ListingId, a.Status })
            .ToListAsync(ct);
        
        // 8. Map to DTO
        var items = pageItems.Select(x =>
        {
            var l = x.Listing;
            var category = l.Type == ListingType.Job
                ? (int?)l.JobCategory
                : (int?)l.ScholarshipField;

            return new ListingFeedItemDto
            {
                Id = l.Id,
                Type = l.Type.ToString(),
                Title = l.Title,
                TitleAm = l.TitleAm,
                Organization = l.Organization,
                CountryCode = l.CountryCode,
                Category = category,
                Deadline = l.Deadline,
                IsFeatured = l.IsFeatured,
                IsBookmarked = bookmarkedIds.Contains(l.Id),
                ApplicationStatus = appStatuses
                    .FirstOrDefault(a => a.ListingId == l.Id)?.Status.ToString(),
                RelevanceScore = x.Score,
                CreatedAt = l.CreatedAt
            };
        }).ToList();
        return new PagedResult<ListingFeedItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }
}