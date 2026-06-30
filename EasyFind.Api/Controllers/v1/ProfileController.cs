using System.Security.Claims;
using Asp.Versioning;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Profile;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;


[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[ApiVersion("1.0")]
[Authorize]
public class ProfileController(IProfileService profileService) : ApiControllerBase
{
    
    [HttpGet]
    public async Task<ActionResult<ApiResponse>> GetMyProfile(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();
        var result = await profileService.GetAsync(UserId, ct);
        return HandleResult(result);
    }

    [HttpPut]
    public async Task<ActionResult<ApiResponse>> Upsert([FromBody] OnboardingDto dto, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();
        var result = await profileService.UpsertAsync(UserId, dto, ct);
        return HandleResult(result);
    }
}