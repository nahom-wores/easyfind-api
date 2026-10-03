using Asp.Versioning;
using EasyFind.Api.Features.Listings.Commands;
using EasyFind.Api.Features.Listings.Queries;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;

// Admin listing management.
//
// Handlers arrive per-action via [FromServices] rather than through the
// constructor: each endpoint's signature then states exactly what it uses, and
// adding an endpoint never touches shared state. The controller's whole job is
// to normalise input and hand the Result to HandleResult.
[Route("api/v{version:apiVersion}/admin/listings")]
[ApiController]
[ApiVersion("1.0")]
[Authorize(Roles = "Admin")]
public class AdminListingsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse>> GetAll(
        [FromQuery] AdminListingFilterDto filter,
        [FromServices] ListAdminListingsHandler handler,
        CancellationToken ct)
    {
        if (filter.Page < 1) filter.Page = 1;
        if (filter.PageSize is < 1 or > 100) filter.PageSize = 20;

        return HandleResult(await handler.HandleAsync(new ListAdminListingsQuery(filter), ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> GetById(
        Guid id,
        [FromServices] GetAdminListingHandler handler,
        CancellationToken ct)
        => HandleResult(await handler.HandleAsync(new GetAdminListingQuery(id), ct));

    [HttpPost]
    public async Task<ActionResult<ApiResponse>> Create(
        [FromBody] CreateListingDto dto,
        [FromServices] CreateListingHandler handler,
        CancellationToken ct)
        => HandleResult(await handler.HandleAsync(new CreateListingCommand(dto), ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> Update(
        Guid id,
        [FromBody] UpdateListingDto dto,
        [FromServices] UpdateListingHandler handler,
        CancellationToken ct)
        => HandleResult(await handler.HandleAsync(new UpdateListingCommand(id, dto), ct));

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> Delete(
        Guid id,
        [FromServices] DeleteListingHandler handler,
        CancellationToken ct)
        => HandleResult(await handler.HandleAsync(new DeleteListingCommand(id), ct), "Listing deleted.");

    [HttpPost("{id:guid}/restore")]
    public async Task<ActionResult<ApiResponse>> Restore(
        Guid id,
        [FromServices] RestoreListingHandler handler,
        CancellationToken ct)
        => HandleResult(await handler.HandleAsync(new RestoreListingCommand(id), ct), "Listing restored.");

    [HttpPatch("{id:guid}/active")]
    public async Task<ActionResult<ApiResponse>> SetActive(
        Guid id,
        [FromQuery] bool isActive,
        [FromServices] SetListingActiveHandler handler,
        CancellationToken ct)
        => HandleResult(await handler.HandleAsync(new SetListingActiveCommand(id, isActive), ct), "Status updated.");

    [HttpPost("{id:guid}/image")]
    public async Task<ActionResult<ApiResponse>> UploadImage(
        Guid id,
        IFormFile file,
        [FromServices] UploadListingImageHandler handler,
        CancellationToken ct)
        => HandleResult(await handler.HandleAsync(new UploadListingImageCommand(id, file), ct));
}
