namespace EasyFind.Api.Models.Listings;

public class UserApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public Guid ListingId { get; set; }
    public Listing Listing { get; set; } = null!;
    public ApplicationTrackStatus Status { get; set; } = ApplicationTrackStatus.Saved;
    public string? Notes { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public enum ApplicationTrackStatus
{
    Saved = 0,
    Applied = 1,
    Interview = 2,
    Offer = 3,
    Rejected = 4
}