using System.Net;
using System.Net.Http.Json;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Subscriptions;
using FluentAssertions;

namespace EasyFind.IntegrationTests;

// SuperAdmin includes Admin. Identity roles are flat, so with
// [Authorize(Roles = "Admin")] an account holding only SuperAdmin got 403 on
// every admin endpoint — even the SuperAdmin-only actions, which also sat
// behind the class-level Admin check. AppPolicies encodes the hierarchy; these
// pin it from both sides.
public class RoleHierarchyTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private const string AdminEndpoint = "/api/v1/admin/stats/overview";

    private static string GrantEndpoint(string userId) => $"/api/v1/admin/users/{userId}/subscription/grant";

    private static readonly object Grant = new { tier = SubscriptionTier.Pro, durationDays = 30, reason = "test" };

    [Fact]
    public async Task SuperAdminOnly_CanUseAdminEndpoints()
    {
        var (superAdmin, _) = await factory.SignedInUserAsync(role: AppRoles.SuperAdmin);

        (await superAdmin.GetAsync(AdminEndpoint)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SuperAdminOnly_CanUseSuperAdminEndpoints()
    {
        var (superAdmin, _) = await factory.SignedInUserAsync(role: AppRoles.SuperAdmin);
        var (_, target) = await factory.SignedInUserAsync();

        (await superAdmin.PostAsJsonAsync(GrantEndpoint(target.Id), Grant)).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_CanUseAdminEndpoints_ButNotSuperAdminOnes()
    {
        var (admin, _) = await factory.SignedInUserAsync(role: AppRoles.Admin);
        var (_, target) = await factory.SignedInUserAsync();

        (await admin.GetAsync(AdminEndpoint)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsJsonAsync(GrantEndpoint(target.Id), Grant)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    // assign-role grants whatever role it's asked for, so if an Admin could call
    // it they could promote themselves to SuperAdmin.
    [Fact]
    public async Task Admin_CannotAssignRoles_EvenToThemselves()
    {
        var (admin, self) = await factory.SignedInUserAsync(role: AppRoles.Admin);

        var response = await admin.PostAsJsonAsync("/api/v1/auth/assign-role",
            new { userId = self.Id, role = AppRoles.SuperAdmin });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SuperAdmin_CanAssignRoles()
    {
        var (superAdmin, _) = await factory.SignedInUserAsync(role: AppRoles.SuperAdmin);
        var (_, target) = await factory.SignedInUserAsync();

        var response = await superAdmin.PostAsJsonAsync("/api/v1/auth/assign-role",
            new { userId = target.Id, role = AppRoles.Admin });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task User_IsForbiddenFromAdminEndpoints()
    {
        var (user, _) = await factory.SignedInUserAsync();

        (await user.GetAsync(AdminEndpoint)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
