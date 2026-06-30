using System.Net;
using System.Security.Claims;
using Asp.Versioning;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[ApiVersion("1.0")]
[Authorize]
public class ApplicationsController(IApplicationService applicationService) : ApiControllerBase
{
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpPost]
    public async Task<ActionResult<ApiResponse>> Create([FromBody] CreateApplicationDto dto, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();
        var result = await applicationService.CreateAsync(UserId, dto, ct);
        return HandleResult(result);
    }

    [HttpPut("{applicationId:guid}")]
    public async Task<ActionResult<ApiResponse>> Update(Guid applicationId, [FromBody] UpdateApplicationDto dto, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();
        var result = await applicationService.UpdateAsync(UserId, applicationId, dto, ct);
        return HandleResult(result, "Updated.");
    }

    [HttpDelete("{applicationId:guid}")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid applicationId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();
        var result = await applicationService.DeleteAsync(UserId, applicationId, ct);
        return HandleResult(result, "Removed.");
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse>> GetMine([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 20;

        var result = await applicationService.GetUserApplicationsAsync(UserId, page, pageSize, ct);
        var response = new ApiResponse { IsSuccess = true, Result = result };
        return Ok(response);
    }
}