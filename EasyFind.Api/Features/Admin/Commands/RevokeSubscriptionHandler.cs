using EasyFind.Api.Data;
using EasyFind.Api.Models.Admin;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Admin;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Admin.Commands;

// Same two-strings hazard as GrantSubscriptionCommand: name them, do not order them.
public sealed record RevokeSubscriptionCommand(string AdminUserId, string TargetUserId, RevokeSubscriptionDto Revocation);

// SuperAdmin cancels every active subscription for a user and drops them back
// to Free. Also audited via AdminAction.
public class RevokeSubscriptionHandler(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    ILogger<RevokeSubscriptionHandler> logger)
{
    public async Task<Result> HandleAsync(RevokeSubscriptionCommand command, CancellationToken ct = default)
    {
        var (adminUserId, targetUserId, dto) = command;
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
