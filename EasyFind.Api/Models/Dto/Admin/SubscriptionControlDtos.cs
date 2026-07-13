using EasyFind.Api.Models.Subscriptions;

namespace EasyFind.Api.Models.Dto.Admin;


public class GrantSubscriptionDto
{
    public SubscriptionTier Tier { get; set; }   // Basic or Premium
    public int DurationDays { get; set; } = 30;
    public string? Reason { get; set; }           // why (comp, offline payment, support)
}

public class RevokeSubscriptionDto
{
    public string? Reason { get; set; }
}