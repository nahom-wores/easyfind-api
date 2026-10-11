using System.Text.Json;
using EasyFind.Api.Features.Listings.Queries;
using EasyFind.Api.Models.Enum;
using EasyFind.Api.Services.IServices;

namespace EasyFind.Api.Services.Assistant.Tools;

// One listing's full details, for "tell me more about X".
//
// Goes through GetListingDetailHandler — the same path as GET /listings/{id} —
// so the paywall holds: a free user gets no organization or apply link here
// either. The user is the signed-in caller (ICurrentUser), never anything the
// model sends.
public class GetListingDetailsTool(GetListingDetailHandler handler, ICurrentUser currentUser) : IAssistantTool
{
    public ToolDefinition Definition { get; } = new(
        Name: "get_listing_details",
        Description: "Gets the full details of one listing, to summarize it. " +
                     "Needs a listing id from search_listings or recommend_listings — if you only " +
                     "know the title, call search_listings with it first.",
        Parameters: new
        {
            type = "object",
            properties = new
            {
                listingId = new { type = "string", description = "The listing's id, exactly as a search or recommendation returned it." }
            },
            required = new[] { "listingId" }
        });

    public async Task<object> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        if (!Guid.TryParse(ToolFormat.GetString(args, "listingId"), out var listingId))
            return new { error = "listingId must be an id returned by search_listings or recommend_listings." };

        if (currentUser.UserId is not { } userId)
            return new { error = "No signed-in user." };

        var result = await handler.HandleAsync(new GetListingDetailQuery(listingId, userId), ct);
        if (!result.IsSuccess)
            return new { found = false, message = "No open listing has that id. It may have closed." };

        var l = result.Value;
        return new
        {
            found = true,
            id = l.Id,
            type = l.Type,
            title = l.Title,
            countryCode = l.CountryCode,
            deadline = l.Deadline,

            // Null for free users — see SubscriptionGate.
            organization = l.Organization,
            applyUrl = l.ApplyUrl,
            locked = l.IsLocked,

            description = ToolFormat.Shorten(l.Description, ToolFormat.DetailDescriptionChars),

            // Jobs
            jobCategory = ToolFormat.EnumName<JobCategory>(l.JobCategory),
            employmentType = ToolFormat.EnumName<EmploymentType>(l.EmploymentType),
            minExperienceYears = l.MinExperienceYears,
            salaryMin = l.SalaryMin,
            salaryMax = l.SalaryMax,
            salaryPeriod = ToolFormat.EnumName<SalaryPeriod>(l.SalaryPeriod),
            salaryCurrency = ToolFormat.EnumName<Currency>(l.SalaryCurrency),

            // Scholarships
            scholarshipField = ToolFormat.EnumName<ScholarshipField>(l.ScholarshipField),
            degreeLevel = ToolFormat.EnumName<DegreeLevel>(l.DegreeLevel),
            fundingType = ToolFormat.EnumName<FundingType>(l.FundingType),
        };
    }
}
