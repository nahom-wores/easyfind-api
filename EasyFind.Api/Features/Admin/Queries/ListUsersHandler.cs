using EasyFind.Api.Data;
using EasyFind.Api.Models.Admin;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Admin.Queries;

public sealed record ListUsersQuery(AdminUserFilterDto Filter);

// The admin user table: search by phone/name, filter by tier, page.
public class ListUsersHandler(ApplicationDbContext db)
{
    public async Task<Result<PagedResult<AdminUserListItemDto>>> HandleAsync(ListUsersQuery query, CancellationToken ct = default)
    {
        var filter = query.Filter;
        var users = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            users = users.Where(u =>
                (u.PhoneNumber != null && u.PhoneNumber.Contains(term)) ||
                (u.FirstName != null && EF.Functions.ILike(u.FirstName, $"%{term}%")) ||
                (u.LastName != null && EF.Functions.ILike(u.LastName, $"%{term}%")));
        }

        if (filter.Tier.HasValue)
            users = users.Where(u => (int)u.SubscriptionTier == filter.Tier.Value);

        users = users.OrderByDescending(u => u.CreatedAt);

        var total = await users.CountAsync(ct);

        // Profile existence check via a subquery, projected in SQL
        var profileUserIds = db.UserProfiles.AsNoTracking().Select(p => p.UserId);

        var items = await users
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

        return Result<PagedResult<AdminUserListItemDto>>.Success(new PagedResult<AdminUserListItemDto>
        {
            Items = items, TotalCount = total, Page = filter.Page, PageSize = filter.PageSize
        });
    }
}
