using System.Security.Claims;
using Asp.Versioning;
using EasyFind.Api.Features.Admin.Commands;
using EasyFind.Api.Features.Admin.Queries;
using EasyFind.Api.Models.Admin;
using EasyFind.Api.Models.Dto.Admin;
using EasyFind.Api.Models.Dto.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;

[Route("api/v{version:apiVersion}/admin/users")]
[ApiController]
[ApiVersion("1.0")]
[Authorize(Roles = "Admin")]
public class AdminUsersController : ApiControllerBase
{
    // Who performed the action — recorded on the AdminAction audit row.
    private string? AdminId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("{userId}/subscription/grant")]
    public async Task<ActionResult<ApiResponse>> GrantSubscription(
        string userId,
        [FromBody] GrantSubscriptionDto dto,
        [FromServices] GrantSubscriptionHandler handler,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(AdminId)) return Unauthorized();
        return HandleResult(await handler.HandleAsync(AdminId, userId, dto, ct), "Subscription granted.");
    }

    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("{userId}/subscription/revoke")]
    public async Task<ActionResult<ApiResponse>> RevokeSubscription(
        string userId,
        [FromBody] RevokeSubscriptionDto dto,
        [FromServices] RevokeSubscriptionHandler handler,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(AdminId)) return Unauthorized();
        return HandleResult(await handler.HandleAsync(AdminId, userId, dto, ct), "Subscription revoked.");
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse>> GetUsers(
        [FromQuery] AdminUserFilterDto filter,
        [FromServices] ListUsersHandler handler,
        CancellationToken ct)
    {
        if (filter.Page < 1) filter.Page = 1;
        if (filter.PageSize is < 1 or > 100) filter.PageSize = 20;
        return HandleResult(await handler.HandleAsync(filter, ct));
    }

    [HttpGet("{userId}")]
    public async Task<ActionResult<ApiResponse>> GetUserDetail(
        string userId,
        [FromServices] GetUserDetailHandler handler,
        CancellationToken ct)
        => HandleResult(await handler.HandleAsync(userId, ct));
}
