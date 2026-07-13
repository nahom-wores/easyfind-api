using EasyFind.Api.Models.Auth;

namespace EasyFind.Api.Models.Subscriptions;

public class Subscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public SubscriptionTier Tier { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum SubscriptionTier
{
    Free = 0,
    Pro = 1
}

public enum SubscriptionStatus
{
    Active = 0,
    Expired = 1,
    Cancelled = 2
}