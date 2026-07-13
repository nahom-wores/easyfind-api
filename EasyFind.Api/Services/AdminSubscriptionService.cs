using EasyFind.Api.Data;
using EasyFind.Api.Models.Admin;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Admin;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Subscriptions;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Services;

public class AdminSubscriptionService(ApplicationDbContext db,
    UserManager<ApplicationUser> userManager, ILogger<AdminSubscriptionService> logger) 
    : IAdminSubscriptionService
{
    public async Task<Result> GrantAsync(string adminUserId, string targetUserId,
        GrantSubscriptionDto dto, CancellationToken ct = default)
    {
        if (dto.Tier == SubscriptionTier.Free)
                return Result.Validation("Cannot grant the Free tier. Use revoke to downgrade.");
            if (dto.DurationDays <= 0)
                return Result.Validation("Duration must be positive.");

            var user = await userManager.FindByIdAsync(targetUserId);
            if (user == null) return Result.NotFound("User not found.");

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var now = DateTimeOffset.UtcNow;

                // Same stacking logic as the payment webhook: extend if active, else start fresh
                var existing = await db.Subscriptions
                    .Where(s => s.UserId == targetUserId && s.Status == SubscriptionStatus.Active)
                    .OrderByDescending(s => s.ExpiresAt)
                    .FirstOrDefaultAsync(ct);

                if (existing != null)
                {
                    var baseDate = existing.ExpiresAt > now ? existing.ExpiresAt : now;
                    existing.ExpiresAt = baseDate.AddDays(dto.DurationDays);
                    existing.Tier = dto.Tier;
                    existing.UpdatedAt = now;
                }
                else
                {
                    db.Subscriptions.Add(new Subscription
                    {
                        UserId = targetUserId,
                        Tier = dto.Tier,
                        Status = SubscriptionStatus.Active,
                        StartedAt = now,
                        ExpiresAt = now.AddDays(dto.DurationDays),
                    });
                }

                // Mirror tier onto the user (same rule as everywhere: sub first, then mirror)
                user.SubscriptionTier = dto.Tier;
                await userManager.UpdateAsync(user);

                // Audit — WHO granted WHAT to WHOM and WHY
                db.AdminActions.Add(new AdminAction
                {
                    AdminUserId = adminUserId,
                    TargetUserId = targetUserId,
                    ActionType = AdminActionType.SubscriptionGranted,
                    Details = $"Granted {dto.Tier} for {dto.DurationDays} days",
                    Reason = dto.Reason,
                });

                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                logger.LogInformation("Admin {Admin} granted {Tier} to {Target} for {Days}d. Reason: {Reason}",
                    adminUserId, dto.Tier, targetUserId, dto.DurationDays, dto.Reason ?? "(none)");

                return Result.Success();
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                logger.LogError(ex, "Failed to grant subscription to {Target}", targetUserId);
                return Result.Failure("Grant failed.", ErrorType.Failure);
            }
    }

    public async Task<Result> RevokeAsync(string adminUserId, string targetUserId,
        RevokeSubscriptionDto dto, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(targetUserId);
        if (user == null) return Result.NotFound("User not found.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;

            var activeSubs = await db.Subscriptions
                .Where(s => s.UserId == targetUserId && s.Status == SubscriptionStatus.Active)
                .ToListAsync(ct);

            if (activeSubs.Count == 0)
                return Result.Validation("User has no active subscription to revoke.");

            foreach (var sub in activeSubs)
            {
                sub.Status = SubscriptionStatus.Cancelled;
                sub.CancelledAt = now;
                sub.UpdatedAt = now;
            }

            // Reset user to Free
            user.SubscriptionTier = SubscriptionTier.Free;
            await userManager.UpdateAsync(user);

            db.AdminActions.Add(new AdminAction
            {
                AdminUserId = adminUserId,
                TargetUserId = targetUserId,
                ActionType = AdminActionType.SubscriptionRevoked,
                Details = $"Revoked {activeSubs.Count} active subscription(s)",
                Reason = dto.Reason,
            });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            logger.LogInformation("Admin {Admin} revoked subscription for {Target}. Reason: {Reason}",
                adminUserId, targetUserId, dto.Reason ?? "(none)");

            return Result.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            logger.LogError(ex, "Failed to revoke subscription for {Target}", targetUserId);
            return Result.Failure("Revoke failed.", ErrorType.Failure);
        }
    }
}