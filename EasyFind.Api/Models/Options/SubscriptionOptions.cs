namespace EasyFind.Api.Models.Options;

public class SubscriptionOptions
{
    public const string SectionName = "Subscription";

    public int ProPriceEtb { get; set; }
    public int DurationDays { get; set; }
    public string CallbackUrl { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public int FreeFeedCap { get; set; } = 5;
}