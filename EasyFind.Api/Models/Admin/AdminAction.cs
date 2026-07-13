using EasyFind.Api.Models.Auth;

namespace EasyFind.Api.Models.Admin;

public class AdminAction
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Who did it
    public string AdminUserId { get; set; } = string.Empty;
    public ApplicationUser AdminUser { get; set; } = null!;

    // Who it was done to
    public string TargetUserId { get; set; } = string.Empty;

    public AdminActionType ActionType { get; set; }
    public string Details { get; set; } = string.Empty;   // human-readable summary
    public string? Reason { get; set; }                    // admin-supplied justification

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum AdminActionType
{
    SubscriptionGranted = 0,
    SubscriptionRevoked = 1,
    RoleAssigned = 2,
    // extend as admin capabilities grow
}