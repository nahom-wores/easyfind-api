using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Subscriptions;
using EasyFind.Api.Services.IServices;

namespace EasyFind.Api.Extensions;

public static class FeedCacheKeyExtensions
{
    public const string FeedCachePrefix = "feed:";

    // Feed pages are cached per user, so there is no way to surgically update a
    // single listing — any listing write drops every cached page. Every listing
    // command calls this; it is what makes caching the feed safe.
    public static Task InvalidateFeedsAsync(this IRedisCacheService cache)
        => cache.RemoveByPatternAsync($"{FeedCachePrefix}*");

    // Narrower version: only this user's pages. Use when what changed is the
    // user's own ranking input (their profile), not the listings themselves.
    public static Task InvalidateFeedsForUserAsync(this IRedisCacheService cache, string userId)
        => cache.RemoveByPatternAsync($"{FeedCachePrefix}{userId}:*");

    // Every request field that changes the ranked page must be in here, or two
    // different pages share one cache entry. Type was once missing — harmless
    // only because the handler ignored it too.
    public static string ToFeedCacheKey(this FeedRequestDto r, string userId, SubscriptionTier tier)
    {
        var country = string.IsNullOrWhiteSpace(r.CountryCode) ? "all" : r.CountryCode.ToUpperInvariant();
        var search  = string.IsNullOrWhiteSpace(r.Search) ? "none" : r.Search.Trim().ToLowerInvariant();
        var type    = r.Type.HasValue ? ((int)r.Type.Value).ToString() : "any";
        return $"{FeedCachePrefix}{userId}:{country}:{search}:{type}:{r.Page}:{r.PageSize}:{(int)tier}";
    }
}