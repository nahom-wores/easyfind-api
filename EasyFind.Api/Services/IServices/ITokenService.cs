

using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.UserDto;

namespace EasyFind.Api.Services.IServices
{
    public interface ITokenService
    {
        Task<string> GenerteAccessToken(ApplicationUser user, string tokenId);
        Task<TokenDto> RefreshAccessToken(TokenDto tokenDto);
        Task RevokeRefreshToken(TokenDto tokenDto);
        Task<string> CreateNewRefreshToken(string userId, string tokenId);


    }
}
