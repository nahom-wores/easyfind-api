using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Services.IServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EasyFind.IntegrationTests;

// Guards the operational decisions in Program.cs that nothing else would catch.
//
// These are not feature tests — each one exists because reverting the decision
// it covers looks harmless in a diff and only shows up as an outage or an open
// door in production.
public class OperationalHardeningTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private static string NewPhone() => "+2519" + Random.Shared.Next(10_000_000, 99_999_999);

    private FakeSmsService Sms => factory.Services.GetRequiredService<FakeSmsService>();

    // ── Liveness must not depend on Postgres ─────────────────────────────────
    //
    // This test harness points DefaultConnection at a Postgres that does not
    // exist (the DbContext is SQLite), so the "postgres" readiness check cannot
    // pass here. That is exactly the condition being asserted: the load
    // balancer's probe still answers Healthy.
    //
    // If someone drops the `Predicate` and lets the database check back onto
    // /health, one RDS blip fails this probe on every ECS task at the same
    // moment, the ALB drains all of them, and a recoverable hiccup becomes a
    // full outage. This test fails the moment that happens.
    [Fact]
    public async Task Liveness_IsHealthy_EvenWhenTheDatabaseIsUnreachable()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "liveness answers 'is this process up', not 'is Postgres up' — " +
            "a task that cannot reach the database is still one worth keeping alive");
    }

    // The dependency check still has to exist somewhere, or the split has just
    // deleted the monitoring rather than moved it.
    [Fact]
    public async Task Readiness_IsMapped_AndReportsOnDependencies()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        // Unreachable Postgres here, so this is the unhealthy path — the point
        // is that the endpoint exists and actually reports the dependency,
        // which is what distinguishes it from liveness above.
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Contain("postgres");
    }

    // ── The Hangfire dashboard is never open ─────────────────────────────────
    //
    // It exposes job arguments and can enqueue, requeue and delete jobs, so an
    // unauthenticated /hangfire behind the ALB is remote control of the
    // background queue. It is opt-in (Hangfire:DashboardEnabled) and authorized
    // to Admin/SuperAdmin even when enabled.
    [Fact]
    public async Task HangfireDashboard_IsNotServedToAnonymousCallers()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/hangfire");

        response.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "the dashboard must never answer an unauthenticated caller");
    }

    [Fact]
    public async Task HangfireDashboard_IsNotServedToASignedInNonAdmin()
    {
        var (client, _) = await factory.SignedInUserAsync(role: AppRoles.User);

        var response = await client.GetAsync("/hangfire");

        response.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "an ordinary signed-in user must not reach the job queue");
    }

    // ── Access tokens expire soon enough to be a revocation window ────────────
    //
    // Nothing checks an issued access token against the database on the way in,
    // so a ban, a role change or a logout only takes effect when the current
    // token expires. That expiry IS the revocation window.
    //
    // It was AddDays(15) — fifteen days during which a banned user stayed
    // signed in. The bound below is deliberately generous; it is there to catch
    // a return to "days", not to pin the exact configured value.
    [Fact]
    public async Task IssuedAccessToken_ExpiresWithinHours_NotDays()
    {
        var client = factory.CreateClient();
        var phone = NewPhone();

        var requested = await client.PostAsJsonAsync(
            "/api/v1/auth/request-otp", new { phoneNumber = phone });
        requested.StatusCode.Should().Be(HttpStatusCode.Created);

        var otp = Sms.LastOtpFor(phone);
        otp.Should().NotBeNull();

        var verified = await client.PostAsJsonAsync(
            "/api/v1/auth/verify-otp", new { phoneNumber = phone, otp });
        verified.StatusCode.Should().Be(HttpStatusCode.OK);

        var accessToken = await ReadAccessTokenAsync(verified);
        accessToken.Should().NotBeNullOrEmpty();

        var expires = new JwtSecurityTokenHandler().ReadJwtToken(accessToken).ValidTo;

        expires.Should().BeAfter(DateTime.UtcNow,
            "a token that is born expired locks every client out");
        expires.Should().BeBefore(DateTime.UtcNow.AddHours(9),
            "an access token cannot be revoked, so its lifetime is the window an " +
            "attacker, a banned user or a demoted admin keeps their access");
    }

    // verify-otp is one of the two endpoints that does NOT use the ApiResponse
    // Result convention — it returns TokenDto nested under Result, because the
    // mobile client depends on that exact body.
    private static async Task<string?> ReadAccessTokenAsync(HttpResponseMessage response)
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        return document.RootElement
            .GetProperty("result")
            .GetProperty("accessToken")
            .GetString();
    }
}
