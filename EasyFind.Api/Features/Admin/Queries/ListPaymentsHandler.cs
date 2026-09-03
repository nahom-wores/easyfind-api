using EasyFind.Api.Data;
using EasyFind.Api.Models.Admin;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Admin.Queries;

public sealed record ListPaymentsQuery(AdminPaymentFilterDto Filter);

// The admin payments table, newest first, optionally filtered by status.
public class ListPaymentsHandler(ApplicationDbContext db)
{
    public async Task<Result<PagedResult<AdminPaymentListItemDto>>> HandleAsync(ListPaymentsQuery query, CancellationToken ct = default)
    {
        var filter = query.Filter;
        var payments = db.Payments.AsNoTracking();

        if (filter.Status.HasValue)
            payments = payments.Where(p => (int)p.Status == filter.Status.Value);

        payments = payments.OrderByDescending(p => p.CreatedAt);

        var total = await payments.CountAsync(ct);

        var items = await payments
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

        return Result<PagedResult<AdminPaymentListItemDto>>.Success(new PagedResult<AdminPaymentListItemDto>
        {
            Items = items, TotalCount = total, Page = filter.Page, PageSize = filter.PageSize
        });
    }
}
