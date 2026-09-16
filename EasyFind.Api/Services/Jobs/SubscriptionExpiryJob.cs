using EasyFind.Api.Data;
using EasyFind.Api.Models.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Services.Jobs;

// Ends lapsed subscriptions. Runs nightly at 02:00 UTC (registered in Program.cs).
//
// Two writes, and they must not come apart:
//   1. Active subscriptions past their expiry  -> Expired
//   2. Users no longer covered by one          -> SubscriptionTier.Free
//
// ApplicationUser.SubscriptionTier is a denormalized copy of "does this user
// hold a live subscription" — GetFeedHandler reads it on every feed request to
// decide the free result cap and whether Organization/ApplyUrl are nulled. The
// Subscription rows are the truth; this job is what keeps the copy honest.
//
// PREVIOUSLY: the two writes were not in a transaction. The tier reset saved
// immediately (one UserManager.UpdateAsync per user) while the status changes
// waited for a single SaveChangesAsync at the very end, so a failure partway
// through left paying customers downgraded to Free with their subscriptions
// still marked Active — locked out of what they had paid for, with nothing to
// re-run until the next night.
public class SubscriptionExpiryJob(
    ApplicationDbContext db,
    ILogger<SubscriptionExpiryJob> logger)
{
    public async Task RunAsync()
    {
        var now = DateTimeOffset.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            // ── 1. Expire the subscriptions that have run out ──
            //
            // One UPDATE, not a read-then-loop: the previous version issued a
            // FindByIdAsync and an UpdateAsync per expired row.
            var expired = await db.Subscriptions
                .Where(s => s.Status == SubscriptionStatus.Active && s.ExpiresAt < now)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, SubscriptionStatus.Expired)
                    .SetProperty(x => x.UpdatedAt, now));

            // ── 2. Reconcile the denormalized tier ──
            //
            // Derived from the subscription rows rather than from the set just
            // updated above, which matters in two ways:
            //
            //  - A user with a SECOND, still-valid subscription keeps their
            //    tier. Downgrading every user who had something expire would
            //    cut off someone who renewed while an old row was lapsing —
            //    including a renewal that lands mid-run.
            //  - It is a repair, not just a follow-up write. Any user whose
            //    flag drifted from their rows for any reason (a half-finished
            //    earlier run, a webhook that died between its writes) is put
            //    back in step the next time this runs.
            //
            // Note the ExpiresAt > now inside the subquery: a row still flagged
            // Active but already past its expiry does not count as cover, so
            // this is correct whether or not step 1 has run.
            var downgraded = await db.Users
                .Where(u => u.SubscriptionTier != SubscriptionTier.Free
                            && !db.Subscriptions.Any(s =>
                                s.UserId == u.Id
                                && s.Status == SubscriptionStatus.Active
                                && s.ExpiresAt > now))
                .ExecuteUpdateAsync(u => u
                    .SetProperty(x => x.SubscriptionTier, SubscriptionTier.Free));

            await tx.CommitAsync();

            if (expired == 0 && downgraded == 0)
                logger.LogInformation("Subscription expiry job ran — nothing to expire.");
            else
                logger.LogInformation(
                    "Subscription expiry job expired {Expired} subscription(s) and downgraded {Downgraded} user(s).",
                    expired, downgraded);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();

            // Rethrown, not swallowed: Hangfire has to see the failure to retry
            // it and to show it in the dashboard. Logging and returning quietly
            // would leave expired subscriptions live until someone noticed.
            logger.LogError(ex, "Subscription expiry job failed — no changes were applied.");
            throw;
        }
    }
}
