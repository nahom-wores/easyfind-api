namespace EasyFind.Api.Models.Options;

public class NotificationOptions
{
    public const string SectionName = "Notifications";

    // The SQS queue NotificationPublisher sends to. Deliberately has no default:
    // it used to be hardcoded to the production queue, so every environment —
    // a developer machine included — texted real users.
    public string QueueUrl { get; set; } = string.Empty;
}
