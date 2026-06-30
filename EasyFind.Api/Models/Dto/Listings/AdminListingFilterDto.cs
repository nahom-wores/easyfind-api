using EasyFind.Api.Models.Enum;

namespace EasyFind.Api.Models.Dto.Listings;

public class AdminListingFilterDto
{
    public ListingType? Type { get; set; }
    public bool? IsActive { get; set; }
    public string? CountryCode { get; set; }
    public bool IncludeDeleted { get; set; } = false;
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}