using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace EasyFind.IntegrationTests;

// Sign-in for the Hangfire dashboard.
//
// This does NOT go through CustomWebApplicationFactory: UseHangfireDashboard
// needs Hangfire's Postgres storage at startup, which is exactly why the harness
// sets Hangfire__Enabled=false. So the pieces that guard the dashboard —
// the login branch, the cookie exchange and the admin check — are hosted here on
// a bare TestServer, with a terminal handler standing in for Hangfire itself.
// The dashboard middleware is library code; what needs proving is that nothing
// reaches it without an admin session.
public class HangfireDashboardAuthTests : IAsyncLifetime
{
    private const string Secret = "test-secret-key-at-least-32-characters-long-for-testing";

    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    // The dashboard validates against the app's own bearer
                    // parameters, so they have to be registered here too.
                    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                        .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(Secret)),
                            ValidateIssuer = false,
                            ValidateAudience = false,
                            ClockSkew = TimeSpan.Zero,
                        });
                })
                .Configure(app =>
                {
                    app.MapHangfireLogin();
                    app.UseHangfireDashboardAuth();

                    // Stands in for UseHangfireDashboard: answers 200 only for a
                    // principal the real filter would accept.
                    app.Run(async context =>
                    {
                        var allowed = HangfireDashboardAuth.IsDashboardAdmin(context.User);
                        context.Response.StatusCode = allowed
                            ? StatusCodes.Status200OK
                            : StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsync(allowed ? "dashboard" : "denied");
                    });
                }))
            .StartAsync();

        // TestServer's handler does not follow redirects, so the 302 out of a
        // successful sign-in is observable.
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    private static string TokenFor(params string[] roles) => TokenFor(TimeSpan.FromHours(1), roles);

    private static string TokenFor(TimeSpan lifetime, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, "+251911000000"),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        // NotBefore is anchored an hour behind the expiry rather than left to
        // default to "now": a token that has already expired would otherwise be
        // refused at creation time (Expires before NotBefore) and the expiry
        // case could not be set up at all.
        var expires = DateTime.UtcNow.Add(lifetime);

        var token = new JwtSecurityTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            NotBefore = expires - TimeSpan.FromHours(1),
            IssuedAt = expires - TimeSpan.FromHours(1),
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.ASCII.GetBytes(Secret)),
                SecurityAlgorithms.HmacSha256Signature)
        });

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private Task<HttpResponseMessage> SignInAsync(string token)
        => _client.PostAsync(HangfireDashboardAuth.LoginPath,
            new FormUrlEncodedContent([new KeyValuePair<string, string>("token", token)]));

    private static string? SessionCookieFrom(HttpResponseMessage response)
        => response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault()
            : null;

    private async Task<HttpResponseMessage> GetDashboardAsync(string? cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, HangfireDashboardAuth.DashboardPath);
        if (cookie != null) request.Headers.Add("Cookie", cookie.Split(';')[0]);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Dashboard_RefusesACallerWithNoSession()
    {
        var response = await GetDashboardAsync(null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LoginPage_IsServedToAnonymousCallers()
    {
        // It has to be reachable without a session — it is how you get one.
        var response = await _client.GetAsync(HangfireDashboardAuth.LoginPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("<form");
    }

    [Fact]
    public async Task AdminToken_IsExchangedForASession_ThatOpensTheDashboard()
    {
        var response = await SignInAsync(TokenFor(AppRoles.Admin));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be(HangfireDashboardAuth.DashboardPath);

        var cookie = SessionCookieFrom(response);
        cookie.Should().NotBeNull();

        (await GetDashboardAsync(cookie)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SuperAdmin_IsAlsoAdmittted()
    {
        var response = await SignInAsync(TokenFor(AppRoles.SuperAdmin));

        (await GetDashboardAsync(SessionCookieFrom(response))).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    // The session cookie is the dashboard's whole authentication, and the
    // dashboard performs deletes and requeues as ordinary POSTs. These three
    // flags are what stop script reading it and stop another site driving it.
    [Fact]
    public async Task SessionCookie_IsHttpOnly_SameSiteStrict_AndScopedToTheDashboard()
    {
        var cookie = SessionCookieFrom(await SignInAsync(TokenFor(AppRoles.Admin)));

        cookie.Should().Contain("httponly", "script must never be able to read it");
        cookie.Should().Contain("samesite=strict",
            "the dashboard deletes and requeues jobs over plain POSTs — a cookie " +
            "sent cross-site would let any page on the internet drive them");
        cookie.Should().Contain($"path={HangfireDashboardAuth.DashboardPath}",
            "it must never be attached to the rest of the API");
    }

    [Fact]
    public async Task OrdinaryUserToken_IsRejected_AndGetsNoSession()
    {
        var response = await SignInAsync(TokenFor(AppRoles.User));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        SessionCookieFrom(response).Should().BeNull("a non-admin must not get a session at all");
    }

    [Fact]
    public async Task TokenWithNoRoles_IsRejected()
    {
        var response = await SignInAsync(TokenFor());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        SessionCookieFrom(response).Should().BeNull();
    }

    [Fact]
    public async Task TokenSignedWithTheWrongSecret_IsRejected()
    {
        var forged = new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity([new Claim(ClaimTypes.Role, AppRoles.SuperAdmin)]),
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.ASCII.GetBytes(
                        "a-completely-different-secret-of-sufficient-length!!")),
                    SecurityAlgorithms.HmacSha256Signature)
            }));

        var response = await SignInAsync(forged);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        SessionCookieFrom(response).Should().BeNull();
    }

    [Fact]
    public async Task ExpiredAdminToken_IsRejected()
    {
        var response = await SignInAsync(TokenFor(TimeSpan.FromMinutes(-5), AppRoles.Admin));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        SessionCookieFrom(response).Should().BeNull();
    }

    // The cookie carries the token itself (no shared Data Protection keys across
    // ECS tasks), so the session cannot outlive the token inside it — the cookie
    // path has to re-check expiry on every request, not just at sign-in.
    //
    // Asserted by presenting an already-expired token as the cookie rather than
    // by minting a short-lived one and sleeping past it: a wall-clock race is
    // decided by how loaded the machine is, and the sleeping version did fail
    // spuriously on a busy one.
    [Fact]
    public async Task SessionDies_WhenTheTokenInsideItExpires()
    {
        var live = SessionCookieFrom(await SignInAsync(TokenFor(AppRoles.Admin)));
        (await GetDashboardAsync(live)).StatusCode.Should().Be(HttpStatusCode.OK,
            "a session holding a live token opens the dashboard");

        var expired = $"yisru_hangfire_session={TokenFor(TimeSpan.FromMinutes(-5), AppRoles.Admin)}";

        (await GetDashboardAsync(expired)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the same cookie with an expired token inside it must not");
    }

    [Fact]
    public async Task GarbageInTheCookie_IsRefusedRatherThanThrowing()
    {
        var response = await GetDashboardAsync("yisru_hangfire_session=not-a-jwt");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
