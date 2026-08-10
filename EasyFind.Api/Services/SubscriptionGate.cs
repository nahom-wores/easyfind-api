using EasyFind.Api.Models.Options;
using EasyFind.Api.Models.Subscriptions;
using Microsoft.Extensions.Options;

namespace EasyFind.Api.Services;

public class SubscriptionGate(IOptions<SubscriptionOptions> opts)
{
    private readonly SubscriptionOptions _opts = opts.Value;
    // Paid = Basic or Premium. Free = the gated tier.
    public bool IsPaid(SubscriptionTier tier) => tier != SubscriptionTier.Free;

    // How many feed results a free user may see
    public int FreeFeedCap => _opts.FreeFeedCap;

    // Convenience: the effective result cap for a tier
    public int? ResultCapFor(SubscriptionTier tier) =>
        IsPaid(tier) ? null : _opts.FreeFeedCap;   // null = unlimited
    
    public string? GateOrganization(string? organization, SubscriptionTier tier)
        => IsPaid(tier) ? organization : null;
    
    public string? GateApplyUrl(string? applyUrl, SubscriptionTier tier)
        => IsPaid(tier) ? applyUrl : null;
}