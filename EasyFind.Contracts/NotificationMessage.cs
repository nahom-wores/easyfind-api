namespace EasyFind.Contracts;

// The envelope — every message has a type + version, so consumers know
// what they're handling and you can evolve payloads safely later.
public class NotificationMessage
{
    public string Type { get; set; } = string.Empty;   // e.g. "payment_success"
    public int Version { get; set; } = 1;
    public string Payload { get; set; } = string.Empty; // JSON of the specific data
}
// The actual data for a payment notification
public class PaymentSuccessPayload
{
    public string UserId { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public int AmountEtb { get; set; }
    public string Tier { get; set; } = string.Empty;
}