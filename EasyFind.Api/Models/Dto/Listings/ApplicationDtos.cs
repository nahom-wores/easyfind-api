using EasyFind.Api.Models.Listings;

namespace EasyFind.Api.Models.Dto.Listings;

public class CreateApplicationDto
{
    public Guid ListingId { get; set; }
    public ApplicationTrackStatus Status { get; set; } = ApplicationTrackStatus.Saved;
    public string? Notes { get; set; }
}

// Update status and/or notes on an existing entry
public class UpdateApplicationDto
{
    public ApplicationTrackStatus Status { get; set; }
    public string? Notes { get; set; }
}

// What we return
public class ApplicationItemDto
{
    public Guid Id { get; set; }
    public Guid ListingId { get; set; }
    public string ListingTitle { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public DateOnly? Deadline { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}