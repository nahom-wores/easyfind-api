using System.Net;
using System.Security.Claims;
using Asp.Versioning;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[ApiVersion("1.0")]
[Authorize]
public class BookmarksController(IBookmarkService bookmarkService) : ControllerBase
{
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    
    [HttpGet]
    public async Task<ActionResult<ApiResponse>> GetMyBookmarks(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var response = new ApiResponse();
        if (string.IsNullOrEmpty(UserId)) return Unauthorized(response);

        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 20;

        var result = await bookmarkService.GetUserBookmarksAsync(UserId, page, pageSize, ct);
        response.IsSuccess = true;
        response.StatusCode = HttpStatusCode.OK;
        response.Result = result;
        return Ok(response);
    }
    
    [HttpPost("{listingId:guid}")]
    public async Task<ActionResult<ApiResponse>> Add(Guid listingId, CancellationToken ct)
    {
        var response = new ApiResponse();
        if (string.IsNullOrEmpty(UserId)) return Unauthorized(response);

        var (ok, message) = await bookmarkService.AddAsync(UserId, listingId, ct);
        response.IsSuccess = ok;
        response.StatusCode = ok ? HttpStatusCode.OK : HttpStatusCode.BadRequest;
        response.Result = message;
        if (!ok) response.ErrorMessage.Add(message);
        return ok ? Ok(response) : BadRequest(response);
    }
    [HttpDelete("{listingId:guid}")]
    public async Task<ActionResult<ApiResponse>> Remove(Guid listingId, CancellationToken ct)
    {
        var response = new ApiResponse();
        if (string.IsNullOrEmpty(UserId)) return Unauthorized(response);

        var (ok, message) = await bookmarkService.RemoveAsync(UserId, listingId, ct);
        response.IsSuccess = ok;
        response.StatusCode = ok ? HttpStatusCode.OK : HttpStatusCode.NotFound;
        response.Result = message;
        if (!ok) response.ErrorMessage.Add(message);
        return ok ? Ok(response) : NotFound(response);
    }
}