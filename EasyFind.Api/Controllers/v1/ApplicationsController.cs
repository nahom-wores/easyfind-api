using System.Security.Claims;
using Asp.Versioning;
using EasyFind.Api.Features.Applications.Commands;
using EasyFind.Api.Features.Applications.Queries;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;

// The user's application tracker.
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[ApiVersion("1.0")]
[Authorize]
public class ApplicationsController : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ApiResponse>> Create(
        [FromBody] CreateApplicationDto dto,
        [FromServices] CreateApplicationHandler handler,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return HandleResult(await handler.HandleAsync(new CreateApplicationCommand(userId, dto), ct));
    }

    [HttpPut("{applicationId:guid}")]
    public async Task<ActionResult<ApiResponse>> Update(
        Guid applicationId,
        [FromBody] UpdateApplicationDto dto,
        [FromServices] UpdateApplicationHandler handler,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return HandleResult(await handler.HandleAsync(new UpdateApplicationCommand(userId, applicationId, dto), ct), "Updated.");
    }

    [HttpDelete("{applicationId:guid}")]
    public async Task<ActionResult<ApiResponse>> Delete(
        Guid applicationId,
        [FromServices] DeleteApplicationHandler handler,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return HandleResult(await handler.HandleAsync(new DeleteApplicationCommand(userId, applicationId), ct), "Removed.");
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse>> GetMine(
        [FromServices] GetUserApplicationsHandler handler,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 20;

        return HandleResult(await handler.HandleAsync(new GetUserApplicationsQuery(userId, page, pageSize), ct));
    }
}
