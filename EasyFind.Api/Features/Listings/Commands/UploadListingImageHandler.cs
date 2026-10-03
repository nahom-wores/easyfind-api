using EasyFind.Api.Data;
using EasyFind.Api.Extensions;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Services;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Listings.Commands;

public sealed record UploadListingImageCommand(Guid ListingId, IFormFile File);

// Admin attaches (or replaces) a listing's image.
//
// Order matters here: validate cheaply before touching S3, and only overwrite
// the stored URL once the upload has actually succeeded.
public class UploadListingImageHandler(
    ApplicationDbContext db,
    ListingAuthorizationService listings,
    IStorageService storage,
    IRedisCacheService cache)
{
    public async Task<Result<ListingImageDto>> HandleAsync(UploadListingImageCommand command, CancellationToken ct = default)
    {
        var (id, file) = command;
        // 1. Size / content-type check
        var (ok, error) = ImageValidator.Validate(file);
        if (!ok)
            return Result<ListingImageDto>.Validation(error!);

        // 2. Magic-byte check — a .png extension proves nothing
        await using var stream = file.OpenReadStream();
        if (!ImageValidator.HasValidImageSignature(stream))
            return Result<ListingImageDto>.Validation("File is not a valid image.");

        // 3. The listing must exist and be one this admin may manage
        var listing = await listings.ManageableListings()
            .FirstOrDefaultAsync(l => l.Id == id, ct);
        if (listing is null)
            return Result<ListingImageDto>.NotFound("Listing not found.");

        // 4. Drop the previous image so replacing doesn't orphan files in S3
        if (!string.IsNullOrEmpty(listing.ImageUrl))
            await storage.DeletePublicImageAsync(listing.ImageUrl, ct);

        // 5. Upload, then record the URL
        var url = await storage.UploadPublicImageAsync(stream, file.FileName, file.ContentType, ct);

        listing.ImageUrl = url;
        listing.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        // The feed carries ImageUrl, so cached pages are now stale.
        await cache.InvalidateFeedsAsync();

        return Result<ListingImageDto>.Success(new ListingImageDto { ImageUrl = url });
    }
}
