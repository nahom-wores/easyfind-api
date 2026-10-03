namespace EasyFind.Api.Services.IServices;

public interface ICurrentUser
{
    string? UserId { get; }

    // Role membership must be asked, not read: a user can hold several roles
    // (a promoted admin keeps "User"), so a single role claim is never the answer.
    bool IsInRole(string role);
}
