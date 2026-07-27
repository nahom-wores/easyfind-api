namespace EasyFind.Api.Models.Dto.Listings;

public class ListingDetailDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? TitleAm { get; set; }
    public string Organization { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? DescriptionAm { get; set; }
    public string ApplyUrl { get; set; } = string.Empty;
    public DateOnly? Deadline { get; set; }
    public bool IsActive { get; set; }
    public bool IsFeatured { get; set; }
    public string? Source { get; set; }
    public int? JobCategory { get; set; }
    public int? SalaryMin { get; set; }
    public int? SalaryMax { get; set; }
    public int? EmploymentType { get; set; }
    public int? MinExperienceYears { get; set; }
    public int? ScholarshipField { get; set; }
    public int? DegreeLevel { get; set; }
    public int? FundingType { get; set; }
    public int? SalaryPeriod { get; set; }
    public int? SalaryCurrency { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}