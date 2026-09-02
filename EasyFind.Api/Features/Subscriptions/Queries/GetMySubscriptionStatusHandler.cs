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

namespace EasyFind.Api.Features.Subscriptions.Queries;

// What the client shows on the account screen. A user with no subscription
// row is reported as Free rather than as an error.
public class GetMySubscriptionStatusHandler(ApplicationDbContext db)
{
    public async Task<Result<SubscriptionStatusDto>> HandleAsync(string userId,
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
