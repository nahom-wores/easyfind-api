using EasyFind.Api.Models.Auth;
using Hangfire.Dashboard;

namespace EasyFind.Api.Services;

// Hangfire's dashboard ships wide open: mapping it with no filter lets anyone
// who can reach the load balancer read job arguments and enqueue, requeue or
// delete jobs. It has no authorization of its own, so this filter is the whole
// of it.
//
// Two things have to line up for this to work at all:
//
//  1. UseHangfireDashboard must be called AFTER UseAuthentication/UseAuthorization.
//     Called earlier, HttpContext.User is still the empty anonymous principal
//     and this filter denies every request, including a legitimate admin's.
//  2. The dashboard is a browser page, and a browser will not attach an
//     Authorization header. Program.cs's JwtBearerEvents.OnMessageReceived
//     therefore also accepts ?access_token= on this path.
public class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var user = context.GetHttpContext().User;

        return user.Identity?.IsAuthenticated == true
               && (user.IsInRole(AppRoles.Admin) || user.IsInRole(AppRoles.SuperAdmin));
    }
}
