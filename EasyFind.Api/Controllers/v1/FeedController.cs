using System.Security.Claims;
using Asp.Versioning;
using EasyFind.Api.Features.Listings.Queries;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[ApiVersion("1.0")]
[Authorize]
public class FeedController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse>> GetFeed(
        [FromQuery] FeedRequestDto request,
        [FromServices] GetFeedHandler handler,
        CancellationToken ct)
    {
        // [Authorize] should guarantee this, but the feed is meaningless without
        // a user, so fail with 401 rather than throwing.
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        // Clamp paging here so no handler has to defend against it.
        if (request.Page < 1) request.Page = 1;
        if (request.PageSize is < 1 or > 50) request.PageSize = 20;

        return HandleResult(await handler.HandleAsync(new GetFeedQuery(userId, request), ct));
    }
}
