namespace EasyFind.Api.Models.Dto.Listings;

// The cacheable part of a feed item — ranking + static data.
public class CachedFeedItem
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? TitleAm { get; set; }
    public string Organization { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public int? Category { get; set; }
    public DateOnly? Deadline { get; set; }
    public bool IsFeatured { get; set; }
    public int RelevanceScore { get; set; }
    public DateTimeOffset CreatedAt { get; set; }   
}
// What we cache: the ranked page of cacheable items + pagination meta.
public class CachedFeedPage
{
    public List<CachedFeedItem> Items { get; set; } = [];
    public int TotalCount { get; set; }
}