using EasyFind.Api.Data;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Subscriptions;
using EasyFind.Api.Models.Options;
using EasyFind.Api.Models.Subscriptions;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyFind.Api.Features.Subscriptions.Commands;

// The Chapa reference for the payment to settle. Arrives from both the webhook
// and the browser callback, hence the idempotency requirement below.
public sealed record ProcessChapaPaymentCommand(string TxRef);

// Settles a Chapa payment and activates the subscription.
//
// MUST stay idempotent: Chapa delivers both a webhook and a browser callback
// for the same payment, and retries on failure, so this can run several times
// for one txRef. The guard is the conditional UPDATE ... WHERE Status = Pending
// below — whichever delivery flips the row first wins, the rest no-op.
//
// Body moved verbatim from the old SubscriptionService; do not 'tidy' the
// ordering without understanding the concurrency it is defending against.
public class ProcessChapaPaymentHandler(
    ApplicationDbContext db,
    IChapaClient chapa,
    UserManager<ApplicationUser> userManager,
    IOptions<SubscriptionOptions> subOptions,
    ILogger<ProcessChapaPaymentHandler> logger)
{
    private readonly SubscriptionOptions _opts = subOptions.Value;

    public async Task<Result> HandleAsync(ProcessChapaPaymentCommand command, CancellationToken ct = default)
    
    {
        var txRef = command.TxRef;
        // 1. Find the payment by tx_ref
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.TxRef == txRef, ct);
        if (payment == null)
        {
            logger.LogWarning("Webhook for unknown tx_ref {TxRef}", txRef);
            return Result.Success(); // 200 OK — don't make Chapa retry an unknown ref
        }

        // 2. IDEMPOTENCY: already processed? No-op.
        if (payment.Status == PaymentStatus.Success)
        {
            logger.LogInformation("Webhook for already-processed {TxRef}, ignoring.", txRef);
            return Result.Success();
        }

        // 3. Independently verify with Chapa — the source of truth
        var verification = await chapa.VerifyPaymentAsync(txRef, ct);
        if (verification is not { Status: "success" })
        {
            logger.LogWarning("Verification failed for {TxRef}", txRef);
            payment.Status = PaymentStatus.Failed;
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }

        // 3a. Confirm the amount matches what we expected (anti-tamper)
        if ((int)verification.Amount != payment.AmountEtb)
        {
            logger.LogError("Amount mismatch for {TxRef}: expected {Expected}, got {Actual}",
                txRef, payment.AmountEtb, verification.Amount);
            payment.Status = PaymentStatus.Failed;
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }

        // 4. Activate — in a transaction, with the atomic guard 
        // meaning either payment success | subscription created | user updated All happen or NONE happen (Atomicity)
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Atomic guard: flip to Success ONLY if still Pending.
            // If another delivery already did it, rowsAffected = 0 → no-op.
            var rowsAffected = await db.Payments
                .Where(p => p.Id == payment.Id && p.Status == PaymentStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, PaymentStatus.Success)
                    .SetProperty(p => p.ChapaReference, verification.Reference)
                    .SetProperty(p => p.CompletedAt, DateTimeOffset.UtcNow), ct);

            if (rowsAffected == 0)
            {
                // Another concurrent delivery won. Already processed.
                await tx.CommitAsync(ct);
                return Result.Success();
            }

            // Create or extend the subscription (stacking logic)
            var durationDays = _opts.DurationDays;
            var now = DateTimeOffset.UtcNow;

            var existing = await db.Subscriptions
                .Where(s => s.UserId == payment.UserId && s.Status == SubscriptionStatus.Active)
                .OrderByDescending(s => s.ExpiresAt)
                .FirstOrDefaultAsync(ct);

            if (existing != null)
            {
                // Stack onto the later of (now, current expiry)
                var baseDate = existing.ExpiresAt > now ? existing.ExpiresAt : now;
                existing.ExpiresAt = baseDate.AddDays(durationDays);
                existing.Tier = payment.Tier; // upgrade/keep tier
                existing.UpdatedAt = now;
                payment.SubscriptionId = existing.Id;
            }
            else
            {
                var sub = new Subscription
                {
                    UserId = payment.UserId,
                    Tier = payment.Tier,
                    Status = SubscriptionStatus.Active,
                    StartedAt = now,
                    ExpiresAt = now.AddDays(durationDays),
                };
                db.Subscriptions.Add(sub);
                await db.SaveChangesAsync(ct);
                payment.SubscriptionId = sub.Id;
            }

            // Reflect tier on the user for fast access (feed gating reads this)
            var user = await userManager.FindByIdAsync(payment.UserId);
            if (user != null)
            {
                user.SubscriptionTier = payment.Tier;
                await userManager.UpdateAsync(user);
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            logger.LogInformation("Subscription activated for user {UserId}, tier {Tier}, tx {TxRef}",
                payment.UserId, payment.Tier, txRef);

            return Result.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            logger.LogError(ex, "Failed to activate subscription for {TxRef}", txRef);
            return Result.Failure("Activation failed.", ErrorType.Failure);
        }
    }
}
