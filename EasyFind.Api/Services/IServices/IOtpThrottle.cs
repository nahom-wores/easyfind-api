namespace EasyFind.Api.Services.IServices;

// The outcome of a throttle check. RetryAfter is surfaced to the client in the
// Retry-After header so the app can show "try again in N minutes" instead of
// guessing.
public readonly record struct ThrottleDecision(bool Allowed, TimeSpan RetryAfter)
{
    public static ThrottleDecision Allow() => new(true, TimeSpan.Zero);
    public static ThrottleDecision Deny(TimeSpan retryAfter) =>
        new(false, retryAfter < TimeSpan.Zero ? TimeSpan.Zero : retryAfter);
}

// Per-phone-number limits on OTP sending and verification.
//
// Counting lives in Postgres, not in memory: the API runs on several ECS tasks,
// so an in-process counter would multiply every limit by the task count and
// reset on every deploy.
public interface IOtpThrottle
{
    // Call BEFORE sending a code. Consumes one send from the number's allowance
    // when it returns Allowed.
    Task<ThrottleDecision> TryConsumeSendAsync(string phoneNumber, CancellationToken ct = default);

    // Call BEFORE checking a code, to refuse numbers that are locked out.
    Task<ThrottleDecision> CheckVerifyAllowedAsync(string phoneNumber, CancellationToken ct = default);

    // Call AFTER a failed check. Locks the number out once the limit is reached.
    Task RecordFailedVerificationAsync(string phoneNumber, CancellationToken ct = default);

    // Call AFTER a successful check — clears failures so a legitimate user is
    // not carrying old strikes.
    Task ResetVerificationFailuresAsync(string phoneNumber, CancellationToken ct = default);
}
