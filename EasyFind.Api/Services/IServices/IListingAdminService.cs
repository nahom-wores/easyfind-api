using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;

namespace EasyFind.Api.Services.IServices;

public interface IListingAdminService
{
    Task<Result<AdminListingDto>> CreateAsync(CreateListingDto dto, CancellationToken ct = default);
    Task<Result<AdminListingDto>> UpdateAsync(Guid id, UpdateListingDto dto, CancellationToken ct = default);
    Task<Result> SoftDeleteAsync(Guid id, CancellationToken ct = default);
    Task<Result> RestoreAsync(Guid id, CancellationToken ct = default);
    Task<Result> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);
    Task<Result<AdminListingDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<AdminListingDto>> GetAllAsync(AdminListingFilterDto filter, CancellationToken ct = default);
}