using System.Net;
using System.Net.Http.Json;
using EasyFind.Api.Models.Auth;
using Microsoft.AspNetCore.Identity;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyFind.IntegrationTests;

// The phone number is the login identifier, and sign-in reads it from two
// different columns: RequestOtpHandler finds the account by UserName,
// VerifyOtpHandler by PhoneNumber. RequestOtpHandler also CREATES an account
// when its lookup misses.
//
// So if those columns ever drift apart, the user's next sign-in silently
// registers a second account and strands their subscription on the first. Every
// test here ultimately asserts UserName == PhoneNumber.
public class PhoneChangeTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private static string NewPhone() => "+2519" + Random.Shared.Next(10_000_000, 99_999_999);

    private Task<ApplicationUser> ReloadAsync(string userId)
        => factory.WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == userId));

    private FakeSmsService Sms => factory.Services.GetRequiredService<FakeSmsService>();

    [Fact]
    public async Task FullFlow_MovesUserNameAndPhoneNumberTogether()
    {
        var (client, user) = await factory.SignedInUserAsync();
        var oldPhone = user.PhoneNumber!;
        var newPhone = NewPhone();

        // Step 1 — code goes to the NEW number
        var request = await client.PostAsJsonAsync(
            "/api/v1/auth/me/phone/request-otp", new { phoneNumber = newPhone });
        request.StatusCode.Should().Be(HttpStatusCode.OK);

        Sms.LastOtpFor(newPhone).Should().NotBeNull(
            "the code must be sent to the number being claimed");
        Sms.LastOtpFor(oldPhone).Should().BeNull(
            "sending it to the old number would prove nothing about the new one");

        // Step 2 — send the code back with the same number
        var confirm = await client.PostAsJsonAsync(
            "/api/v1/auth/me/phone/confirm",
            new { phoneNumber = newPhone, otp = Sms.LastOtpFor(newPhone) });

        confirm.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await ReloadAsync(user.Id);
        updated.PhoneNumber.Should().Be(newPhone);
        updated.UserName.Should().Be(newPhone, "UserName must move with the number");
        updated.UserName.Should().Be(updated.PhoneNumber, "the two must never diverge");
        updated.PhoneNumberConfirmed.Should().BeTrue();
        updated.NormalizedUserName.Should().Be(newPhone.ToUpperInvariant(),
            "the normalised column is what FindByNameAsync actually matches on");
    }

    [Fact]
    public async Task WrongCode_ChangesNothing()
    {
        var (client, user) = await factory.SignedInUserAsync();
        var oldPhone = user.PhoneNumber!;
        var newPhone = NewPhone();

        await client.PostAsJsonAsync("/api/v1/auth/me/phone/request-otp",
            new { phoneNumber = newPhone });

        var confirm = await client.PostAsJsonAsync("/api/v1/auth/me/phone/confirm",
            new { phoneNumber = newPhone, otp = "000000" });

        confirm.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var unchanged = await ReloadAsync(user.Id);
        unchanged.PhoneNumber.Should().Be(oldPhone);
        unchanged.UserName.Should().Be(oldPhone);
    }

    [Fact]
    public async Task CodeIssuedForOneNumber_CannotConfirmAnother()
    {
        // The token is bound to (user + number). Without that binding, a user
        // could request a code for a number they own and redeem it against one
        // they do not.
        var (client, user) = await factory.SignedInUserAsync();
        var oldPhone = user.PhoneNumber!;
        var claimedPhone = NewPhone();
        var otherPhone = NewPhone();

        await client.PostAsJsonAsync("/api/v1/auth/me/phone/request-otp",
            new { phoneNumber = claimedPhone });
        var otp = Sms.LastOtpFor(claimedPhone);

        var confirm = await client.PostAsJsonAsync("/api/v1/auth/me/phone/confirm",
            new { phoneNumber = otherPhone, otp });

        confirm.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var unchanged = await ReloadAsync(user.Id);
        unchanged.PhoneNumber.Should().Be(oldPhone);
        unchanged.UserName.Should().Be(oldPhone);
    }

    [Fact]
    public async Task NumberAlreadyTaken_IsRejected()
    {
        var (_, existing) = await factory.SignedInUserAsync();
        var (client, mover) = await factory.SignedInUserAsync();

        var response = await client.PostAsJsonAsync("/api/v1/auth/me/phone/request-otp",
            new { phoneNumber = existing.PhoneNumber });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Sms.LastOtpFor(existing.PhoneNumber!).Should().BeNull(
            "we must not text someone else's phone on demand");

        var unchanged = await ReloadAsync(mover.Id);
        unchanged.PhoneNumber.Should().NotBe(existing.PhoneNumber);
    }

    [Fact]
    public async Task ChangingToTheSameNumber_IsRejected()
    {
        var (client, user) = await factory.SignedInUserAsync();

        var response = await client.PostAsJsonAsync("/api/v1/auth/me/phone/request-otp",
            new { phoneNumber = user.PhoneNumber });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AfterChange_TheAccountIsFoundByTheNewNumber_AndNotTheOld()
    {
        // The actual failure mode this flow exists to prevent: sign-in looks the
        // account up by UserName, so after a change the new number must resolve
        // to the SAME account and the old number to none.
        var (client, user) = await factory.SignedInUserAsync();
        var oldPhone = user.PhoneNumber!;
        var newPhone = NewPhone();

        await client.PostAsJsonAsync("/api/v1/auth/me/phone/request-otp",
            new { phoneNumber = newPhone });
        await client.PostAsJsonAsync("/api/v1/auth/me/phone/confirm",
            new { phoneNumber = newPhone, otp = Sms.LastOtpFor(newPhone) });

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();

        var byNew = await userManager.FindByNameAsync(newPhone);
        byNew.Should().NotBeNull();
        byNew!.Id.Should().Be(user.Id, "it must be the same account, not a new one");

        var byOld = await userManager.FindByNameAsync(oldPhone);
        byOld.Should().BeNull("the old number must no longer resolve to an account");
    }
}
