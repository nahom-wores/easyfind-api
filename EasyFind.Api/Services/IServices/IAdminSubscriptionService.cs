using EasyFind.Api.Models.Dto.Admin;
using EasyFind.Api.Models.Dto.Common;

namespace EasyFind.Api.Services.IServices;

public interface IAdminSubscriptionService
{
    Task<Result> GrantAsync(string adminUserId, string targetUserId,
        GrantSubscriptionDto dto, CancellationToken ct = default);

    Task<Result> RevokeAsync(string adminUserId, string targetUserId,
        RevokeSubscriptionDto dto, CancellationToken ct = default);
}