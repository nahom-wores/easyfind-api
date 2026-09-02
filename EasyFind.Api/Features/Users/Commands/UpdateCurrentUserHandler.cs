using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.UserDto;
using Microsoft.AspNetCore.Identity;

namespace EasyFind.Api.Features.Users.Commands;

// Edits the signed-in user's account record — the counterpart to
// GetCurrentUserHandler behind PUT /auth/me.
//
// This is the identity record (name, contact). Their job-seeker preferences are
// a different thing entirely and live in the Profile feature.
public class UpdateCurrentUserHandler(UserManager<ApplicationUser> userManager)
{
    public async Task<Result<UserProfileDto>> HandleAsync(
        string userId, UpdateUserProfileDto dto, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return Result<UserProfileDto>.NotFound("User not found.");

        user.FirstName = dto.FirstName;
        user.LastName = dto.LastName;

        // PhoneNumber is deliberately NOT updated here, even though the DTO
        // carries one. The phone number is the login identifier: RequestOtpHandler
        // looks the account up by UserName while VerifyOtpHandler looks it up by
        // PhoneNumber. Changing one without the other splits the account in two —
        // the next request-otp wouldn't find the user and would create a second
        // one. A phone change needs its own flow that moves UserName as well and
        // re-verifies the new number by OTP.
        //
        // Email is likewise left alone: it is passed to Chapa at checkout, and
        // this endpoint does no ownership verification.

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return Result<UserProfileDto>.Validation(errors);
        }

        // Same shape GET /auth/me returns, so a client can swap one response for
        // the other without special-casing.
        var roles = await userManager.GetRolesAsync(user);
        return Result<UserProfileDto>.Success(user.ToProfileDto(roles));
    }
}
