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
    // Pure yes/no, with no side effects. Sending someone to the login form is
    // UseHangfireDashboardAuth's job, because Hangfire overwrites the status
    // code after this returns false and would turn a redirect into a bare 401.
    public bool Authorize(DashboardContext context)
        => HangfireDashboardAuth.IsDashboardAdmin(context.GetHttpContext().User);
}
