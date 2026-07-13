using EasyFind.Api.Data;
using EasyFind.Api.Models.Dto.Admin;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Subscriptions;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Services;

public class AdminStatsService(ApplicationDbContext db, IRedisCacheService cache) : IAdminStatsService
{
    public async Task<Result<AdminOverviewStatsDto>> GetOverviewAsync(CancellationToken ct = default)
    {
        // Stats are expensive and don't need to be real-time — cache 60s.
            const string cacheKey = "admin:stats:overview";
            var cached = await cache.GetAsync<AdminOverviewStatsDto>(cacheKey);
            if (cached != null) return Result<AdminOverviewStatsDto>.Success(cached);

            var now = DateTimeOffset.UtcNow;
            var last7 = now.AddDays(-7);
            var last30 = now.AddDays(-30);

            // Users by tier — one grouped query, not four separate counts
            var tierCounts = await db.Users
                .AsNoTracking()
                .GroupBy(u => u.SubscriptionTier)
                .Select(g => new { Tier = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            int CountFor(SubscriptionTier t) => tierCounts.FirstOrDefault(x => x.Tier == t)?.Count ?? 0;

            var totalUsers = tierCounts.Sum(x => x.Count);

            var newUsers7 = await db.Users.AsNoTracking()
                .CountAsync(u => u.CreatedAt >= last7, ct);
            var newUsers30 = await db.Users.AsNoTracking()
                .CountAsync(u => u.CreatedAt >= last30, ct);

            var activeSubs = await db.Subscriptions.AsNoTracking()
                .CountAsync(s => s.Status == SubscriptionStatus.Active && s.ExpiresAt > now, ct);

            var activeListings = await db.Listings.AsNoTracking().CountAsync(l => l.IsActive, ct);
            var inactiveListings = await db.Listings.AsNoTracking()
                .IgnoreQueryFilters().CountAsync(l => !l.IsActive, ct);

            var appsTracked = await db.UserApplications.AsNoTracking().CountAsync(ct);
            var bookmarks = await db.Bookmarks.AsNoTracking().CountAsync(ct);

            // Revenue from successful payments only
            var successfulPayments = db.Payments.AsNoTracking()
                .Where(p => p.Status == PaymentStatus.Success);

            var totalRevenue = await successfulPayments.SumAsync(p => (decimal?)p.AmountEtb, ct) ?? 0;
            var successCount = await successfulPayments.CountAsync(ct);
            var revenue30 = await successfulPayments
                .Where(p => p.CompletedAt >= last30)
                .SumAsync(p => (decimal?)p.AmountEtb, ct) ?? 0;

            var failedCount = await db.Payments.AsNoTracking()
                .CountAsync(p => p.Status == PaymentStatus.Failed, ct);

            var dto = new AdminOverviewStatsDto
            {
                TotalUsers = totalUsers,
                FreeUsers = CountFor(SubscriptionTier.Free),
                ProUsers = CountFor(SubscriptionTier.Pro),
                NewUsersLast7Days = newUsers7,
                NewUsersLast30Days = newUsers30,
                ActiveSubscriptions = activeSubs,
                TotalActiveListings = activeListings,
                TotalInactiveListings = inactiveListings,
                TotalApplicationsTracked = appsTracked,
                TotalBookmarks = bookmarks,
                TotalRevenueEtb = totalRevenue,
                SuccessfulPayments = successCount,
                FailedPayments = failedCount,
                RevenueLast30DaysEtb = revenue30,
            };

            await cache.SetAsync(cacheKey, dto, TimeSpan.FromSeconds(60));
            return Result<AdminOverviewStatsDto>.Success(dto);
    }
}