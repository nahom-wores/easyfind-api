using EasyFind.Api.Data;
using EasyFind.Api.Extensions;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Enum;
using EasyFind.Api.Models.Users;
using EasyFind.Api.Services;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Listings.Queries;

// Whose feed, and which page/filters of it.
public sealed record GetFeedQuery(string UserId, FeedRequestDto Request);

// The personalized feed — the core read of the product.
//
// The shape to keep in mind:
//   ranking  = the same for everyone on the same tier  -> cached 5 minutes
//   flags    = bookmarked / application status, per user -> ALWAYS fresh
// Mixing the two up is how you ship someone else's bookmarks, so the split is
// deliberate: never put a user-mutable field into CachedFeedPage.
public class GetFeedHandler(
    ApplicationDbContext db,
    ListingAuthorizationService listings,
    SubscriptionGate gate,
    IRedisCacheService cache)
{
    // Scoring weights — central and tunable. Change here, the whole feed re-ranks.
    // NOTE: ListingScorer holds the same formula for unit tests. It is a copy:
    // the real ranking has to be an expression tree so Postgres can sort it.
    // Change one, change the other.
    private const int CountryWeight = 50;
    private const int CategoryWeight = 30;
    private const int DegreeWeight = 15;
    private const int FeaturedWeight = 10;

    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    public async Task<Result<PagedResult<ListingFeedItemDto>>> HandleAsync(GetFeedQuery query, CancellationToken ct = default)
    {
        var (userId, request) = query;
        // Tier drives the free-tier result cap, and is part of the cache key so
        // a free and a paid user can never share a cached page.
        var tier = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.SubscriptionTier)
            .FirstOrDefaultAsync(ct);

        var resultCap = gate.ResultCapFor(tier);   // null = unlimited

        // ── 1. RANKING: from cache, or compute and cache on miss ──
        var cacheKey = request.ToFeedCacheKey(userId, tier);

        var cachedPage = await cache.GetAsync<CachedFeedPage>(cacheKey);
        if (cachedPage is null)
        {
            cachedPage = await BuildRankedPageAsync(userId, request, resultCap, ct);
            await cache.SetAsync(cacheKey, cachedPage, CacheFor);
        }

        // ── 2. FLAGS: re-read for this user on every request, never cached ──
        var pageListingIds = cachedPage.Items.Select(i => i.Id).ToList();

        // Both lookups are one query each, then O(1) in memory — no N+1.
        var bookmarkedIds = (await db.Bookmarks
                .AsNoTracking()
                .Where(b => b.UserId == userId && pageListingIds.Contains(b.ListingId))
                .Select(b => b.ListingId)
                .ToListAsync(ct))
            .ToHashSet();

        var appStatusLookup = (await db.UserApplications
                .AsNoTracking()
                .Where(a => a.UserId == userId && pageListingIds.Contains(a.ListingId))
                .Select(a => new { a.ListingId, a.Status })
                .ToListAsync(ct))
            .ToDictionary(x => x.ListingId, x => x.Status);

        // ── 3. Cached ranking + fresh flags = what the client receives ──
        var items = cachedPage.Items
            .Select(c => c.ToFeedItemDto(
                gate,
                tier,
                isBookmarked: bookmarkedIds.Contains(c.Id),
                applicationStatus: appStatusLookup.TryGetValue(c.Id, out var status)
                    ? status.ToString()
                    : null))
            .ToList();

        return Result<PagedResult<ListingFeedItemDto>>.Success(new PagedResult<ListingFeedItemDto>
        {
            Items = items,
            TotalCount = cachedPage.TotalCount,
            Page = request.Page,
            PageSize = request.PageSize
        });
    }

    // Computes the ranking. Runs only on a cache miss.
    // Returns the cacheable half: ranking + fields identical for every viewer.
    private async Task<CachedFeedPage> BuildRankedPageAsync(
        string userId, FeedRequestDto request, int? resultCap, CancellationToken ct)
    {
        // The user's onboarding answers are what "personalized" means here.
        var profile = await db.UserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        var targetCountries = profile?.TargetCountries ?? [];
        var jobCats = profile?.PreferredJobCategories.Select(c => (int)c).ToList() ?? [];
        var schFields = profile?.PreferredScholarshipFields.Select(f => (int)f).ToList() ?? [];
        var targetDegree = (int?)profile?.TargetDegreeLevel;

        // AuthorizedListings() is the only sanctioned way in — see
        // ListingAuthorizationService.
        var query = listings.AuthorizedListings()
            .AsNoTracking()
            .Where(l => l.IsActive);

        // ── Hard filters: what the user is allowed to be shown at all ──
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
            // ILike is Postgres-only (case-insensitive LIKE).
            query = query.Where(l =>
                EF.Functions.ILike(l.Title, $"%{term}%") ||
                EF.Functions.ILike(l.Organization, $"%{term}%"));
        }

        var totalMatching = await query.CountAsync(ct);
        var totalCount = resultCap.HasValue
            ? Math.Min(totalMatching, resultCap.Value)
            : totalMatching;

        // ── Soft ranking: scored in SQL so Postgres does the sort ──
        // This stays an inline expression on purpose. Extracting it into a C#
        // method would force EF to pull every row into memory to rank it.
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

        // Free users get a hard cap, so the last page may be a partial one
        // (or empty, once skip has passed the cap).
        var skip = (request.Page - 1) * request.PageSize;
        var take = request.PageSize;
        if (resultCap.HasValue)
            take = Math.Min(take, Math.Max(0, resultCap.Value - skip));

        var pageItems = take == 0
            ? []
            : await scored
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Listing.CreatedAt)   // newest breaks ties
                .Skip(skip).Take(take)
                .ToListAsync(ct);

        return new CachedFeedPage
        {
            Items = pageItems.Select(x => x.Listing.ToCachedFeedItem(x.Score)).ToList(),
            TotalCount = totalCount
        };
    }
}
