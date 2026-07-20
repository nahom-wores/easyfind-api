using EasyFind.Api.Models.Enum;

namespace EasyFind.Api.Models.Dto.Listings;


public class AdminListingDto
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
public class CreateListingDto
{
    public ListingType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? TitleAm { get; set; }
    public string Organization { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? DescriptionAm { get; set; }
    public string ApplyUrl { get; set; } = string.Empty;
    public DateOnly? Deadline { get; set; }
    public bool IsFeatured { get; set; }
    public string? Source { get; set; }

    // Job-only
    public JobCategory? JobCategory { get; set; }
    public int? SalaryMin { get; set; }
    public int? SalaryMax { get; set; }
    public EmploymentType? EmploymentType { get; set; }
    public int? MinExperienceYears { get; set; }
    public SalaryPeriod? SalaryPeriod { get; set; }
    public Currency? SalaryCurrency { get; set; }
    // Scholarship-only
    public ScholarshipField? ScholarshipField { get; set; }
    public DegreeLevel? DegreeLevel { get; set; }
    public FundingType? FundingType { get; set; }
}

// Update is the same shape — admin sends the full corrected listing
public class UpdateListingDto : CreateListingDto
{
}

