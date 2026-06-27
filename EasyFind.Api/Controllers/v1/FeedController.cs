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
public class FeedController(IFeedService feedService) : ControllerBase
{
    /// <summary>
    /// Personalized, ranked feed of jobs & scholarships for the current user.
    /// GET /api/v1/feed
    /// </summary>
    ///

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse>> GetFeed([FromQuery]
        FeedRequestDto request, CancellationToken ct)
    {
        var response = new ApiResponse();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            response.IsSuccess = false;
            response.StatusCode = HttpStatusCode.Unauthorized;
            response.ErrorMessage.Add("User not authenticated");
            return Unauthorized(response);
        }
        // Guardrails on paging — never trust client input blindly
        if (request.Page < 1) request.Page = 1;
        if (request.PageSize is < 1 or > 50) request.PageSize = 20;
        var result = await feedService.GetPersonalizedFeedAsync(userId, request, ct);

        response.IsSuccess = true;
        response.StatusCode = HttpStatusCode.OK;
        response.Result = result;
        return Ok(response);
    }
}