using System.Security.Claims;
using Asp.Versioning;
using EasyFind.Api.Models.Admin;
using EasyFind.Api.Models.Dto.Admin;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;

[Route("api/v{version:apiVersion}/admin/users")]
[ApiController]
[ApiVersion("1.0")]
[Authorize(Roles = "Admin")]
public class AdminUsersController(IAdminSubscriptionService adminSubService, IAdminUserService adminUserService) : ApiControllerBase
{
    private string? AdminId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("{userId}/subscription/grant")]
    public async Task<ActionResult<ApiResponse>> GrantSubscription(
        string userId, [FromBody] GrantSubscriptionDto dto, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(AdminId)) return Unauthorized();
        var result = await adminSubService.GrantAsync(AdminId, userId, dto, ct);
        return HandleResult(result, "Subscription granted.");
    }
    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("{userId}/subscription/revoke")]
    public async Task<ActionResult<ApiResponse>> RevokeSubscription(
        string userId, [FromBody] RevokeSubscriptionDto dto, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(AdminId)) return Unauthorized();
        var result = await adminSubService.RevokeAsync(AdminId, userId, dto, ct);
        return HandleResult(result, "Subscription revoked.");
    }
    
    [HttpGet]
    public async Task<ActionResult<ApiResponse>> GetUsers([FromQuery] AdminUserFilterDto filter, CancellationToken ct)
    {
        if (filter.Page < 1) filter.Page = 1;
        if (filter.PageSize is < 1 or > 100) filter.PageSize = 20;
        var result = await adminUserService.GetUsersAsync(filter, ct);
        return Ok(new ApiResponse { IsSuccess = true, Result = result });
    }

    [HttpGet("{userId}")]
    public async Task<ActionResult<ApiResponse>> GetUserDetail(string userId, CancellationToken ct)
        => HandleResult(await adminUserService.GetUserDetailAsync(userId, ct));
}