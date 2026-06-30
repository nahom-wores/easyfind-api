using Asp.Versioning;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;

[Route("api/v{version:apiVersion}/admin/listings")]
[ApiController]
[ApiVersion("1.0")]
//[Authorize(Roles = "Admin")]
public class AdminListingsController(IListingAdminService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse>> GetAll([FromQuery] AdminListingFilterDto filter, CancellationToken ct)
    {
        if (filter.Page < 1) filter.Page = 1;
        if (filter.PageSize is < 1 or > 100) filter.PageSize = 20;
        var result = await service.GetAllAsync(filter, ct);
        return Ok(new ApiResponse { IsSuccess = true, Result = result });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> GetById(Guid id, CancellationToken ct)
        => HandleResult(await service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<ApiResponse>> Create([FromBody] CreateListingDto dto, CancellationToken ct)
        => HandleResult(await service.CreateAsync(dto, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> Update(Guid id, [FromBody] UpdateListingDto dto, CancellationToken ct)
        => HandleResult(await service.UpdateAsync(id, dto, ct));

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id, CancellationToken ct)
        => HandleResult(await service.SoftDeleteAsync(id, ct), "Listing deleted.");

    [HttpPost("{id:guid}/restore")]
    public async Task<ActionResult<ApiResponse>> Restore(Guid id, CancellationToken ct)
        => HandleResult(await service.RestoreAsync(id, ct), "Listing restored.");

    [HttpPatch("{id:guid}/active")]
    public async Task<ActionResult<ApiResponse>> SetActive(Guid id, [FromQuery] bool isActive, CancellationToken ct)
        => HandleResult(await service.SetActiveAsync(id, isActive, ct), "Status updated.");
}