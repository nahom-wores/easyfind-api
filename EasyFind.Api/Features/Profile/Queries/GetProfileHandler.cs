using EasyFind.Api.Data;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Profile;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Profile.Queries;

public sealed record GetProfileQuery(string UserId);

// The signed-in user's own profile. "Not yet created" is distinct from "no such
// user" so the client knows to send them through onboarding.
public class GetProfileHandler(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager)
{
    public async Task<Result<ProfileResponseDto>> HandleAsync(GetProfileQuery query, CancellationToken ct = default)
    {
        var userId = query.UserId;
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return Result<ProfileResponseDto>.NotFound("User not found.");

        var profile = await db.UserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (profile is null)
            return Result<ProfileResponseDto>.NotFound("Profile not yet created.");

        return Result<ProfileResponseDto>.Success(profile.ToResponseDto(user));
    }
}
