using EasyFind.Api.Data;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.UserDto;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Users.Queries;

// The signed-in user's account record (identity + roles), behind /auth/me.
// Their job-seeker preferences live in the Profile feature instead.
public class GetCurrentUserHandler(UserManager<ApplicationUser> userManager)
{
    public async Task<Result<UserProfileDto>> HandleAsync(string userId, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
            return Result<UserProfileDto>.NotFound("User not found.");

        var roles = await userManager.GetRolesAsync(user);
        return Result<UserProfileDto>.Success(user.ToProfileDto(roles));
    }
}
