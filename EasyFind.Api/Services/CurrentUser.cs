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
    public string UserId  =>
        http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
    public string Role => 
        http.HttpContext?.User.FindFirstValue(ClaimTypes.Role);
}