using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Listings;

namespace EasyFind.Api.Features.Applications;

// A tracker entry always renders together with its listing, so the mapping
// takes both.
public static class ApplicationMapper
{
    public static ApplicationItemDto ToItemDto(this UserApplication a, Listing l) => new()
    {
        Id = a.Id,
        ListingId = a.ListingId,
        ListingTitle = l.Title,
        Organization = l.Organization,
        CountryCode = l.CountryCode,
        Status = a.Status.ToString(),
        Notes = a.Notes,
        AppliedAt = a.AppliedAt,
        Deadline = l.Deadline,
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt
    };
}
