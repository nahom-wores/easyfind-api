using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;

namespace EasyFind.Api.Services.IServices;

public interface IApplicationService
{
    Task<Result<ApplicationItemDto>> CreateAsync(string userId, CreateApplicationDto dto, CancellationToken ct = default);
    Task<Result> UpdateAsync(string userId, Guid applicationId, UpdateApplicationDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(string userId, Guid applicationId, CancellationToken ct = default);
    Task<PagedResult<ApplicationItemDto>> GetUserApplicationsAsync(string userId, int page, int pageSize, CancellationToken ct = default);
}