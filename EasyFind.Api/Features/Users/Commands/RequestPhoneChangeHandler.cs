using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.UserDto;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Identity;

namespace EasyFind.Api.Features.Users.Commands;

// Step 1 of changing the login phone number: send an OTP to the NEW number.
//
// Sending to the new number (not the current one) is the point — receiving the
// code is what proves the user actually controls it.
//
// Nothing is written here. The OTP is derived from (user, new number, purpose)
// by Identity, so there is no pending-change row to store, expire or clean up;
// ConfirmPhoneChangeHandler just needs the same number sent back to it.
public class RequestPhoneChangeHandler(
    UserManager<ApplicationUser> userManager,
    ISmsService smsService,
    ILogger<RequestPhoneChangeHandler> logger)
{
    public async Task<Result> HandleAsync(
        string userId, UpdateUserPhoneNumberDto dto, CancellationToken ct = default)
    {
        var newPhone = PhoneNumberRules.Normalize(dto.PhoneNumber);
        if (string.IsNullOrWhiteSpace(newPhone))
            return Result.Validation("A phone number is required.");

        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return Result.NotFound("User not found.");

        if (string.Equals(newPhone, user.PhoneNumber, StringComparison.Ordinal))
            return Result.Validation("That is already your phone number.");

        // Checked here for a friendly early answer; checked again at confirm
        // time, because the number could be claimed in between.
        if (await PhoneNumberRules.IsTakenByAnotherUserAsync(userManager, newPhone, user.Id, ct))
            return Result.Conflict("That phone number is already in use.");

        // Token is bound to this user AND this number, so a code issued for one
        // number cannot be replayed to confirm a different one.
        var otp = await userManager.GenerateChangePhoneNumberTokenAsync(user, newPhone);

        var sent = await smsService.SendOTPAsync(newPhone, otp);
        if (!sent)
        {
            logger.LogError("Failed to send phone-change OTP for user {UserId}", userId);
            return Result.Failure("Could not send the verification code. Please try again.",
                ErrorType.Failure);
        }

        return Result.Success();
    }
}
