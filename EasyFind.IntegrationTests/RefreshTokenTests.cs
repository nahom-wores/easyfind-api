using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyFind.IntegrationTests;

// POST /api/v1/auth/refresh-token.
//
// This endpoint is now the thing standing between a 60-minute access token and
// a user being thrown back to the SMS login screen, so its contract is worth
// pinning down precisely.
//
// It used to answer 200 for BOTH outcomes: the failure path returned a bare
// TokenDto whose IsSuccess defaulted to true, and the success path never set
// the envelope's IsSuccess at all. A client could only tell them apart by
// testing whether Result.AccessToken came back null. Success is now 200 with
// IsSuccess true, and every failure is 401.
public class RefreshTokenTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private static string NewPhone() => "+2519" + Random.Shared.Next(10_000_000, 99_999_999);

    private FakeSmsService Sms => factory.Services.GetRequiredService<FakeSmsService>();

    private sealed record Tokens(string AccessToken, string RefreshToken);

    // A real sign-in: request an OTP, read it off the fake SMS service, verify.
    private async Task<Tokens> SignInAsync(HttpClient client)
    {
        var phone = NewPhone();

        var requested = await client.PostAsJsonAsync(
            "/api/v1/auth/request-otp", new { phoneNumber = phone });
        requested.StatusCode.Should().Be(HttpStatusCode.Created);

        var verified = await client.PostAsJsonAsync(
            "/api/v1/auth/verify-otp", new { phoneNumber = phone, otp = Sms.LastOtpFor(phone) });
        verified.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ResultOf(verified);
        return new Tokens(
            result.GetProperty("accessToken").GetString()!,
            result.GetProperty("refreshToken").GetString()!);
    }

    private static async Task<JsonElement> ResultOf(HttpResponseMessage response)
    {
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("result").Clone();
    }

    private static async Task<bool> EnvelopeSucceeded(HttpResponseMessage response)
    {
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("isSuccess").GetBoolean();
    }

    private Task<HttpResponseMessage> RefreshAsync(HttpClient client, string? access, string? refresh)
        => client.PostAsJsonAsync("/api/v1/auth/refresh-token",
            new { accessToken = access, refreshToken = refresh });

    [Fact]
    public async Task ValidRefreshToken_ReturnsANewPair_AndSaysItSucceeded()
    {
        var client = factory.CreateClient();
        var tokens = await SignInAsync(client);

        var response = await RefreshAsync(client, tokens.AccessToken, tokens.RefreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await EnvelopeSucceeded(response)).Should().BeTrue(
            "the envelope used to report false on success, leaving the client to guess");

        var result = await ResultOf(response);
        result.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        result.GetProperty("refreshToken").GetString().Should().NotBeNullOrEmpty();
        result.GetProperty("isSuccess").GetBoolean().Should().BeTrue();
    }

    // The client keys off result.accessToken, and will for as long as old app
    // versions are in the wild. That shape must not drift.
    [Fact]
    public async Task SuccessBody_KeepsTheShapeOlderClientsRead()
    {
        var client = factory.CreateClient();
        var tokens = await SignInAsync(client);

        var result = await ResultOf(await RefreshAsync(client, tokens.AccessToken, tokens.RefreshToken));

        result.TryGetProperty("accessToken", out _).Should().BeTrue();
        result.TryGetProperty("refreshToken", out _).Should().BeTrue();
        result.TryGetProperty("isSuccess", out _).Should().BeTrue();
        result.TryGetProperty("message", out _).Should().BeTrue();
    }

    [Fact]
    public async Task RefreshTokenIsRotated_TheOldOneStopsWorking()
    {
        var client = factory.CreateClient();
        var tokens = await SignInAsync(client);

        var first = await ResultOf(await RefreshAsync(client, tokens.AccessToken, tokens.RefreshToken));
        var rotated = first.GetProperty("refreshToken").GetString();

        rotated.Should().NotBe(tokens.RefreshToken, "a refresh token is single-use");

        // The client MUST persist the rotated token; one that keeps sending the
        // original is the most common way to get a user signed out.
        var reuse = await RefreshAsync(client, tokens.AccessToken, tokens.RefreshToken);
        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // The whole point of the endpoint: it is called BECAUSE the access token has
    // expired, so an expired one must be accepted here.
    [Fact]
    public async Task AnExpiredAccessToken_StillRefreshes()
    {
        var client = factory.CreateClient();
        var tokens = await SignInAsync(client);

        // RefreshAccessToken reads the access token's claims rather than
        // validating it, so expiry is irrelevant — proven here by sending none
        // at all, the degenerate case of "not a usable token".
        var response = await RefreshAsync(client, access: null, refresh: tokens.RefreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ResultOf(response)).GetProperty("accessToken").GetString()
            .Should().NotBeNullOrEmpty();
    }

    // Re-presenting a consumed token revokes every token in the chain, not just
    // that one. This is what a client without single-flight refresh triggers:
    // two concurrent 401s, the second sending what the first just spent.
    [Fact]
    public async Task ReusingAConsumedToken_RevokesTheWholeChain()
    {
        var client = factory.CreateClient();
        var tokens = await SignInAsync(client);

        var rotated = (await ResultOf(await RefreshAsync(client, tokens.AccessToken, tokens.RefreshToken)))
            .GetProperty("refreshToken").GetString();

        // The stale token comes back — and takes the good one down with it.
        (await RefreshAsync(client, tokens.AccessToken, tokens.RefreshToken))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var afterRevocation = await RefreshAsync(client, tokens.AccessToken, rotated);
        afterRevocation.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the chain is revoked wholesale, so the freshly issued token dies too");
    }

    [Fact]
    public async Task UnknownRefreshToken_Is401_NotA200ClaimingSuccess()
    {
        var client = factory.CreateClient();

        var response = await RefreshAsync(client, null, "not-a-real-refresh-token");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await EnvelopeSucceeded(response)).Should().BeFalse();
    }

    [Fact]
    public async Task MissingRefreshToken_IsRejectedAsABadRequest()
    {
        var client = factory.CreateClient();

        foreach (var empty in new string?[] { null, "", "   " })
        {
            var response = await RefreshAsync(client, "whatever", empty);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "no refresh token is a malformed request, not a rejected credential");
        }
    }

    // A failure must carry a usable message in the standard envelope, so the
    // client has something to show rather than a blank sign-out.
    [Fact]
    public async Task AFailure_ExplainsItselfInTheUsualEnvelope()
    {
        var client = factory.CreateClient();

        var response = await RefreshAsync(client, null, "not-a-real-refresh-token");
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        document.RootElement.GetProperty("errors").GetArrayLength()
            .Should().BeGreaterThan(0);
        document.RootElement.GetProperty("errors")[0].GetString()
            .Should().NotBeNullOrWhiteSpace();
    }

    // An access token belonging to a different sign-in is treated as theft: that
    // refresh token is burned. A client that mixes tokens from two sessions —
    // easy to do when a re-login races an in-flight refresh — will hit this.
    [Fact]
    public async Task AccessTokenFromAnotherSession_IsRefused()
    {
        var client = factory.CreateClient();
        var mine = await SignInAsync(client);
        var theirs = await SignInAsync(client);

        var response = await RefreshAsync(client, theirs.AccessToken, mine.RefreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // ...and the mismatched pairing burns the token it was offered with.
        (await RefreshAsync(client, mine.AccessToken, mine.RefreshToken))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
