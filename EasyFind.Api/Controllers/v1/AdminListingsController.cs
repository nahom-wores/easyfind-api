using Asp.Versioning;
using EasyFind.Api.Data;
using EasyFind.Api.Features.Listings.Commands.CreateListing;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Services;
using EasyFind.Api.Services.IServices;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Controllers.v1;

[Route("api/v{version:apiVersion}/admin/listings")]
[ApiController]
[ApiVersion("1.0")]
//[Authorize(Roles = "Admin")]
public class AdminListingsController(IListingAdminService service,
    IMediator mediator, IStorageService storage, ApplicationDbContext db) : ApiControllerBase
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
        => HandleResult(await mediator.Send(new CreateListingCommand{CreateListingDto = dto}, ct));
    
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
    
    [HttpPost("{id:guid}/image")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult<ApiResponse>> UploadImage(
        Guid id, IFormFile file, CancellationToken ct)
    {
        // 1. basic validation
        var (ok, error) = ImageValidator.Validate(file);
        if (!ok) return HandleResult(Result.Validation(error!));

        // 2. magic-byte validation
        await using var stream = file.OpenReadStream();
        if (!ImageValidator.HasValidImageSignature(stream))
            return HandleResult(Result.Validation("File is not a valid image."));

        // 3. find the listing
        var listing = await db.Listings.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (listing == null) return HandleResult(Result.NotFound("Listing not found."));

        // 4. delete the old image if replacing (avoid orphans in S3)
        if (!string.IsNullOrEmpty(listing.ImageUrl))
            await storage.DeletePublicImageAsync(listing.ImageUrl, ct);

        // 5. upload the new one
        var url = await storage.UploadPublicImageAsync(stream, file.FileName, file.ContentType, ct);

        // 6. save
        listing.ImageUrl = url;
        listing.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(new ApiResponse { IsSuccess = true, Result = new { imageUrl = url } });
    }
}