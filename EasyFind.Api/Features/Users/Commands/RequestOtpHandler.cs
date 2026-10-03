using EasyFind.Api.Data;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.UserDto;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Users.Commands;

public sealed record RequestOtpCommand(LogInRequestDto Credentials);

// Step 1 of sign-in: send an OTP by SMS.
//
// Registration and login are the SAME endpoint — an unknown phone number gets a
// user created for it here, so the client never has to ask which it is.
//
// Returns LoginResponseDto (with its own IsSuccess/ResultMessage) rather than
// Result<T>: AuthController shapes the HTTP response from it, and the mobile
// client depends on that body. Do not 'modernise' without a client change.
public class RequestOtpHandler(
    UserManager<ApplicationUser> userManager,
    ISmsService smsService,
    ILogger<RequestOtpHandler> logger)
{
    public async Task<LoginResponseDto> HandleAsync(RequestOtpCommand command)
    {
        var dto = command.Credentials;
        // Find existing user, or create a new one
        var user = await userManager.FindByNameAsync(dto.PhoneNumber);

        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = dto.PhoneNumber,
                PhoneNumber = dto.PhoneNumber,
                TwoFactorEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            // Two simultaneous first-time requests for the same number both get
            // here, and one loses the race on the unique UserName index. Catch
            // that and adopt the account the winner created rather than failing
            // the caller — this is the sign-in path, and a duplicate request is
            // not the user's fault.
            IdentityResult result;
            try
            {
                result = await userManager.CreateAsync(user);
            }
            catch (DbUpdateException)
            {
                result = IdentityResult.Failed();
            }

            if (!result.Succeeded)
            {
                var existing = await userManager.FindByNameAsync(dto.PhoneNumber);
                if (existing is null)
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    logger.LogError("User creation failed: {Errors}", errors);
                    return new LoginResponseDto { IsSuccess = false, ResultMessage = errors };
                }

                user = existing;   // the concurrent request created it
            }
            else
            {
                await userManager.AddToRoleAsync(user, AppRoles.User);
            }
        }

        // From here, the path is identical for new AND returning users
        var otp = await userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultPhoneProvider);
        var sent = await smsService.SendOTPAsync(user.PhoneNumber, otp);

        if (!sent)
            return new LoginResponseDto
            {
                IsSuccess = false,
                PhoneNumber = user.UserName,
                ResultMessage = "Error sending OTP"
            };

        return new LoginResponseDto
        {
            IsSuccess = true,
            PhoneNumber = user.UserName,
            ResultMessage = "OTP sent successfully"
        };
    }
}
