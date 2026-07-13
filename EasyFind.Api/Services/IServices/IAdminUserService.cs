using EasyFind.Api.Models.Admin;
using EasyFind.Api.Models.Dto.Common;

namespace EasyFind.Api.Services.IServices;

public interface IAdminUserService
{
    Task<PagedResult<AdminUserListItemDto>> GetUsersAsync(AdminUserFilterDto filter,
        CancellationToken ct = default);
    Task<Result<AdminUserDetailDto>> GetUserDetailAsync(string userId,
        CancellationToken ct = default);
    Task<PagedResult<AdminPaymentListItemDto>> GetPaymentsAsync(AdminPaymentFilterDto filter,
        CancellationToken ct = default);
}