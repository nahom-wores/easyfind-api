using EasyFind.Api.Data;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Profile;
using EasyFind.Api.Models.Users;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Services;

public class ProfileService(ApplicationDbContext db,
    UserManager<ApplicationUser> userManager, IRedisCacheService cache) : IProfileService
{
    public async Task<Result<ProfileResponseDto>> UpsertAsync(string userId,
        OnboardingDto dto, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null) return Result<ProfileResponseDto>.NotFound("User not found.");

        // 1. Write name to the Identity user
        user.FirstName = dto.FirstName;
        user.LastName = dto.LastName;
        var userUpdate = await userManager.UpdateAsync(user);
        if (!userUpdate.Succeeded)
            return Result<ProfileResponseDto>.Validation(
                string.Join(", ", userUpdate.Errors.Select(e => e.Description)));

        // 2. Create or update the profile
        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var isNew = profile == null;

        if (isNew)
        {
            profile = new UserProfile { UserId = userId };
            db.UserProfiles.Add(profile);
        }

        profile!.SeekingType = dto.SeekingType;
        profile.TargetCountries = dto.TargetCountries
            .Select(c => c.ToUpperInvariant()).ToList();
        profile.PreferredJobCategories = dto.PreferredJobCategories;
        profile.PreferredScholarshipFields = dto.PreferredScholarshipFields;
        profile.TargetDegreeLevel = dto.TargetDegreeLevel;
        profile.EducationLevel = dto.EducationLevel;
        profile.WorkExperienceYears = dto.WorkExperienceYears;
        profile.ExperienceRange = dto.ExperienceRange;
        profile.EnglishLevel = dto.EnglishLevel;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        profile.DateOfBirth = dto.DateOfBirth;
        profile.Sex = dto.Sex;
        profile.PassportStatus = dto.PassportStatus;
        
        await db.SaveChangesAsync(ct);

        // 3. Their scoring inputs changed — invalidate any cached feed
        await cache.RemoveByPatternAsync($"feed:{userId}:*");

        return Result<ProfileResponseDto>.Success(Map(user, profile));
    }

    public async Task<Result<ProfileResponseDto>> GetAsync(string userId,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null) return Result<ProfileResponseDto>.NotFound("User not found.");

        var profile = await db.UserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (profile == null)
            return Result<ProfileResponseDto>.NotFound("Profile not yet created.");

        return Result<ProfileResponseDto>.Success(Map(user, profile));
    }
    private static ProfileResponseDto Map(ApplicationUser u, UserProfile p) => new()
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
    };
}