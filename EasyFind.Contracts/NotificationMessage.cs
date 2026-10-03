namespace EasyFind.Contracts;

// The Type strings both sides switch on. The match is exact and case-sensitive,
// so producer and consumer must share these rather than each typing a literal —
// they drifted once ("Payment_success" vs "payment_success") and every message
// would have gone to the DLQ.
public static class NotificationTypes
{
    public const string PaymentSuccess = "payment_success";
}

// The envelope — every message has a type + version, so consumers know
// what they're handling and you can evolve payloads safely later.
public class NotificationMessage
{
    // Unique per publish. Identifies this one message in logs on both sides.
    public Guid MessageId { get; set; } = Guid.NewGuid();

    // Stable per business event (e.g. "payment_success:{txRef}"), so the same
    // event published or delivered twice carries the same key. Nothing
    // deduplicates on it yet — see doc.md, Known limitations.
    public string IdempotencyKey { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;   // one of NotificationTypes
    public int Version { get; set; } = 1;
    public string Payload { get; set; } = string.Empty; // JSON of the specific data
}

// The actual data for a payment notification
public class PaymentSuccessPayload
{
    public string TxRef { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public int AmountEtb { get; set; }
    public string Tier { get; set; } = string.Empty;
}
