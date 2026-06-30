using Asp.Versioning;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Subscriptions;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[ApiVersion("1.0")]
[Authorize]
public class SubscriptionsController(ISubscriptionService subscriptionService) : ApiControllerBase
{
   
    
    [HttpPost("initiate")]
    public async Task<ActionResult<ApiResponse>> Initiate([FromBody] InitiateSubscriptionDto dto, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();
        var result = await subscriptionService.InitiateAsync(UserId, dto.Tier, ct);
        return HandleResult(result);
    }
    
    [HttpGet("me")]
    public async Task<ActionResult<ApiResponse>> GetMyStatus(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();
        var result = await subscriptionService.GetMyStatusAsync(UserId, ct);
        return HandleResult(result);
    }
}