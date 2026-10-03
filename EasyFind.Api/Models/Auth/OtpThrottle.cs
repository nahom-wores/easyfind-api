namespace EasyFind.Api.Models.Auth;

// One row per phone number, tracking how often codes have been sent to it and
// how many wrong codes have been offered for it.
//
// This lives in Postgres rather than in memory on purpose: the API runs several
// ECS tasks, so an in-process counter would multiply every limit by the task
// count and reset on each deploy. The database is the only shared, durable place
// to count.
public class OtpThrottle
{
    public int Id { get; set; }

    // Unique — the partition key for every limit here.
    public string PhoneNumber { get; set; } = string.Empty;

    // ── Send window ──────────────────────────────────────────────────────
    public int SendCount { get; set; }
    public DateTimeOffset WindowStartedAt { get; set; }

    // ── Verification failures ────────────────────────────────────────────
    public int FailedVerifications { get; set; }

    // Set once the failure limit is hit. While this is in the future the number
    // is refused even if the code offered is correct.
    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
