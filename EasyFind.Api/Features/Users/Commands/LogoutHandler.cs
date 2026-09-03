using EasyFind.Api.Data;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.UserDto;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Users.Commands;

public sealed record LogoutCommand(TokenDto Token);

// Revokes a refresh token, which invalidates the whole chain issued from it.
// Throws on a missing token — AuthController turns that into a 500, as before.
public class LogoutHandler(ITokenService tokenService)
{
    public async Task HandleAsync(LogoutCommand command)
    {
        var tokenDto = command.Token;
        if (string.IsNullOrWhiteSpace(tokenDto.RefreshToken))
        {
            throw new ArgumentException("Refresh token is required.");
        }
        // invalidates the entire chain
        await tokenService.RevokeRefreshToken(tokenDto);
    }
}
