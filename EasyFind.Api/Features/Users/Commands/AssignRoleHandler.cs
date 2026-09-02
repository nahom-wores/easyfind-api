using EasyFind.Api.Data;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.UserDto;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Users.Commands;

// Admin grants a role. Idempotent: re-assigning an existing role succeeds.
// Stamps the security stamp so existing tokens pick the change up.
public class AssignRoleHandler(UserManager<ApplicationUser> userManager)
{
    public async Task<Result> HandleAsync(AssignRoleDto dto, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(dto.UserId);
        if (user == null)
            return Result.NotFound("User not found.");

        if (await userManager.IsInRoleAsync(user, dto.Role))
            return Result.Success();   // idempotent — already has the role

        var result = await userManager.AddToRoleAsync(user, dto.Role);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return Result.Validation(errors);
        }

        await userManager.UpdateSecurityStampAsync(user);
        return Result.Success();
    }
}
