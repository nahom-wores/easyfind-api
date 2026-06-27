using System.ComponentModel.DataAnnotations;
using EasyFind.Api.Models.Enum;

namespace EasyFind.Api.Models.Listings;

public class Listing
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public ListingType Type { get; set; }

    // ── Shared fields ───────────────────────────────
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;
    [MaxLength(255)]
    public string? TitleAm { get; set; }

    [MaxLength(255)]
    public string Organization { get; set; } = string.Empty;

    [MaxLength(5)]
    public string CountryCode { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
    public string? DescriptionAm { get; set; }

    public string ApplyUrl { get; set; } = string.Empty;

    public DateOnly? Deadline { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; } = false;

    [MaxLength(100)]
    public string? Source { get; set; } 

    // ── Job-specific (null when Type = Scholarship) ──
    public JobCategory? JobCategory { get; set; }
    public int? SalaryMin { get; set; }            // USD/year
    public int? SalaryMax { get; set; }
    public EmploymentType? EmploymentType { get; set; } // part-time | full-time
    public int? MinExperienceYears { get; set; }

    // ── Scholarship-specific (null when Type = Job) ─
    public ScholarshipField? ScholarshipField { get; set; }
    public DegreeLevel? DegreeLevel { get; set; }
    public FundingType? FundingType { get; set; }

    // ── Timestamps ──────────────────────────────────
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; } // soft delete
}

