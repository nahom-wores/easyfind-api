using System.Text.Json;
using EasyFind.Api.Features.Listings;
using EasyFind.Api.Models.Enum;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Services.Assistant.Tools;

public class SearchListingsTool(ListingAuthorizationService listingAccess) : IAssistantTool
{
    private const int MaxResults = 5;
    public ToolDefinition Definition { get; } = new(
        Name: "search_listings",
        Description: "Searches active Yisru listings (jobs and scholarships). " +
                     "Use it whenever the user asks what jobs or scholarships are available. " +
                     "Returns at most 5 results.",
        Parameters: new
        {
            type = "object",
            properties = new
            {
                query = new { type = "string", description = "Keywords, e.g. a job title or field of study, like 'nurse' or 'computer science'." },
                type = new { type = "string", @enum = new[] { "job", "scholarship" }, description = "Listing type, only if the user specified one." },
                countryCode = new { type = "string", description = "Two-letter ISO country code, e.g. DE for Germany, CA for Canada." }
            }
        });

    public async Task<object> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        // The model's arguments are UNTRUSTED input — validate like any user input.
        var query = GetString(args, "query")?.Trim();
        var type = GetString(args, "type")?.ToLowerInvariant();
        var country = GetString(args, "countryCode")?.Trim().ToUpperInvariant();

        // Published only, whatever the caller's role — see PublishedListings.
        var listings = listingAccess.PublishedListings().AsNoTracking();

        if (type == "job")
            listings = listings.Where(l => l.Type == ListingType.Job);
        else if (type == "scholarship")
            listings = listings.Where(l => l.Type == ListingType.Scholarship);

        if (country is { Length: 2 })
            listings = listings.Where(l => l.CountryCode == country);

        if (!string.IsNullOrEmpty(query))
            listings = listings.Where(l =>
                EF.Functions.ILike(l.Title, $"%{query}%") ||
                EF.Functions.ILike(l.Organization, $"%{query}%"));

        var results = await listings
            .OrderByDescending(l => l.IsFeatured)
            .ThenByDescending(l => l.CreatedAt)
            .Take(MaxResults)
            .Select(l => new
            {
                id = l.Id,
                title = l.Title,
                type = l.Type.ToString(),
                countryCode = l.CountryCode,
                deadline = l.Deadline
            })
            .ToListAsync(ct);

        // An explicit "nothing found" makes the model far less likely to invent listings.
        return results.Count == 0
            ? new { count = 0, message = "No matching listings found." }
            : new { count = results.Count, listings = results };
    }
    private static string? GetString(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object &&
        args.TryGetProperty(name, out var v) &&
        v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}