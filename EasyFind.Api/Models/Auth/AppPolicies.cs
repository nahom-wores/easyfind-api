namespace EasyFind.Api.Models.Auth;

// The role hierarchy lives here, in code, not in role assignments.
//
// Roles are flat in Identity: [Authorize(Roles = "Admin")] checks for that exact
// role, so a SuperAdmin without Admin was locked out of every admin endpoint —
// including the SuperAdmin-only actions, which also sat behind the class-level
// Admin check. Policies say "SuperAdmin includes Admin" once, so promoting
// someone is a single role change and nothing can drift out of sync.
//
// Use these on controllers instead of [Authorize(Roles = ...)]. Registered in
// Program.cs.
public static class AppPolicies
{
    public const string AdminAccess = "AdminAccess";            // Admin or SuperAdmin
    public const string SuperAdminAccess = "SuperAdminAccess";  // SuperAdmin only
}
