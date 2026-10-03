using EasyFind.Api.Models.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Users;

// The phone number is this app's login identifier, so "is it free?" has to mean
// the same thing everywhere it is asked. Both steps of the change flow call this.
public static class PhoneNumberRules
{
    public static string Normalize(string? phoneNumber) => (phoneNumber ?? string.Empty).Trim();

    // A number is taken if ANY other account uses it as either its UserName or
    // its PhoneNumber. Both columns matter: request-otp looks accounts up by
    // UserName, verify-otp by PhoneNumber.
    public static async Task<bool> IsTakenByAnotherUserAsync(
        UserManager<ApplicationUser> userManager,
        string phoneNumber,
        string currentUserId,
        CancellationToken ct = default)
    {
        // FindByNameAsync normalises and hits the unique index
        var byUserName = await userManager.FindByNameAsync(phoneNumber);
        if (byUserName is not null && byUserName.Id != currentUserId)
            return true;

        return await userManager.Users
            .AnyAsync(u => u.PhoneNumber == phoneNumber && u.Id != currentUserId, ct);
    }
}
