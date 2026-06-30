using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Profile;

namespace EasyFind.Api.Services.IServices;

public interface IProfileService
{
    Task<Result<ProfileResponseDto>> UpsertAsync(string userId, OnboardingDto dto, CancellationToken ct = default);
    Task<Result<ProfileResponseDto>> GetAsync(string userId, CancellationToken ct = default);
}