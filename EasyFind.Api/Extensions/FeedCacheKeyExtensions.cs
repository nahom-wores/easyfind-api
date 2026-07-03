using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Subscriptions;

namespace EasyFind.Api.Extensions;

public static class FeedCacheKeyExtensions
{
    public const string FeedCachePrefix = "feed:";
    public static string ToFeedCacheKey(this FeedRequestDto r, string userId, SubscriptionTier tier)
    {
        var country = string.IsNullOrWhiteSpace(r.CountryCode) ? "all" : r.CountryCode.ToUpperInvariant();
        var search  = string.IsNullOrWhiteSpace(r.Search) ? "none" : r.Search.Trim().ToLowerInvariant();
        return $"{FeedCachePrefix}{userId}:{country}:{search}:{r.Page}:{r.PageSize}:{(int)tier}";
    }
}