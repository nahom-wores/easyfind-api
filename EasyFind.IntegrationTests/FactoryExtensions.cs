using EasyFind.Api.Data;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace EasyFind.IntegrationTests;

// Small helpers so the tests read as "given this data, when this request, then"
// instead of drowning in scope/ServiceProvider plumbing.
public static class FactoryExtensions
{
    // Run something against the real DbContext (seeding, or asserting on state
    // after a request).
    public static async Task SeedAsync(
        this CustomWebApplicationFactory factory, Action<ApplicationDbContext> seed)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        seed(db);
        await db.SaveChangesAsync();
    }

    public static async Task<T> WithDbAsync<T>(
        this CustomWebApplicationFactory factory, Func<ApplicationDbContext, Task<T>> read)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await read(db);
    }

    // Resolve a handler exactly as a request would, for the cases where driving
    // the logic directly is clearer than going through HTTP.
    public static async Task<T> WithHandlerAsync<T>(
        this CustomWebApplicationFactory factory, Func<IServiceProvider, Task<T>> act)
    {
        using var scope = factory.Services.CreateScope();
        return await act(scope.ServiceProvider);
    }

    // Creates a real Identity user (so UserManager, roles and the tier column all
    // behave) and hands back a client whose requests are authenticated as them.
    public static async Task<(HttpClient client, ApplicationUser user)> SignedInUserAsync(
        this CustomWebApplicationFactory factory,
        SubscriptionTier tier = SubscriptionTier.Free,
        string role = AppRoles.User,
        string? phoneNumber = null)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));

        var phone = phoneNumber ?? "+2519" + Random.Shared.Next(10_000_000, 99_999_999);
        var user = new ApplicationUser
        {
            UserName = phone,
            PhoneNumber = phone,
            TwoFactorEnabled = true,
            SubscriptionTier = tier,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var created = await userManager.CreateAsync(user);
        if (!created.Succeeded)
            throw new InvalidOperationException(
                "Could not create test user: " +
                string.Join(", ", created.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(user, role);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, user.Id);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, role);

        return (client, user);
    }
}
