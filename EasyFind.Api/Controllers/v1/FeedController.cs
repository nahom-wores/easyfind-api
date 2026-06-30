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
public class FeedController(IFeedService feedService) : ApiControllerBase
{
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
    public async Task<ActionResult<ApiResponse>> GetFeed([FromQuery] FeedRequestDto request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();
        if (request.Page < 1) request.Page = 1;
        if (request.PageSize is < 1 or > 50) request.PageSize = 20;

        var result = await feedService.GetPersonalizedFeedAsync(UserId, request, ct);
        return Ok(new ApiResponse { IsSuccess = true, Result = result });
    }
}