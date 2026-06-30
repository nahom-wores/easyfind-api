using EasyFind.Api.Models.Auth;

namespace EasyFind.Api.Models.Subscriptions;

// mutable history of payments
// this tx_ref, this amount, succeeded at this time
public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    // Our generated unique reference — sent to Chapa, echoed back in webhook
    public string TxRef { get; set; } = string.Empty;

    // Chapa's own reference, captured after verification
    public string? ChapaReference { get; set; }

    public SubscriptionTier Tier { get; set; }
    public int AmountEtb { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public PaymentProvider Provider { get; set; } = PaymentProvider.Chapa;

    // Link to the subscription this payment created (once successful)
    public Guid? SubscriptionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public enum PaymentStatus
{
    Pending = 0,    // initiated, awaiting payment
    Success = 1,    // verified, subscription activated
    Failed = 2,     // payment failed or verification failed
}

public enum PaymentProvider
{
    Chapa = 0,
    Telebirr = 1    // future
}