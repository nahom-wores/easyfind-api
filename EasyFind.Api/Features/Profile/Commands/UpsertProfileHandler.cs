using EasyFind.Api.Data;
using EasyFind.Api.Extensions;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Profile;
using EasyFind.Api.Models.Users;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Profile.Commands;

public sealed record UpsertProfileCommand(string UserId, OnboardingDto Profile);

// Onboarding, and every later edit of it. Creates the profile on first call and
// updates it thereafter — the client uses one endpoint for both.
public class UpsertProfileHandler(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IRedisCacheService cache)
{
    public async Task<Result<ProfileResponseDto>> HandleAsync(UpsertProfileCommand command, CancellationToken ct = default)
    {
        var (userId, dto) = command;
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return Result<ProfileResponseDto>.NotFound("User not found.");

        // 1. Name lives on the Identity user, not the profile
        user.FirstName = dto.FirstName;
        user.LastName = dto.LastName;
        var userUpdate = await userManager.UpdateAsync(user);
        if (!userUpdate.Succeeded)
            return Result<ProfileResponseDto>.Validation(
                string.Join(", ", userUpdate.Errors.Select(e => e.Description)));

        // 2. Create or update the profile itself
        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null)
        {
            profile = new UserProfile { UserId = userId };
            db.UserProfiles.Add(profile);
        }

        profile.SeekingType = dto.SeekingType;
        profile.TargetCountries = dto.TargetCountries
            .Select(c => c.ToUpperInvariant()).ToList();
        profile.PreferredJobCategories = dto.PreferredJobCategories;
        profile.PreferredScholarshipFields = dto.PreferredScholarshipFields;
        profile.TargetDegreeLevel = dto.TargetDegreeLevel;
        profile.EducationLevel = dto.EducationLevel;
        profile.WorkExperienceYears = dto.WorkExperienceYears;
        profile.ExperienceRange = dto.ExperienceRange;
        profile.EnglishLevel = dto.EnglishLevel;
        profile.EnglishTestType = dto.EnglishTestType;
        profile.EnglishTestScore = dto.EnglishTestScore;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        profile.DateOfBirth = dto.DateOfBirth;
        profile.Sex = dto.Sex;
        profile.PassportStatus = dto.PassportStatus;

        await db.SaveChangesAsync(ct);

        // 3. These fields ARE the feed's ranking inputs, so this user's cached
        //    pages are now wrong. Only theirs — everyone else's stay valid.
        await cache.InvalidateFeedsForUserAsync(userId);

        return Result<ProfileResponseDto>.Success(profile.ToResponseDto(user));
    }
}
