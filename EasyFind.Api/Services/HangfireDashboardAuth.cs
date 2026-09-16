using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using EasyFind.Api.Models.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EasyFind.Api.Services;

// Sign-in for the Hangfire dashboard.
//
// WHY THIS EXISTS AT ALL: the dashboard is a browser page, and a browser sends
// no Authorization header when it navigates. Accepting ?access_token= on the
// URL is not enough either — Hangfire serves its CSS, its JS, the /hangfire/stats
// poll and every nav link as SEPARATE requests that carry no query string, so
// query-string auth yields an unstyled page that 401s the moment you click
// anything. It also writes a live admin JWT into ALB access logs, browser
// history and Referer headers. So the token is exchanged once for a cookie, and
// the cookie is what every subsequent request carries.
//
// WHY NOT AddCookie(): ASP.NET's cookie authentication encrypts the ticket with
// Data Protection keys, and nothing here persists those to shared storage — each
// ECS task generates its own key ring, so a cookie issued by one task fails on
// the next request that lands on another. The cookie therefore carries the JWT
// itself, validated with the same HMAC secret every task already shares. Same
// signature, same expiry, no new infrastructure.
//
// The cookie is HttpOnly (script cannot read it), Secure over HTTPS, and
// SameSite=Strict — that last one is load-bearing, not decoration: the dashboard
// performs its destructive actions (delete, requeue) as plain POSTs, and a
// cookie sent cross-site would let any page on the internet drive them.
public static class HangfireDashboardAuth
{
    public const string DashboardPath = "/hangfire";
    public const string LoginPath = "/hangfire/login";

    private const string CookieName = "yisru_hangfire_session";

    // A dashboard session never outlives this, however long the pasted token
    // had left to run.
    private static readonly TimeSpan MaxSession = TimeSpan.FromHours(8);

    // Reads the session cookie and, if it holds a valid token, puts the
    // principal on the context so the (synchronous) dashboard filter can read
    // it. Must run BEFORE UseHangfireDashboard.
    //
    // HttpContext.User is assigned directly rather than going through
    // authentication: the app's default scheme is JwtBearer and this is a
    // cookie, so nothing else would populate it.
    public static IApplicationBuilder UseHangfireDashboardAuth(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(DashboardPath)
                && context.Request.Cookies.TryGetValue(CookieName, out var token)
                && !string.IsNullOrWhiteSpace(token))
            {
                var principal = Validate(context, token);
                if (principal != null) context.User = principal;
            }

            await next();
        });

    // The login branch. Mapped before the dashboard so Hangfire's own middleware
    // — which answers every unrecognised path under /hangfire with a 404 —
    // cannot swallow it.
    public static IApplicationBuilder MapHangfireLogin(this IApplicationBuilder app)
        => app.Map(LoginPath, branch => branch.Run(async context =>
        {
            if (HttpMethods.IsGet(context.Request.Method))
            {
                await WritePageAsync(context, null);
                return;
            }

            if (!HttpMethods.IsPost(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                return;
            }

            var form = await context.Request.ReadFormAsync();
            var token = form["token"].ToString().Trim();

            var principal = string.IsNullOrWhiteSpace(token) ? null : Validate(context, token);

            // Deliberately one message for "not a valid token" and "valid but
            // not an admin": a login form that distinguishes them tells an
            // attacker which of the two they got right.
            if (principal == null || !IsDashboardAdmin(principal))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await WritePageAsync(context, "That token is not valid for an admin account.");
                return;
            }

            context.Response.Cookies.Append(CookieName, token, new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                Path = DashboardPath,              // never sent to the rest of the API
                Expires = DateTimeOffset.UtcNow.Add(MaxSession)
            });

            context.Response.Redirect(DashboardPath);
        }));

    public static bool IsDashboardAdmin(ClaimsPrincipal user)
        => user.Identity?.IsAuthenticated == true
           && (user.IsInRole(AppRoles.Admin) || user.IsInRole(AppRoles.SuperAdmin));

    // Validated against the SAME parameters the API uses for bearer tokens, so
    // a revoked secret or an expired token stops working here at the same moment
    // it stops working everywhere else.
    private static ClaimsPrincipal? Validate(HttpContext context, string token)
    {
        var parameters = context.RequestServices
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme)
            .TokenValidationParameters;

        try
        {
            var principal = new JwtSecurityTokenHandler()
                .ValidateToken(token, parameters, out _);

            // ValidateToken builds the identity from the raw claim URIs, so
            // IsInRole needs to be told which claim carries the role.
            var identity = new ClaimsIdentity(
                principal.Claims, "HangfireSession", ClaimTypes.Name, ClaimTypes.Role);

            return new ClaimsPrincipal(identity);
        }
        catch (Exception)
        {
            // Expired, tampered with, signed by an old secret — all the same
            // answer, and none of them worth logging a token over.
            return null;
        }
    }

    private static async Task WritePageAsync(HttpContext context, string? error)
    {
        var banner = error == null
            ? ""
            : $"<p class=\"error\">{WebUtility.HtmlEncode(error)}</p>";

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";

        await context.Response.WriteAsync($$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Hangfire — sign in</title>
              <style>
                :root { color-scheme: light dark; }
                body {
                  margin: 0; min-height: 100vh; display: grid; place-items: center;
                  font: 14px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif;
                  background: #f6f7f9; color: #1a1d21;
                }
                @media (prefers-color-scheme: dark) {
                  body { background: #16181d; color: #e8eaed; }
                  form { background: #1f2229 !important; border-color: #2e323b !important; }
                  textarea { background: #16181d !important; color: #e8eaed !important;
                             border-color: #2e323b !important; }
                }
                form {
                  width: min(92vw, 30rem); padding: 1.75rem; background: #fff;
                  border: 1px solid #e2e5ea; border-radius: .6rem;
                }
                h1 { margin: 0 0 .25rem; font-size: 1.1rem; }
                p.hint { margin: 0 0 1.25rem; color: #6b7280; font-size: .85rem; }
                textarea {
                  width: 100%; box-sizing: border-box; min-height: 7rem; padding: .6rem;
                  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
                  font-size: .8rem; border: 1px solid #d3d7de; border-radius: .35rem;
                  resize: vertical;
                }
                button {
                  margin-top: .9rem; width: 100%; padding: .6rem; font-size: .9rem;
                  font-weight: 600; color: #fff; background: #2563eb; border: 0;
                  border-radius: .35rem; cursor: pointer;
                }
                button:hover { background: #1d4ed8; }
                p.error {
                  margin: 0 0 1rem; padding: .6rem .75rem; font-size: .85rem;
                  color: #991b1b; background: #fee2e2; border-radius: .35rem;
                }
              </style>
            </head>
            <body>
              <form method="post" autocomplete="off">
                <h1>Hangfire dashboard</h1>
                <p class="hint">Paste an admin access token to start a session.</p>
                {{banner}}
                <textarea name="token" required autofocus spellcheck="false"
                          placeholder="eyJhbGciOi..."></textarea>
                <button type="submit">Sign in</button>
              </form>
            </body>
            </html>
            """);
    }
}
