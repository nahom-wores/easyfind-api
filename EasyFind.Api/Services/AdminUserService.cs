using EasyFind.Api.Data;
using EasyFind.Api.Models.Admin;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Services;

public class AdminUserService(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    : IAdminUserService
{
    public async Task<PagedResult<AdminUserListItemDto>> GetUsersAsync(AdminUserFilterDto filter,
        CancellationToken ct = default)
    {
        var query = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(u =>
                (u.PhoneNumber != null && u.PhoneNumber.Contains(term)) ||
                (u.FirstName != null && EF.Functions.ILike(u.FirstName, $"%{term}%")) ||
                (u.LastName != null && EF.Functions.ILike(u.LastName, $"%{term}%")));
        }

        if (filter.Tier.HasValue)
            query = query.Where(u => (int)u.SubscriptionTier == filter.Tier.Value);

        query = query.OrderByDescending(u => u.CreatedAt);

        var total = await query.CountAsync(ct);

        // Profile existence check via a subquery, projected in SQL
        var profileUserIds = db.UserProfiles.AsNoTracking().Select(p => p.UserId);

        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(u => new AdminUserListItemDto
            {
                Id = u.Id,
                FirstName = u.FirstName,
                LastName = u.LastName,
                PhoneNumber = u.PhoneNumber,
                Tier = u.SubscriptionTier.ToString(),
                HasProfile = profileUserIds.Contains(u.Id),
                CreatedAt = u.CreatedAt
            })
            .ToListAsync(ct);

        return new PagedResult<AdminUserListItemDto>
        {
            Items = items, TotalCount = total, Page = filter.Page, PageSize = filter.PageSize
        };
    }

    public async Task<Result<AdminUserDetailDto>> GetUserDetailAsync(string userId, CancellationToken ct = default)
    {
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

    public async Task<PagedResult<AdminPaymentListItemDto>> GetPaymentsAsync(AdminPaymentFilterDto filter,
        CancellationToken ct = default)
    {
        var query = db.Payments.AsNoTracking();

        if (filter.Status.HasValue)
            query = query.Where(p => (int)p.Status == filter.Status.Value);

        query = query.OrderByDescending(p => p.CreatedAt);

        var total = await query.CountAsync(ct);

        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(p => new AdminPaymentListItemDto
            {
                Id = p.Id,
                UserId = p.UserId,
                UserPhone = p.User.PhoneNumber,
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

        return new PagedResult<AdminPaymentListItemDto>
        {
            Items = items, TotalCount = total, Page = filter.Page, PageSize = filter.PageSize
        };
    }
}