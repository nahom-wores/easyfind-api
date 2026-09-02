using Asp.Versioning;
using EasyFind.Api.Features.Profile.Commands;
using EasyFind.Api.Features.Profile.Queries;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Profile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;

// The job-seeker's own preferences — the inputs the feed ranks on.
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[ApiVersion("1.0")]
[Authorize]
public class ProfileController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse>> GetMyProfile(
        [FromServices] GetProfileHandler handler,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();
        return HandleResult(await handler.HandleAsync(UserId, ct));
    }

    // Create-or-update: one endpoint for onboarding and every later edit.
    [HttpPut]
    public async Task<ActionResult<ApiResponse>> Upsert(
        [FromBody] OnboardingDto dto,
        [FromServices] UpsertProfileHandler handler,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();
        return HandleResult(await handler.HandleAsync(UserId, dto, ct));
    }
}
