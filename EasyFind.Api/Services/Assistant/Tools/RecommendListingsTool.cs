using System.Text.Json;
using EasyFind.Api.Features.Listings;
using EasyFind.Api.Features.Listings.Queries;
using EasyFind.Api.Features.Profile.Queries;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Enum;
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

        var profile = await ProfileAsync(userId, ct);

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
            profile = (object?)profile ?? new { missing = true, message = "The user has not completed their profile, so results are not personalized." },
            count = listings.Count,
            listings,
        };
    }

    // Only the fields ranking uses, plus education and English for context.
    // Name, birth date, sex and passport status stay out: the model doesn't
    // need them, and they'd be sent to a third party on every round.
    private async Task<object?> ProfileAsync(string userId, CancellationToken ct)
    {
        var result = await profiles.HandleAsync(new GetProfileQuery(userId), ct);
        if (!result.IsSuccess) return null;

        var p = result.Value;
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
