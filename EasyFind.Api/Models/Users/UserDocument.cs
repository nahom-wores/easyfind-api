using EasyFind.Api.Models.Auth;
using Scalar.AspNetCore;

namespace EasyFind.Api.Models.Users;

public class UserDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public DocumentType Type { get; set; }
    public string FileName { get; set; } = string.Empty;   // original name, for display
    public string StorageKey { get; set; } = string.Empty; // our generated key
    public long FileSizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;

    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}
public enum DocumentType
{
    Cv = 0,
    Transcript = 1,
    Certificate = 2,
    Passport = 3,
    RecommendationLetter = 4,
    Other = 99
}