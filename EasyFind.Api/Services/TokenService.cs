

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EasyFind.Api.Data;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.UserDto;
using EasyFind.Api.Services.IServices;

namespace EasyFind.Api.Services
{
    public class TokenService : ITokenService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _db;
        private readonly string _secretKey;

        public TokenService(UserManager<ApplicationUser> userManager,
            IConfiguration configuration,
            ApplicationDbContext db)
        {
            this._userManager = userManager;
            this._db = db;
            _secretKey = configuration.GetValue<string>("JwtConfig:Secret");
        }
        public async Task<string> CreateNewRefreshToken(string userId, string tokenId)
        {
            RefreshToken refreshToken = new()
            {
                IsValid = true,
                UserId = userId,
                JwtTokenId = tokenId,
                ExpireDate = DateTimeOffset.UtcNow.AddDays(30),
                Refresh_Token = Guid.NewGuid() + "-" + Guid.NewGuid(),
            };
            await _db.RefreshTokens.AddAsync(refreshToken);
            await _db.SaveChangesAsync();
            return refreshToken.Refresh_Token;
        }

        public async Task<string> GenerteAccessToken(ApplicationUser user, string tokenId)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var key = Encoding.ASCII.GetBytes(_secretKey);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(JwtRegisteredClaimNames.Jti, tokenId),
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            };

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                //Expires = DateTime.UtcNow.AddMinutes(15),
                Expires = DateTime.UtcNow.AddDays(15), // change to minute on production
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public async Task<TokenDto> RefreshAccessToken(TokenDto tokenDto)
        {
            // Find an existing refresh token
            var existingRefreshToken = await _db.RefreshTokens.FirstOrDefaultAsync(x => x.Refresh_Token == tokenDto.RefreshToken);
            if (existingRefreshToken == null)
            {
                return new TokenDto();
            }
            // Instead of failing immediately if the AccessToken is missing/malformed, 
            // we only check it if it's actually provided. 
            // The RefreshToken itself is the primary proof of identity here.
            if (!string.IsNullOrEmpty(tokenDto.AccessToken))
            {
                var isTokenValid = GetAccessTokenData(tokenDto.AccessToken, existingRefreshToken.UserId, existingRefreshToken.JwtTokenId);
                if (!isTokenValid)
                {
                    // If the access token is provided but belongs to a different user/chain, it's fraud
                    await MarkTokenAsInvalid(existingRefreshToken);
                    return new TokenDto();
                }
            }

            // When someone tries to use invalid refresh token, fraud possible
            // If just expired then mark as invalid and return empty
            if (!existingRefreshToken.IsValid || existingRefreshToken.ExpireDate < DateTimeOffset.UtcNow)
            {
                // If someone uses an invalid token, revoke the whole chain for safety
                await MarkAllTokenInChainAsInvalid(existingRefreshToken.UserId, existingRefreshToken.JwtTokenId);
                return new TokenDto();
            }
          

            // replace old refresh with a new one with updated expire date
            var newRefreshToken = await CreateNewRefreshToken(existingRefreshToken.UserId, existingRefreshToken.JwtTokenId);
            await MarkTokenAsInvalid(existingRefreshToken); // revoke old one

            // Generate new access token

            var applicationUser = await _db.ApplicationUsers
                .FirstOrDefaultAsync(x => x.Id == existingRefreshToken.UserId);
            if (applicationUser == null)
            {
                return new TokenDto();
            }

            var newAccessToken = await GenerteAccessToken(applicationUser, existingRefreshToken.JwtTokenId);

            return new TokenDto()
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken,
                IsSuccess = true
            };
        }

        public async Task RevokeRefreshToken(TokenDto tokenDto)
        {
            var existingRefreshToken = await _db.RefreshTokens.FirstOrDefaultAsync(x => x.Refresh_Token == tokenDto.RefreshToken);
            if (existingRefreshToken == null)
            {
                return;
            }
            // Compare data from existing refresh and access token provided and 
            // if there is any mismatch then we should do nothing with refresh token
            var isTokenValid = GetAccessTokenData(tokenDto.AccessToken, existingRefreshToken.UserId, existingRefreshToken.JwtTokenId);
            if (!isTokenValid)
            {
                return;
            }

            await MarkAllTokenInChainAsInvalid(existingRefreshToken.UserId, existingRefreshToken.JwtTokenId);

        }

        private bool GetAccessTokenData(string accessToken, string expectedUserId, string expectedTokenId)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwt = tokenHandler.ReadJwtToken(accessToken);
                var jwtTokenId = jwt.Claims.FirstOrDefault(x => x.Type == JwtRegisteredClaimNames.Jti).Value;
                var userId = jwt.Claims.FirstOrDefault(x => x.Type == JwtRegisteredClaimNames.Sub).Value;
                return userId == expectedUserId && jwtTokenId == expectedTokenId;
            }
            catch
            {

                return false;
            }
        }
        private async Task MarkAllTokenInChainAsInvalid(string userId, string tokenId)
        {
            var chainRecords = await _db.RefreshTokens
                   .Where(x => x.UserId == userId
                   && x.JwtTokenId == tokenId)
                   .ExecuteUpdateAsync(x => x.SetProperty(refreshToken => refreshToken.IsValid, false)); // .net 8 new feature bulk update foreach() update           
        }
        private async Task MarkTokenAsInvalid(RefreshToken refreshToken)
        {
            refreshToken.IsValid = false;
            await _db.SaveChangesAsync();
        }
    }
}
