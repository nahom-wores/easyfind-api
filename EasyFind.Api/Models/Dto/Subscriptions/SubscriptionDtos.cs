using EasyFind.Api.Models.Subscriptions;

namespace EasyFind.Api.Models.Dto.Subscriptions;

public class InitiateSubscriptionDto
{
    public SubscriptionTier Tier { get; set; }
}

public class CheckoutResponseDto
{
    public string CheckoutUrl { get; set; } = string.Empty;
    public string TxRef { get; set; } = string.Empty;
}

public class SubscriptionStatusDto
{
    public string Tier { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
}