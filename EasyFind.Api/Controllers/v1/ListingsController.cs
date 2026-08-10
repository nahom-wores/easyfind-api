using Asp.Versioning;
using EasyFind.Api.Features.Listings.Queries.GetListingById;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Services.IServices;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[ApiVersion("1.0")]
[Authorize]
public class ListingsController(GetListingByIdHandler getListingByIdHandler) : ApiControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> GetDetail(Guid id, CancellationToken ct)
    {
        if (getListingByIdHandler is null)
        {
            throw new ArgumentNullException(nameof(getListingByIdHandler));
        }
        var result = await getListingByIdHandler.FetchAsync(id, UserId, ct);
        return HandleResult(result);
    }
}