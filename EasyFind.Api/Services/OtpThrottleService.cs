using EasyFind.Api.Data;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Options;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyFind.Api.Services;

// Postgres-backed OTP limits, one row per phone number.
//
// Concurrency: the increments are conditional UPDATE statements evaluated by the
// database, not read-then-write in application code. Two simultaneous requests
// therefore cannot both see "2 sends used" and both write 3 — the same guard
// idiom ProcessChapaPaymentHandler uses for payments.
public class OtpThrottleService(
    ApplicationDbContext db,
    IOptions<OtpThrottleOptions> options,
    ILogger<OtpThrottleService> logger) : IOtpThrottle
{
    private readonly OtpThrottleOptions _opts = options.Value;

    public async Task<ThrottleDecision> TryConsumeSendAsync(
        string phoneNumber, CancellationToken ct = default)
    {
        var phone = Normalize(phoneNumber);
        var now = DateTimeOffset.UtcNow;

        await EnsureRowAsync(phone, now, ct);

        // Roll the window over if the current one has expired. Atomic, so a
        // burst arriving at the boundary cannot reset it more than once.
        await db.OtpThrottles
            .Where(t => t.PhoneNumber == phone && t.WindowStartedAt < now - _opts.SendWindow)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.SendCount, 0)
                .SetProperty(t => t.WindowStartedAt, now)
                .SetProperty(t => t.UpdatedAt, now), ct);

        // Consume one send, but only if the allowance has not been spent.
        // rowsAffected == 0 means it had been.
        var consumed = await db.OtpThrottles
            .Where(t => t.PhoneNumber == phone && t.SendCount < _opts.SendsPerWindow)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.SendCount, t => t.SendCount + 1)
                .SetProperty(t => t.UpdatedAt, now), ct);

        if (consumed == 1) return ThrottleDecision.Allow();

        var windowStart = await db.OtpThrottles
            .Where(t => t.PhoneNumber == phone)
            .Select(t => t.WindowStartedAt)
            .FirstOrDefaultAsync(ct);

        logger.LogWarning("OTP send limit reached for {Phone}", phone);
        return ThrottleDecision.Deny(windowStart + _opts.SendWindow - now);
    }

    public async Task<ThrottleDecision> CheckVerifyAllowedAsync(
        string phoneNumber, CancellationToken ct = default)
    {
        var phone = Normalize(phoneNumber);
        var now = DateTimeOffset.UtcNow;

        var lockedUntil = await db.OtpThrottles
            .AsNoTracking()
            .Where(t => t.PhoneNumber == phone)
            .Select(t => t.LockedUntil)
            .FirstOrDefaultAsync(ct);

        // A lockout refuses the number even when the code offered is correct —
        // otherwise an attacker who guesses on the last allowed try still wins.
        return lockedUntil > now
            ? ThrottleDecision.Deny(lockedUntil.Value - now)
            : ThrottleDecision.Allow();
    }

    public async Task RecordFailedVerificationAsync(
        string phoneNumber, CancellationToken ct = default)
    {
        var phone = Normalize(phoneNumber);
        var now = DateTimeOffset.UtcNow;

        await EnsureRowAsync(phone, now, ct);

        await db.OtpThrottles
            .Where(t => t.PhoneNumber == phone)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.FailedVerifications, t => t.FailedVerifications + 1)
                .SetProperty(t => t.UpdatedAt, now), ct);

        // Apply the lockout in a second conditional statement, so whichever
        // request pushed the count over the line is the one that sets it.
        var locked = await db.OtpThrottles
            .Where(t => t.PhoneNumber == phone
                        && t.FailedVerifications >= _opts.MaxFailedVerifications
                        && (t.LockedUntil == null || t.LockedUntil < now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.LockedUntil, now + _opts.Lockout)
                .SetProperty(t => t.FailedVerifications, 0)
                .SetProperty(t => t.UpdatedAt, now), ct);

        if (locked > 0)
            logger.LogWarning("OTP verification locked out for {Phone} until {Until}",
                phone, now + _opts.Lockout);
    }

    public async Task ResetVerificationFailuresAsync(
        string phoneNumber, CancellationToken ct = default)
    {
        var phone = Normalize(phoneNumber);
        var now = DateTimeOffset.UtcNow;

        await db.OtpThrottles
            .Where(t => t.PhoneNumber == phone)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.FailedVerifications, 0)
                .SetProperty(t => t.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(t => t.UpdatedAt, now), ct);
    }

    // Creates the row if this number has never been seen. The unique index on
    // PhoneNumber is the real guard: if two requests race, one insert loses and
    // is swallowed, leaving exactly one row.
    private async Task EnsureRowAsync(string phone, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.OtpThrottles.AnyAsync(t => t.PhoneNumber == phone, ct)) return;

        db.OtpThrottles.Add(new OtpThrottle
        {
            PhoneNumber = phone,
            SendCount = 0,
            WindowStartedAt = now,
            UpdatedAt = now,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost the race; the other request created it.
            db.ChangeTracker.Clear();
        }
    }

    // Limits are per number, so the key has to be stable. Without this, " +251"
    // and "+251" would get separate allowances.
    private static string Normalize(string phoneNumber) => (phoneNumber ?? string.Empty).Trim();
}
