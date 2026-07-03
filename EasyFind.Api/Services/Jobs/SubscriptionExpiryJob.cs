using EasyFind.Api.Data;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Services.Jobs;

public class SubscriptionExpiryJob(ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    ILogger<SubscriptionExpiryJob> logger)
{
    public async Task RunAsync()
    {
        var now = DateTimeOffset.UtcNow;

        // Find subscriptions that are still marked Active but have passed their expiry
        var expired = await db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active && s.ExpiresAt < now)
            .ToListAsync();

        if (expired.Count == 0)
        {
            logger.LogInformation("Subscription expiry job ran — nothing to expire.");
            return;
        }

        foreach (var sub in expired)
        {
            sub.Status = SubscriptionStatus.Expired;
            sub.UpdatedAt = now;

            // Reset the denormalized tier on the user back to Free
            var user = await userManager.FindByIdAsync(sub.UserId);
            if (user != null)
            {
                user.SubscriptionTier = SubscriptionTier.Free;
                await userManager.UpdateAsync(user);
            }
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Subscription expiry job expired {Count} subscription(s).", expired.Count);
    }
}