using System.Security.Claims;
using EasyFind.Api.Services.IServices;

namespace EasyFind.Api.Services;

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor http;

    public CurrentUser(IHttpContextAccessor http)
    {
        this.http = http;
    }

    public string? UserId =>
        http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

    // Checks every role claim, not just the first one.
    public bool IsInRole(string role) =>
        http.HttpContext?.User.IsInRole(role) ?? false;
}
