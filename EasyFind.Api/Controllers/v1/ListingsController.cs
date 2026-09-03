using Asp.Versioning;
using EasyFind.Api.Features.Listings.Queries;
using EasyFind.Api.Models.Dto.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;

// Consumer-facing listing reads.
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[ApiVersion("1.0")]
[Authorize]
public class ListingsController : ApiControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> GetDetail(
        Guid id,
        [FromServices] GetListingDetailHandler handler,
        CancellationToken ct)
        => HandleResult(await handler.HandleAsync(new GetListingDetailQuery(id, UserId), ct));
}
