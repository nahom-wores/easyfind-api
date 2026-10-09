using EasyFind.Api.Data;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.UserDto;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Users.Commands;

public sealed record AssignRoleCommand(AssignRoleDto Assignment);

// SuperAdmin grants a role (the endpoint is SuperAdminAccess: this grants
// whatever it's asked for, so an Admin could otherwise promote themselves).
// Idempotent: re-assigning an existing role succeeds.
//
// The role reaches the user's token only on their next sign-in or refresh.
// Nothing validates the security stamp on a request, so stamping it here does
// not revoke tokens already issued.
public class AssignRoleHandler(UserManager<ApplicationUser> userManager)
{
    public async Task<Result> HandleAsync(AssignRoleCommand command, CancellationToken ct = default)
    {
        var dto = command.Assignment;
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
