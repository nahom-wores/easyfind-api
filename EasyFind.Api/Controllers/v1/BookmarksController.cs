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
public class BookmarksController(IBookmarkService bookmarkService) : ApiControllerBase
{
    
    [HttpGet]
    public async Task<ActionResult<ApiResponse>> GetMyBookmarks(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 20;

        var bookmarks = await bookmarkService.GetUserBookmarksAsync(UserId, page, pageSize, ct);
       
        return Ok(new ApiResponse { IsSuccess = true, Result = bookmarks });
    }
    
    [HttpPost("{listingId:guid}")]
    public async Task<ActionResult<ApiResponse>> Add(Guid listingId, CancellationToken ct)
    {
        var result = await bookmarkService.AddAsync(UserId, listingId, ct);
        return HandleResult(result);
    }
    [HttpDelete("{listingId:guid}")]
    public async Task<ActionResult<ApiResponse>> Remove(Guid listingId, CancellationToken ct)
    {
        var result = await bookmarkService.RemoveAsync(UserId, listingId, ct);
        return HandleResult(result);
    }
}