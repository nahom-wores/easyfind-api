using Hangfire.Dashboard;

namespace EasyFind.Api.Services;

// Hangfire's dashboard ships wide open: mapping it with no filter lets anyone
// who can reach the load balancer read job arguments and enqueue, requeue or
// delete jobs. It has no authorization of its own, so this filter is the whole
// of it.
//
// The principal it reads is put on the context by UseHangfireDashboardAuth,
// which must run before UseHangfireDashboard — see HangfireDashboardAuth for
// why the dashboard has its own cookie session rather than using the bearer
// token directly.
public class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        if (HangfireDashboardAuth.IsDashboardAdmin(httpContext.User))
            return true;

        // Send a person to the login form rather than leaving them at a blank
        // 401 — but only for a page they navigated to. Redirecting the
        // dashboard's own CSS, JS and stats polling would answer them with HTML
        // and quietly corrupt the page instead of failing honestly.
        if (HttpMethods.IsGet(httpContext.Request.Method)
            && httpContext.Request.Headers.Accept.ToString().Contains("text/html"))
        {
            httpContext.Response.Redirect(HangfireDashboardAuth.LoginPath);
        }

        return false;
    }
}
