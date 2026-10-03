using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Profile;
using EasyFind.Api.Models.Users;

namespace EasyFind.Api.Features.Profile;

// A profile response is stitched from two records: the Identity user (name) and
// the UserProfile (preferences).
public static class ProfileMapper
{
    public static ProfileResponseDto ToResponseDto(this UserProfile p, ApplicationUser u) => new()
    {
        FirstName = u.FirstName ?? "",
        LastName = u.LastName ?? "",
        SeekingType = p.SeekingType.ToString(),
        TargetCountries = p.TargetCountries,
        PreferredJobCategories = p.PreferredJobCategories.Select(c => (int)c).ToList(),
        PreferredScholarshipFields = p.PreferredScholarshipFields.Select(f => (int)f).ToList(),
        TargetDegreeLevel = (int?)p.TargetDegreeLevel,
        EducationLevel = p.EducationLevel.ToString(),
        WorkExperienceYears = p.WorkExperienceYears,
        ExperienceRange = p.ExperienceRange,
        EnglishLevel = p.EnglishLevel,
        CvFileUrl = p.CvFileUrl,
        HasProfile = true,
        DateOfBirth = p.DateOfBirth,
        Sex = p.Sex?.ToString(),
        PassportStatus = p.PassportStatus?.ToString(),
        EnglishTestType = (int?)p.EnglishTestType,
        EnglishTestScore = p.EnglishTestScore,
    };
}
