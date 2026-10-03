using Asp.Versioning;
using EasyFind.Api.Features.Subscriptions.Commands;
using EasyFind.Api.Features.Subscriptions.Queries;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[ApiVersion("1.0")]
[Authorize]
public class SubscriptionsController : ApiControllerBase
{
    // Returns a Chapa checkout URL for the client to open.
    [HttpPost("initiate")]
    public async Task<ActionResult<ApiResponse>> Initiate(
        [FromBody] InitiateSubscriptionDto dto,
        [FromServices] InitiateSubscriptionHandler handler,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();
        return HandleResult(await handler.HandleAsync(new InitiateSubscriptionCommand(UserId, dto.Tier), ct));
    }

    [HttpGet("me")]
    public async Task<ActionResult<ApiResponse>> GetMyStatus(
        [FromServices] GetMySubscriptionStatusHandler handler,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();
        return HandleResult(await handler.HandleAsync(new GetMySubscriptionStatusQuery(UserId), ct));
    }
}
