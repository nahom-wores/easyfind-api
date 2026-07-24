using EasyFind.Api.Models.Enum;
using EasyFind.Api.Models.Users;

namespace EasyFind.Api.Models.Dto.Profile;

public class OnboardingDto
{
    // Identity (writes to ApplicationUser)
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    // What they're seeking (writes to UserProfile)
    public SeekingType SeekingType { get; set; } = SeekingType.Both;
    public List<string> TargetCountries { get; set; } = [];
    public List<JobCategory> PreferredJobCategories { get; set; } = [];
    public List<ScholarshipField> PreferredScholarshipFields { get; set; } = [];
    public DegreeLevel? TargetDegreeLevel { get; set; }

    public EducationLevel EducationLevel { get; set; }
    public short WorkExperienceYears { get; set; }
    public ExperienceRange ExperienceRange { get; set; }
    public string? EnglishLevel { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public Sex? Sex { get; set; }
    public PassportStatus? PassportStatus { get; set; }
    public EnglishTestType? EnglishTestType { get; set; }
    public string? EnglishTestScore { get; set; }
}
public class ProfileResponseDto
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string SeekingType { get; set; } = string.Empty;
    public List<string> TargetCountries { get; set; } = [];
    public List<int> PreferredJobCategories { get; set; } = [];
    public List<int> PreferredScholarshipFields { get; set; } = [];
    public int? TargetDegreeLevel { get; set; }
    public string EducationLevel { get; set; } = string.Empty;
    public short WorkExperienceYears { get; set; }
    public ExperienceRange ExperienceRange { get; set; }
    public string? EnglishLevel { get; set; }
    public int? EnglishTestType { get; set; }
    public string? EnglishTestScore { get; set; }
    public string? CvFileUrl { get; set; }
    public bool HasProfile { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Sex { get; set; }
    public string? PassportStatus { get; set; }
    
}