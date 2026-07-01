using EasyFind.Api.Data;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Subscriptions;
using EasyFind.Api.Models.Subscriptions;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Services;

public class SubscriptionService(
    ApplicationDbContext db,
    IChapaClient chapa,
    UserManager<ApplicationUser> userManager,
    IConfiguration config,
    ILogger<SubscriptionService> logger) : ISubscriptionService
{
    public async Task<Result<CheckoutResponseDto>> InitiateAsync(string userId, SubscriptionTier tier,
        CancellationToken ct = default)
    {
        if (tier == SubscriptionTier.Free)
            return Result<CheckoutResponseDto>.Validation("Cannot purchase the Free tier.");

        var user = await userManager.FindByIdAsync(userId);
        if (user == null) return Result<CheckoutResponseDto>.NotFound("User not found.");

        var amount = tier switch
        {
            SubscriptionTier.Basic => config.GetValue<int>("Subscription:BasicPriceEtb"),
            SubscriptionTier.Premium => config.GetValue<int>("Subscription:PremiumPriceEtb"),
            _ => 0
        };
        if (amount <= 0)
            return Result<CheckoutResponseDto>.Failure("Invalid plan pricing.", ErrorType.Failure);

        // Generate our unique reference
        var txRef = $"easyfind-{Guid.NewGuid():N}";

        // Record the pending payment BEFORE calling Chapa
        var payment = new Payment
        {
            UserId = userId,
            TxRef = txRef,
            Tier = tier,
            AmountEtb = amount,
            Status = PaymentStatus.Pending,
            Provider = PaymentProvider.Chapa,
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        // Call Chapa initialize
        var initRequest = new ChapaInitializeRequest
        {
            Amount = amount.ToString(),
            Currency = "ETB",
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            FirstName = user.FirstName,
            LastName = user.LastName,
            TxRef = txRef,
            CallbackUrl = config["Subscription:CallbackUrl"] ?? "",
            ReturnUrl = config["Subscription:ReturnUrl"] ?? "",
        };

        var checkoutUrl = await chapa.InitializePaymentAsync(initRequest, ct);

        if (string.IsNullOrEmpty(checkoutUrl))
        {
            // Chapa failed — mark the payment failed so it's not left dangling
            payment.Status = PaymentStatus.Failed;
            await db.SaveChangesAsync(ct);
            return Result<CheckoutResponseDto>.Failure(
                "Could not start payment. Please try again.", ErrorType.Failure);
        }

        return Result<CheckoutResponseDto>.Success(new CheckoutResponseDto
        {
            CheckoutUrl = checkoutUrl,
            TxRef = txRef
        });
    }

    public async Task<Result> HandleWebhookAsync(string txRef, CancellationToken ct = default)
    {
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
            var durationDays = config.GetValue<int>("Subscription:DurationDays");
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

    public async Task<Result<SubscriptionStatusDto>> GetMyStatusAsync(string userId,
        CancellationToken ct = default)
    {
        var sub = await db.Subscriptions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.ExpiresAt)
            .FirstOrDefaultAsync(ct);

        if (sub == null)
            return Result<SubscriptionStatusDto>.Success(new SubscriptionStatusDto
            {
                Tier = "Free", Status = "None", ExpiresAt = null, IsActive = false
            });

        var isActive = sub.Status == SubscriptionStatus.Active && sub.ExpiresAt > DateTimeOffset.UtcNow;

        return Result<SubscriptionStatusDto>.Success(new SubscriptionStatusDto
        {
            Tier = isActive ? sub.Tier.ToString() : "Free",
            Status = sub.Status.ToString(),
            ExpiresAt = sub.ExpiresAt,
            IsActive = isActive
        });
    }
}