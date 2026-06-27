using EasyFind.Api.Models.Enum;

namespace EasyFind.Api.Models.Dto.Listings;

public class FeedRequestDto
{
    public ListingType? Type { get; set; }        // filter: jobs only, scholarships only, or both
    public string? CountryCode { get; set; }      // optional hard filter
    public int? Category { get; set; }            // optional: JobCategory or ScholarshipField as int
    public string? Search { get; set; }           // title/org keyword
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}