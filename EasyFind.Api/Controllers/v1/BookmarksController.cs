using Asp.Versioning;
using EasyFind.Api.Features.Bookmarks.Commands;
using EasyFind.Api.Features.Bookmarks.Queries;
using EasyFind.Api.Models.Dto.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[ApiVersion("1.0")]
[Authorize]
public class BookmarksController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse>> GetMyBookmarks(
        [FromServices] GetUserBookmarksHandler handler,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 20;

        return HandleResult(await handler.HandleAsync(UserId, page, pageSize, ct));
    }

    [HttpPost("{listingId:guid}")]
    public async Task<ActionResult<ApiResponse>> Add(
        Guid listingId,
        [FromServices] AddBookmarkHandler handler,
        CancellationToken ct)
        => HandleResult(await handler.HandleAsync(UserId, listingId, ct));

    [HttpDelete("{listingId:guid}")]
    public async Task<ActionResult<ApiResponse>> Remove(
        Guid listingId,
        [FromServices] RemoveBookmarkHandler handler,
        CancellationToken ct)
        => HandleResult(await handler.HandleAsync(UserId, listingId, ct));
}
