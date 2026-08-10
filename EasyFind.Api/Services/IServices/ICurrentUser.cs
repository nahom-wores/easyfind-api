namespace EasyFind.Api.Services.IServices;

public interface ICurrentUser
{
    string? UserId { get; }
    string? Role { get; }
}