using System.Net;
using System.Net.Http.Json;
using EasyFind.Api.Models.Auth;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyFind.IntegrationTests;

// OTP throttling.
//
// The limit that matters is per PHONE NUMBER and lives in Postgres, because the
// API runs several ECS tasks and an in-process counter would multiply the cap by
// the task count and reset on deploy.
//
// The first test here is the regression for the previous implementation, which
// partitioned on an "X-Phone" header no client sends: every caller shared one
// bucket, so three requests locked out the entire product.
public class OtpThrottleTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    // Matches the test configuration.
    private const int SendsPerWindow = 3;
    private const int MaxFailedVerifications = 5;

    private static string NewPhone() => "+2519" + Random.Shared.Next(10_000_000, 99_999_999);

    private FakeSmsService Sms => factory.Services.GetRequiredService<FakeSmsService>();

    private Task<HttpResponseMessage> RequestOtpAsync(HttpClient client, string phone)
        => client.PostAsJsonAsync("/api/v1/auth/request-otp", new { phoneNumber = phone });

    private Task<HttpResponseMessage> VerifyOtpAsync(HttpClient client, string phone, string otp)
        => client.PostAsJsonAsync("/api/v1/auth/verify-otp", new { phoneNumber = phone, otp });

    [Fact]
    public async Task ThrottlingOneNumber_DoesNotAffectAnother()
    {
        // THE regression test. Exhaust one number completely, then prove a
        // different number is still served.
        var client = factory.CreateClient();
        var victim = NewPhone();
        var bystander = NewPhone();

        for (var i = 0; i < SendsPerWindow; i++)
            (await RequestOtpAsync(client, victim)).StatusCode
                .Should().Be(HttpStatusCode.Created);

        (await RequestOtpAsync(client, victim)).StatusCode
            .Should().Be(HttpStatusCode.TooManyRequests, "this number is exhausted");

        (await RequestOtpAsync(client, bystander)).StatusCode
            .Should().Be(HttpStatusCode.Created,
                "a different number must have its own allowance");
    }

    [Fact]
    public async Task SendLimit_IsEnforcedPerNumber_AndStopsTheSms()
    {
        var client = factory.CreateClient();
        var phone = NewPhone();

        for (var i = 0; i < SendsPerWindow; i++)
            await RequestOtpAsync(client, phone);

        var sentBefore = Sms.Sent.Count(s => s.Phone == phone);

        var blocked = await RequestOtpAsync(client, phone);

        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        Sms.Sent.Count(s => s.Phone == phone).Should().Be(sentBefore,
            "a throttled request must not reach the SMS gateway — that is the cost being controlled");
    }

    [Fact]
    public async Task Throttled_Response_UsesTheApiEnvelope_AndSaysWhenToRetry()
    {
        var client = factory.CreateClient();
        var phone = NewPhone();

        for (var i = 0; i < SendsPerWindow; i++)
            await RequestOtpAsync(client, phone);

        var blocked = await RequestOtpAsync(client, phone);

        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        blocked.Headers.RetryAfter.Should().NotBeNull(
            "the client should be told how long to wait, not have to guess");

        var errors = await blocked.ReadErrorsAsync();
        errors.Should().NotBeEmpty("429 must use the same ApiResponse shape as every other error");
    }

    [Fact]
    public async Task SendAllowance_RecoversWhenTheWindowRolls()
    {
        var client = factory.CreateClient();
        var phone = NewPhone();

        for (var i = 0; i < SendsPerWindow; i++)
            await RequestOtpAsync(client, phone);

        (await RequestOtpAsync(client, phone)).StatusCode
            .Should().Be(HttpStatusCode.TooManyRequests);

        // Age the window rather than waiting an hour.
        await factory.SeedAsync(db => db.OtpThrottles
            .Single(t => t.PhoneNumber == phone)
            .WindowStartedAt = DateTimeOffset.UtcNow.AddHours(-2));

        (await RequestOtpAsync(client, phone)).StatusCode
            .Should().Be(HttpStatusCode.Created, "the window has rolled over");
    }

    [Fact]
    public async Task RepeatedWrongCodes_LockTheNumberOut()
    {
        var client = factory.CreateClient();
        var phone = NewPhone();

        await RequestOtpAsync(client, phone);

        for (var i = 0; i < MaxFailedVerifications; i++)
            (await VerifyOtpAsync(client, phone, "000000")).StatusCode
                .Should().Be(HttpStatusCode.BadRequest);

        var locked = await VerifyOtpAsync(client, phone, "000000");
        locked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        locked.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task Lockout_RefusesEvenTheCorrectCode()
    {
        // Otherwise an attacker who happens to guess on the final permitted try
        // still gets in, and the lockout is decorative.
        var client = factory.CreateClient();
        var phone = NewPhone();

        await RequestOtpAsync(client, phone);
        var realOtp = Sms.LastOtpFor(phone);
        realOtp.Should().NotBeNull();

        for (var i = 0; i < MaxFailedVerifications; i++)
            await VerifyOtpAsync(client, phone, "000000");

        var withCorrectCode = await VerifyOtpAsync(client, phone, realOtp!);

        withCorrectCode.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "a locked-out number is refused regardless of the code offered");
    }

    [Fact]
    public async Task SuccessfulVerification_ClearsPreviousFailures()
    {
        var client = factory.CreateClient();
        var phone = NewPhone();

        await RequestOtpAsync(client, phone);
        var realOtp = Sms.LastOtpFor(phone)!;

        // A couple of typos, then the right code.
        await VerifyOtpAsync(client, phone, "000000");
        await VerifyOtpAsync(client, phone, "111111");

        (await VerifyOtpAsync(client, phone, realOtp)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var throttle = await factory.WithDbAsync(db => db.OtpThrottles
            .AsNoTracking().SingleAsync(t => t.PhoneNumber == phone));

        throttle.FailedVerifications.Should().Be(0,
            "a legitimate user should not carry old strikes");
        throttle.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task LockingOneNumber_DoesNotLockAnother()
    {
        var client = factory.CreateClient();
        var locked = NewPhone();
        var other = NewPhone();

        await RequestOtpAsync(client, locked);
        await RequestOtpAsync(client, other);
        var otherOtp = Sms.LastOtpFor(other)!;

        for (var i = 0; i < MaxFailedVerifications + 1; i++)
            await VerifyOtpAsync(client, locked, "000000");

        (await VerifyOtpAsync(client, other, otherOtp)).StatusCode
            .Should().Be(HttpStatusCode.OK, "lockouts are per number");
    }

    // NOT TESTED HERE: that a concurrent burst cannot exceed the allowance.
    //
    // The counter is a conditional UPDATE evaluated by the database precisely so
    // simultaneous requests cannot all read the same count and slip through, but
    // that cannot be demonstrated on SQLite: the tests share one in-memory
    // connection, so ten parallel requests just produce "database is locked".
    // A test that serialises the requests would prove nothing about atomicity,
    // and a flaky one is worse than none. Verifying this needs a real Postgres.
}
