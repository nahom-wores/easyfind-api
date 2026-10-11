using System.Text.Json;
using EasyFind.Api.Features.Listings;
using EasyFind.Api.Features.Listings.Queries;
using EasyFind.Api.Features.Profile.Queries;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Dto.Profile;
using EasyFind.Api.Models.Enum;
using EasyFind.Api.Models.Users;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Services.Assistant.Tools;

// "Find me scholarships that suit me": the user's own feed, plus the profile it
// was ranked against, so the model can explain each fit.
//
// WHOSE feed: always the signed-in caller (ICurrentUser). The tool has no user
// parameter, and anything extra the model sends is ignored, so it cannot be
// talked into ranking or revealing someone else's profile.
//
// The ranking, cache, free-tier cap and paywall are all GetFeedHandler's — the
// assistant recommends exactly what the feed would show.
public class RecommendListingsTool(
    GetFeedHandler feed,
    GetProfileHandler profiles,
    ListingAuthorizationService listingAccess,
    ICurrentUser currentUser) : IAssistantTool
{
    private const int MaxResults = 5;

    public ToolDefinition Definition { get; } = new(
        Name: "recommend_listings",
        Description: "Recommends listings that fit the signed-in user, ranked by their profile " +
                     "(target countries, preferred fields, degree level). Returns their profile and " +
                     "at most 5 results. Use it when the user asks what suits or fits them.",
        Parameters: new
        {
            type = "object",
            properties = new
            {
                type = new { type = "string", @enum = new[] { "job", "scholarship" }, description = "Listing type, only if the user specified one." },
                countryCode = new { type = "string", description = "Two-letter ISO country code, only if the user named a country." }
            }
        });

    public async Task<object> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId)
            return new { error = "No signed-in user." };

        var request = new FeedRequestDto
        {
            Type = ToolFormat.GetString(args, "type")?.ToLowerInvariant() switch
            {
                "job" => ListingType.Job,
                "scholarship" => ListingType.Scholarship,
                _ => null
            },
            CountryCode = ToolFormat.GetString(args, "countryCode")?.Trim().ToUpperInvariant() is { Length: 2 } c ? c : null,
            Page = 1,
            PageSize = MaxResults,
        };

        var profileResult = await profiles.HandleAsync(new GetProfileQuery(userId), ct);
        var profile = profileResult.IsSuccess ? profileResult.Value : null;

        var page = await feed.HandleAsync(new GetFeedQuery(userId, request), ct);
        var items = page.IsSuccess ? page.Value.Items : [];

        // The feed item has no degree, funding or description, and the model
        // needs them to explain a fit. One extra query, public view only.
        var ids = items.Select(i => i.Id).ToList();
        var extras = await listingAccess.PublishedListings()
            .AsNoTracking()
            .Where(l => ids.Contains(l.Id))
            .Select(l => new { l.Id, l.DegreeLevel, l.FundingType, l.EmploymentType, l.Description })
            .ToDictionaryAsync(l => l.Id, ct);

        var listings = items.Select(i =>
        {
            extras.TryGetValue(i.Id, out var x);
            return new
            {
                id = i.Id,
                title = i.Title,
                type = i.Type,
                countryCode = i.CountryCode,
                category = i.Type == nameof(ListingType.Job)
                    ? ToolFormat.EnumName<JobCategory>(i.Category)
                    : ToolFormat.EnumName<ScholarshipField>(i.Category),
                degreeLevel = ToolFormat.EnumName<DegreeLevel>((int?)x?.DegreeLevel),
                fundingType = ToolFormat.EnumName<FundingType>((int?)x?.FundingType),
                employmentType = ToolFormat.EnumName<EmploymentType>((int?)x?.EmploymentType),
                deadline = i.Deadline,
                organization = i.Organization,   // null for free users
                locked = i.IsLocked,
                snippet = ToolFormat.Shorten(x?.Description, ToolFormat.SnippetChars),
            };
        }).ToList();

        return new
        {
            profile = profile is null
                ? new { missing = true, message = "The user has not completed their profile, so results are not personalized." }
                : Describe(profile),
            count = listings.Count,
            note = TypeExcludedByProfile(request.Type, profile),
            listings,
        };
    }

    // The feed's ?type= narrows on top of the profile's SeekingType, so a
    // jobs-only profile asking for scholarships always gets nothing. Without
    // the reason, the model can only say "none found" — which reads as "there
    // are no scholarships" when there may be plenty.
    private static string? TypeExcludedByProfile(ListingType? requested, ProfileResponseDto? profile)
    {
        if (requested is not { } type || profile is null) return null;
        if (!Enum.TryParse<SeekingType>(profile.SeekingType, out var seeking) || seeking == SeekingType.Both) return null;
        if ((seeking, type) is (SeekingType.Job, ListingType.Job) or (SeekingType.Scholarship, ListingType.Scholarship))
            return null;

        var wanted = type == ListingType.Job ? "jobs" : "scholarships";
        var setTo = seeking == SeekingType.Job ? "jobs" : "scholarships";
        return $"No {wanted} are shown because the user's profile is set to {setTo} only. " +
               $"They can change what they are looking for in their profile settings to see {wanted}.";
    }

    // Only the fields ranking uses, plus education and English for context.
    // Name, birth date, sex and passport status stay out: the model doesn't
    // need them, and they'd be sent to a third party on every round.
    private static object Describe(ProfileResponseDto p)
    {
        return new
        {
            seeking = p.SeekingType,
            targetCountries = p.TargetCountries,
            preferredJobCategories = p.PreferredJobCategories.Select(c => ToolFormat.EnumName<JobCategory>(c)).OfType<string>(),
            preferredScholarshipFields = p.PreferredScholarshipFields.Select(f => ToolFormat.EnumName<ScholarshipField>(f)).OfType<string>(),
            targetDegreeLevel = ToolFormat.EnumName<DegreeLevel>(p.TargetDegreeLevel),
            educationLevel = p.EducationLevel,
            englishLevel = p.EnglishLevel,
        };
    }
}
