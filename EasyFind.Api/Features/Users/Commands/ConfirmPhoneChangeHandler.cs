using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.UserDto;
using Microsoft.AspNetCore.Identity;

namespace EasyFind.Api.Features.Users.Commands;

public sealed record ConfirmPhoneChangeCommand(string UserId, ConfirmPhoneChangeDto Confirmation);

// Step 2: verify the OTP and move the account onto the new number.
//
// UserName and PhoneNumber MUST move together. Sign-in reads them from
// different columns — RequestOtpHandler finds the account by UserName,
// VerifyOtpHandler by PhoneNumber — and RequestOtpHandler creates an account
// when its lookup misses. Leave the two out of step and the user's next sign-in
// silently registers a duplicate account, stranding their subscription on the
// old one. Both are therefore set on the tracked entity and persisted by a
// single UpdateAsync: it either all lands or none of it does.
public class ConfirmPhoneChangeHandler(
    UserManager<ApplicationUser> userManager,
    ILogger<ConfirmPhoneChangeHandler> logger)
{
    public async Task<Result<UserProfileDto>> HandleAsync(ConfirmPhoneChangeCommand command, CancellationToken ct = default)
    {
        var (userId, dto) = command;
        var newPhone = PhoneNumberRules.Normalize(dto.PhoneNumber);
        if (string.IsNullOrWhiteSpace(newPhone) || string.IsNullOrWhiteSpace(dto.OTP))
            return Result<UserProfileDto>.Validation("Phone number and code are both required.");

        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return Result<UserProfileDto>.NotFound("User not found.");

        // Verify only — this does not mutate anything. Identity's own
        // ChangePhoneNumberAsync is deliberately not used: it saves PhoneNumber
        // on its own, which would leave UserName behind in a second write.
        var validOtp = await userManager.VerifyChangePhoneNumberTokenAsync(user, dto.OTP, newPhone);
        if (!validOtp)
            return Result<UserProfileDto>.Validation("Invalid or expired code.");

        // Re-check: the number may have been claimed since the code was sent.
        if (await PhoneNumberRules.IsTakenByAnotherUserAsync(userManager, newPhone, user.Id, ct))
            return Result<UserProfileDto>.Conflict("That phone number is already in use.");

        var oldPhone = user.PhoneNumber;

        user.PhoneNumber = newPhone;
        user.UserName = newPhone;               // normalised by UpdateAsync
        user.PhoneNumberConfirmed = true;       // they just proved they hold it
        user.SecurityStamp = Guid.NewGuid().ToString();

        // One write: normalises UserName, re-runs Identity's duplicate-username
        // validation (the unique index is the last line of defence), and saves.
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            logger.LogError("Phone change failed for {UserId}: {Errors}", userId, errors);
            return Result<UserProfileDto>.Validation(errors);
        }

        logger.LogInformation("User {UserId} changed phone number from {Old} to {New}",
            userId, oldPhone, newPhone);

        // The caller's existing JWT stays valid — it identifies the user by id,
        // not by phone number — so there is no forced re-login. Their NEXT
        // sign-in uses the new number.
        var roles = await userManager.GetRolesAsync(user);
        return Result<UserProfileDto>.Success(user.ToProfileDto(roles));
    }
}
