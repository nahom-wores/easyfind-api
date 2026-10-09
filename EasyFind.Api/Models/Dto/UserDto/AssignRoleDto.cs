namespace EasyFind.Api.Models.Dto.UserDto;

public class AssignRoleDto
{
    public string UserId { get; set; } = string.Empty;
    // "User" | "Admin" | "SuperAdmin" (see AppRoles)
    public string Role { get; set; } = string.Empty;
}