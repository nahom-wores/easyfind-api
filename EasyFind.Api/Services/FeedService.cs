using EasyFind.Api.Data;
using EasyFind.Api.Extensions;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Enum;
using EasyFind.Api.Models.Users;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Services;

public class FeedService(
    ApplicationDbContext db,
    SubscriptionGate gate,
    IRedisCacheService cache) : IFeedService
{
    // Scoring weights — central, tunable. Change here, whole feed re-ranks.
    private const int CountryWeight  = 50;
    private const int CategoryWeight = 30;
    private const int DegreeWeight   = 15;
    private const int FeaturedWeight = 10;

    public async Task<PagedResult<ListingFeedItemDto>> GetPersonalizedFeedAsync(
        string userId, FeedRequestDto request, CancellationToken ct = default)
    {
        // Tier drives the free-tier result cap and is part of the cache key.
        var tier = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.SubscriptionTier)
            .FirstOrDefaultAsync(ct);

        // how many lists to show
        var resultCap = gate.ResultCapFor(tier);

        // ── 1. RANKING: from cache, or compute + cache on miss ──
        var cacheKey = request.ToFeedCacheKey(userId, tier);
        // asking redis do u have this page cached?
        var cachedPage = await cache.GetAsync<CachedFeedPage>(cacheKey);
        if (cachedPage is null)
        {
            // cache miss compute from db
            cachedPage = await BuildRankedPageAsync(userId, request, resultCap, ct);
            // Cache result TTL(Time To Live) set to 5 minute
            await cache.SetAsync(cacheKey, cachedPage, TimeSpan.FromMinutes(5));
        }

        // ── 2. ENRICH per-user flags FRESH every time (never cached) ──
        var pageListingIds = cachedPage.Items.Select(i => i.Id).ToList();

        var bookmarkedIds = (await db.Bookmarks
                .AsNoTracking()
                .Where(b => b.UserId == userId && pageListingIds.Contains(b.ListingId))
                .Select(b => b.ListingId)
                .ToListAsync(ct))
            .ToHashSet();   // O(1) lookups

        var appStatusLookup = (await db.UserApplications
                .AsNoTracking()
                .Where(a => a.UserId == userId && pageListingIds.Contains(a.ListingId))
                .Select(a => new { a.ListingId, a.Status })
                .ToListAsync(ct))
            .ToDictionary(x => x.ListingId, x => x.Status);   // O(1) lookups

        // ── 3. Combine cached ranking + fresh flags into the final DTO ──
        var items = cachedPage.Items.Select(c => new ListingFeedItemDto
        {
            Id = c.Id,
            Type = c.Type,
            Title = c.Title,
            TitleAm = c.TitleAm,
            Organization = c.Organization,
            CountryCode = c.CountryCode,
            Category = c.Category,
            Deadline = c.Deadline,
            IsFeatured = c.IsFeatured,
            RelevanceScore = c.RelevanceScore,
            CreatedAt = c.CreatedAt,
            IsBookmarked = bookmarkedIds.Contains(c.Id),
            ApplicationStatus = appStatusLookup.TryGetValue(c.Id, out var status)
                ? status.ToString()
                : null
        }).ToList();

        return new PagedResult<ListingFeedItemDto>
        {
            Items = items,
            TotalCount = cachedPage.TotalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }

    // Computes the personalized ranking. Runs only on cache miss.
    // Returns the cacheable page (ranking + static data, NO per-user flags).
    private async Task<CachedFeedPage> BuildRankedPageAsync(
        string userId, FeedRequestDto request, int? resultCap, CancellationToken ct)
    {
        var profile = await db.UserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        var targetCountries = profile?.TargetCountries ?? [];
        var jobCats   = profile?.PreferredJobCategories.Select(c => (int)c).ToList() ?? [];
        var schFields = profile?.PreferredScholarshipFields.Select(f => (int)f).ToList() ?? [];
        var targetDegree = (int?)profile?.TargetDegreeLevel;

        var query = db.Listings
            .AsNoTracking()
            .Where(l => l.IsActive);

        if (profile != null)
        {
            if (profile.SeekingType == SeekingType.Job)
                query = query.Where(l => l.Type == ListingType.Job);
            else if (profile.SeekingType == SeekingType.Scholarship)
                query = query.Where(l => l.Type == ListingType.Scholarship);
            // SeekingType.Both => no filter
        }

        if (!string.IsNullOrWhiteSpace(request.CountryCode))
            query = query.Where(l => l.CountryCode == request.CountryCode);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(l =>
                EF.Functions.ILike(l.Title, $"%{term}%") ||
                EF.Functions.ILike(l.Organization, $"%{term}%"));
        }

        var totalMatching = await query.CountAsync(ct);
        var totalCount = resultCap.HasValue
            ? Math.Min(totalMatching, resultCap.Value)
            : totalMatching;

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

        int skip = (request.Page - 1) * request.PageSize;
        int take = request.PageSize;
        if (resultCap.HasValue)
            take = Math.Min(take, Math.Max(0, resultCap.Value - skip));

        var pageItems = take == 0
            ? []
            : await scored
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Listing.CreatedAt)
                .Skip(skip).Take(take)
                .ToListAsync(ct);

        var items = pageItems.Select(x =>
        {
            var l = x.Listing;
            int? category = l.Type == ListingType.Job
                ? (int?)l.JobCategory
                : (int?)l.ScholarshipField;

            return new CachedFeedItem
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
                RelevanceScore = x.Score,
                CreatedAt = l.CreatedAt
            };
        }).ToList();

        return new CachedFeedPage { Items = items, TotalCount = totalCount };
    }
}