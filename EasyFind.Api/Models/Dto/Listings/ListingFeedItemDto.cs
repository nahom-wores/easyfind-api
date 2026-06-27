namespace EasyFind.Api.Models.Dto.Listings;

public class ListingFeedItemDto
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
    public bool IsBookmarked { get; set; }
    public string? ApplicationStatus { get; set; }
    public int RelevanceScore { get; set; }       // exposed for debugging; can hide later
    public DateTimeOffset CreatedAt { get; set; }
}