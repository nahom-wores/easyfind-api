using EasyFind.Api.Data;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.UserDto;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Users.Commands;

// Step 2 of sign-in: check the OTP and issue tokens.
//
// Also confirms the phone number and back-fills the default User role for
// accounts created before roles were seeded.
//
// Returns TokenDto (its own IsSuccess/Message) for the same client-contract
// reason as RequestOtpHandler.
public class VerifyOtpHandler(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    ITokenService tokenService,
    ILogger<VerifyOtpHandler> logger)
{
    public async Task<TokenDto> HandleAsync(VerifyOTPRequestDto verifyOTPRequestDTO)
    {
        try
        {
            var user = await db.ApplicationUsers.FirstOrDefaultAsync(x =>
                x.PhoneNumber == verifyOTPRequestDTO.PhoneNumber);
            if (user == null)
            {
                return new TokenDto()
                {
                    AccessToken = string.Empty,
                    IsSuccess = false,
                    Message = "User Not Found!"
                };
            }

            // Validate OTP
            var isValidOTP = await userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultPhoneProvider,
                verifyOTPRequestDTO.OTP);
            if (!isValidOTP)
            {
                return new TokenDto()
                {
                    AccessToken = string.Empty,
                    IsSuccess = false,
                    Message = "Invalid OTP"
                };
            }

            // Generate Token
            var jwtTokenId = $"JIT{Guid.NewGuid()}";
            var accessToken = await tokenService.GenerteAccessToken(user, jwtTokenId);
            var refreshToken = await tokenService.CreateNewRefreshToken(user.Id, jwtTokenId);

            // Update User Status
            if (!await userManager.IsPhoneNumberConfirmedAsync(user))
            {
                user.PhoneNumberConfirmed = true;
                await userManager.UpdateAsync(user);
            }

            // Ensure user has the default role
            var role = await roleManager.FindByNameAsync(AppRoles.User);
            var existingUserRole = await db.UserRoles
                .FirstOrDefaultAsync(x => x.UserId == user.Id && x.RoleId == role.Id);
            if (existingUserRole == null && role != null)
            {
                var userRole = new IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id };
                await db.UserRoles.AddAsync(userRole);
                await db.SaveChangesAsync();
                await userManager.UpdateSecurityStampAsync(user);
            }
            
            //return success
            return new TokenDto
            {
                IsSuccess = true,
                Message = "Verification Successful",
                AccessToken = accessToken,
                RefreshToken = refreshToken
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error verifying OTP");
            return new TokenDto
            {
                IsSuccess = false,
                Message = "Verification failed"
            };
        }

    }
}
