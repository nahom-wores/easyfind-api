using EasyFind.Api.Data;
using EasyFind.Api.Models.Admin;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Admin.Queries;

public sealed record GetUserDetailQuery(string UserId);

// One user, everything about them: roles, profile, subscription, activity
// counts and recent payments. Several small queries rather than one big join.
public class GetUserDetailHandler(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager)
{
    public async Task<Result<AdminUserDetailDto>> HandleAsync(GetUserDetailQuery query, CancellationToken ct = default)
    {
        var userId = query.UserId;
         var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user == null) return Result<AdminUserDetailDto>.NotFound("User not found.");

            var roles = await userManager.GetRolesAsync(user);

            var profile = await db.UserProfiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId, ct);

            var sub = await db.Subscriptions.AsNoTracking()
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.ExpiresAt)
                .FirstOrDefaultAsync(ct);

            var bookmarkCount = await db.Bookmarks.AsNoTracking().CountAsync(b => b.UserId == userId, ct);
            var appCount = await db.UserApplications.AsNoTracking().CountAsync(a => a.UserId == userId, ct);
            var docCount = await db.UserDocuments.AsNoTracking().CountAsync(d => d.UserId == userId, ct);

            var recentPayments = await db.Payments.AsNoTracking()
                .Where(p => p.UserId == userId)
                .OrderByDescending(p => p.CreatedAt)
                .Take(10)
                .Select(p => new AdminPaymentDto
                {
                    Id = p.Id,
                    TxRef = p.TxRef,
                    ChapaReference = p.ChapaReference,
                    Tier = p.Tier.ToString(),
                    AmountEtb = p.AmountEtb,
                    Status = p.Status.ToString(),
                    Provider = p.Provider.ToString(),
                    CreatedAt = p.CreatedAt,
                    CompletedAt = p.CompletedAt
                })
                .ToListAsync(ct);

            var dto = new AdminUserDetailDto
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                PhoneNumber = user.PhoneNumber,
                Email = user.Email,
                PhoneConfirmed = user.PhoneNumberConfirmed,
                Roles = roles.ToList(),
                CreatedAt = user.CreatedAt,
                Tier = user.SubscriptionTier.ToString(),
                SubscriptionStatus = sub?.Status.ToString(),
                SubscriptionExpiresAt = sub?.ExpiresAt,
                HasProfile = profile != null,
                SeekingType = profile?.SeekingType.ToString(),
                TargetCountries = profile?.TargetCountries ?? [],
                BookmarkCount = bookmarkCount,
                ApplicationCount = appCount,
                DocumentCount = docCount,
                RecentPayments = recentPayments
            };

            return Result<AdminUserDetailDto>.Success(dto);
    }
}
