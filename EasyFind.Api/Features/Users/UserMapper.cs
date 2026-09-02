using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.UserDto;

namespace EasyFind.Api.Features.Users;

public static class UserMapper
{
    // The account record as every /auth/me endpoint returns it. Roles are passed
    // in because fetching them is a separate UserManager call the caller has
    // usually already made.
    public static UserProfileDto ToProfileDto(this ApplicationUser u, IEnumerable<string> roles) => new()
    {
        FirstName = u.FirstName,
        LastName = u.LastName,
        Email = u.Email,
        PhoneNumber = u.PhoneNumber,
        ProfilePictureUrl = u.ProfilePictureUrl,
        IsVerified = u.PhoneNumberConfirmed || u.EmailConfirmed,
        CreatedAt = u.CreatedAt,
        Roles = roles.ToList()
    };
}
