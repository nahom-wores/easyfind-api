using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.UserDto;
using EasyFind.Api.Services;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Identity;

namespace EasyFind.Api.Features.Users.Commands;

public sealed record UpdateProfilePictureCommand(string UserId, IFormFile Image);

// Replaces the signed-in user's avatar.
//
// Order matters: validate before uploading, and only delete the old image once
// the new one is stored AND the user row points at it. Deleting first would
// leave the user with no picture if the upload failed.
public class UpdateProfilePictureHandler(
    UserManager<ApplicationUser> userManager,
    IImageService imageService)
{
    public async Task<Result<ProfilePictureDto>> HandleAsync(UpdateProfilePictureCommand command, CancellationToken ct = default)
    {
        var (userId, image) = command;
        // 1. Size / content-type, then magic bytes — the same checks the listing
        //    image endpoint applies. A .png extension proves nothing.
        var (ok, error) = ImageValidator.Validate(image);
        if (!ok)
            return Result<ProfilePictureDto>.Validation(error!);

        await using (var checkStream = image.OpenReadStream())
        {
            if (!ImageValidator.HasValidImageSignature(checkStream))
                return Result<ProfilePictureDto>.Validation("File is not a valid image.");
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return Result<ProfilePictureDto>.NotFound("User not found.");

        // 2. Keep the old URL so it can be cleaned up after a successful swap
        var oldImageUrl = user.ProfilePictureUrl;

        var newImageUrl = await imageService.UploadImageAsync(image, $"users/{userId}");
        if (string.IsNullOrEmpty(newImageUrl))
            return Result<ProfilePictureDto>.Failure("Image upload failed. Please try again.", ErrorType.Failure);

        // 3. Point the user at the new image
        user.ProfilePictureUrl = newImageUrl;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return Result<ProfilePictureDto>.Validation(errors);
        }

        // 4. Only now is the old image safe to remove. A failure here leaves an
        //    orphaned file, which is better than losing the live one.
        if (!string.IsNullOrEmpty(oldImageUrl))
            await imageService.DeleteImageAsync(oldImageUrl);

        return Result<ProfilePictureDto>.Success(new ProfilePictureDto
        {
            ProfilePictureUrl = newImageUrl
        });
    }
}
