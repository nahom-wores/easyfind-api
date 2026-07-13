using EasyFind.Api.Models.Dto.Admin;
using EasyFind.Api.Models.Dto.Common;

namespace EasyFind.Api.Services.IServices;

public interface IAdminStatsService
{
    Task<Result<AdminOverviewStatsDto>> GetOverviewAsync(CancellationToken ct = default);

}