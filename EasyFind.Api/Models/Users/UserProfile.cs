using System.ComponentModel.DataAnnotations;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Enum;

namespace EasyFind.Api.Models.Users;

public class UserProfile
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    // FK to Identity user — string, to match ApplicationUser.Id
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    // ── What the user is looking for ────────────────
    public SeekingType SeekingType { get; set; } = SeekingType.Both;

    // Target destination countries (ISO alpha-2): ["DE","CA","GB"]
    public List<string> TargetCountries { get; set; } = []; // PostgreSQL has native array support, and Npgsql maps List<T> directly to a Postgres array column (text[], integer[])

    // Job preferences (used when SeekingType includes Job)
    public List<JobCategory> PreferredJobCategories { get; set; } = [];

    // Scholarship preferences (used when SeekingType includes Scholarship)
    public List<ScholarshipField> PreferredScholarshipFields { get; set; } = [];
    public DegreeLevel? TargetDegreeLevel { get; set; }

    // ── Who the user is ─────────────────────────────
    public EducationLevel EducationLevel { get; set; }
    public short WorkExperienceYears { get; set; }
    public ExperienceRange ExperienceRange { get; set; }
    [MaxLength(10)]
    public string? EnglishLevel { get; set; }      // CEFR: "B2", "C1"
    
    public DateOnly? DateOfBirth { get; set; }
    public Sex? Sex { get; set; }
    public PassportStatus? PassportStatus { get; set; }
    
    // CV stored in Cloudinary (you already have ImageService;
    // we'll add a raw-file path for PDFs/docs)
    public string? CvFileUrl { get; set; }
    public DateTimeOffset? CvUploadedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum SeekingType
{
    Job = 0,
    Scholarship = 1,
    Both = 2
}

public enum EducationLevel
{
    HighSchool = 0,
    Diploma = 1,
    Bachelor = 2,
    Master = 3,
    Phd = 4,
    Other = 99
}

public enum PassportStatus
{
    HasValid = 0,
    None = 1,
}
public enum Sex
{
    Male = 0,
    Female = 1,
    PreferNotToSay = 99
}

public enum ExperienceRange
{
    LessThan3 = 0,
    ThreeToFive = 1,
    FiveToTen = 2,
    TenPlus = 3
}