

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
        // An access token cannot be revoked — nothing checks it against the
        // database on the way in, so a ban, a role change or a logout only takes
        // effect when the current one expires. That expiry is therefore the
        // revocation window, and it has to stay short. The refresh chain below
        // is what keeps sessions long-lived.
        private const int DefaultAccessTokenMinutes = 60;

        // Refuse to issue a token that outlives a working day. Set
        // JwtConfig:AccessTokenMinutes deliberately; do not raise it to paper
        // over a client that has not implemented refresh.
        private const int MaxAccessTokenMinutes = 8 * 60;

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<TokenService> _logger;
        private readonly string _secretKey;
        private readonly TimeSpan _accessTokenLifetime;

        public TokenService(UserManager<ApplicationUser> userManager,
            IConfiguration configuration,
            ApplicationDbContext db,
            ILogger<TokenService> logger)
        {
            this._userManager = userManager;
            this._db = db;
            this._logger = logger;
            _secretKey = configuration.GetValue<string>("JwtConfig:Secret");

            var minutes = configuration.GetValue("JwtConfig:AccessTokenMinutes", DefaultAccessTokenMinutes);
            if (minutes <= 0) minutes = DefaultAccessTokenMinutes;
            if (minutes > MaxAccessTokenMinutes) minutes = MaxAccessTokenMinutes;
            _accessTokenLifetime = TimeSpan.FromMinutes(minutes);
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
                // Short by design — see _accessTokenLifetime. The client is
                // expected to hit /auth/refresh-token on a 401; RefreshAccessToken
                // reads the expired token's claims rather than validating it, so
                // an expired access token still refreshes cleanly.
                Expires = DateTime.UtcNow.Add(_accessTokenLifetime),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        // Every failure returns IsSuccess = false and a message; AuthController
        // turns that into a 401. The caller must not have to infer failure from a
        // null AccessToken, which is what it used to require.
        //
        // The message is the SAME for every failure on purpose. "No such token",
        // "already used" and "belongs to someone else" are different facts about
        // what an attacker just guessed, and the client has one action for all of
        // them anyway: sign in again. The specifics go to the log.
        private const string RefreshFailedMessage =
            "Invalid or expired refresh token. Please sign in again.";

        public async Task<TokenDto> RefreshAccessToken(TokenDto tokenDto)
        {
            // Find an existing refresh token
            var existingRefreshToken = await _db.RefreshTokens.FirstOrDefaultAsync(x => x.Refresh_Token == tokenDto.RefreshToken);
            if (existingRefreshToken == null)
            {
                _logger.LogInformation("Refresh attempted with an unrecognised refresh token.");
                return Failed();
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

                    _logger.LogWarning(
                        "Refresh for user {UserId} sent an access token from a different chain; token invalidated.",
                        existingRefreshToken.UserId);

                    return Failed();
                }
            }

            // When someone tries to use invalid refresh token, fraud possible
            // If just expired then mark as invalid and return empty
            if (!existingRefreshToken.IsValid || existingRefreshToken.ExpireDate < DateTimeOffset.UtcNow)
            {
                // If someone uses an invalid token, revoke the whole chain for safety
                await MarkAllTokenInChainAsInvalid(existingRefreshToken.UserId, existingRefreshToken.JwtTokenId);

                // Logged at Warning because this is the one that signs a user out
                // completely, and the usual cause is not an attacker but a client
                // refreshing twice concurrently — the second request presents the
                // token the first just consumed. Without this line that support
                // ticket has nothing behind it.
                _logger.LogWarning(
                    "Refresh token for user {UserId} was already used or expired; whole chain {ChainId} revoked.",
                    existingRefreshToken.UserId, existingRefreshToken.JwtTokenId);

                return Failed();
            }


            // replace old refresh with a new one with updated expire date
            var newRefreshToken = await CreateNewRefreshToken(existingRefreshToken.UserId, existingRefreshToken.JwtTokenId);
            await MarkTokenAsInvalid(existingRefreshToken); // revoke old one

            // Generate new access token

            var applicationUser = await _db.ApplicationUsers
                .FirstOrDefaultAsync(x => x.Id == existingRefreshToken.UserId);
            if (applicationUser == null)
            {
                _logger.LogWarning("Refresh token {ChainId} points at a user that no longer exists.",
                    existingRefreshToken.JwtTokenId);
                return Failed();
            }

            var newAccessToken = await GenerteAccessToken(applicationUser, existingRefreshToken.JwtTokenId);

            return new TokenDto()
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken,
                IsSuccess = true
            };
        }

        private static TokenDto Failed() => new()
        {
            IsSuccess = false,
            Message = RefreshFailedMessage
        };

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
