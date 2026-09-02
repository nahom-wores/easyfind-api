using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Enum;
using EasyFind.Api.Models.Listings;
using EasyFind.Api.Models.Subscriptions;
using EasyFind.Api.Services;

namespace EasyFind.Api.Features.Listings;

// Every Listing -> DTO conversion lives here, so a new field is added in one
// place instead of being copy-pasted into each handler.
//
// These are plain extension methods on purpose: you can F12 into them, step
// through them in the debugger, and see exactly which field went where.
public static class ListingMapper
{
    // Copies the editable fields of a create/update request onto an entity.
    // Shared by CreateListingHandler and UpdateListingHandler so the two can
    // never drift apart.
    //
    // Deliberately does NOT touch Id, IsActive, DeletedAt, CreatedAt or
    // ImageUrl — those are lifecycle fields owned by their own handlers
    // (ImageUrl is set by the image upload endpoint).
    public static void ApplyTo(this CreateListingDto dto, Listing listing)
    {
        listing.Type = dto.Type;
        listing.Title = dto.Title;
        listing.TitleAm = dto.TitleAm;
        listing.Organization = dto.Organization;
        listing.CountryCode = dto.CountryCode.ToUpperInvariant();
        listing.Description = dto.Description;
        listing.DescriptionAm = dto.DescriptionAm;
        listing.ApplyUrl = dto.ApplyUrl;
        listing.Deadline = dto.Deadline;
        listing.IsFeatured = dto.IsFeatured;
        listing.Source = dto.Source;

        // Job-only fields
        listing.JobCategory = dto.JobCategory;
        listing.SalaryMin = dto.SalaryMin;
        listing.SalaryMax = dto.SalaryMax;
        listing.EmploymentType = dto.EmploymentType;
        listing.MinExperienceYears = dto.MinExperienceYears;

        // Scholarship-only fields
        listing.ScholarshipField = dto.ScholarshipField;
        listing.DegreeLevel = dto.DegreeLevel;
        listing.FundingType = dto.FundingType;

        // NOTE: dto.SalaryPeriod and dto.SalaryCurrency are accepted by the API
        // but were never copied onto the entity. Left as-is to avoid changing
        // behaviour in this refactor — see the handover notes.
    }

    // The admin view: everything, ungated.
    public static AdminListingDto ToAdminDto(this Listing l) => new()
    {
        Id = l.Id,
        Type = l.Type.ToString(),
        Title = l.Title,
        TitleAm = l.TitleAm,
        Organization = l.Organization,
        CountryCode = l.CountryCode,
        Description = l.Description,
        DescriptionAm = l.DescriptionAm,
        ApplyUrl = l.ApplyUrl,
        Deadline = l.Deadline,
        IsActive = l.IsActive,
        IsFeatured = l.IsFeatured,
        Source = l.Source,
        JobCategory = (int?)l.JobCategory,
        SalaryMin = l.SalaryMin,
        SalaryMax = l.SalaryMax,
        EmploymentType = (int?)l.EmploymentType,
        MinExperienceYears = l.MinExperienceYears,
        ScholarshipField = (int?)l.ScholarshipField,
        DegreeLevel = (int?)l.DegreeLevel,
        FundingType = (int?)l.FundingType,
        CreatedAt = l.CreatedAt,
        UpdatedAt = l.UpdatedAt,
        SalaryPeriod = (int?)l.SalaryPeriod,
        SalaryCurrency = (int?)l.SalaryCurrency,
        ImageUrl = l.ImageUrl
    };

    // The consumer detail view. Organization and ApplyUrl are the paywalled
    // fields — free users get null and IsLocked = true.
    public static ListingDetailDto ToDetailDto(this Listing l, SubscriptionGate gate, SubscriptionTier tier) => new()
    {
        Id = l.Id,
        Type = l.Type.ToString(),
        Title = l.Title,
        TitleAm = l.TitleAm,

        // ── Gated fields: only real values for paid users ──
        Organization = gate.GateOrganization(l.Organization, tier),
        ApplyUrl = gate.GateApplyUrl(l.ApplyUrl, tier),
        IsLocked = !gate.IsPaid(tier),

        CountryCode = l.CountryCode,
        Description = l.Description,
        DescriptionAm = l.DescriptionAm,
        Deadline = l.Deadline,
        IsActive = l.IsActive,
        IsFeatured = l.IsFeatured,
        Source = l.Source,
        JobCategory = (int?)l.JobCategory,
        SalaryMin = l.SalaryMin,
        SalaryMax = l.SalaryMax,
        EmploymentType = (int?)l.EmploymentType,
        MinExperienceYears = l.MinExperienceYears,
        ScholarshipField = (int?)l.ScholarshipField,
        DegreeLevel = (int?)l.DegreeLevel,
        FundingType = (int?)l.FundingType,
        CreatedAt = l.CreatedAt,
        UpdatedAt = l.UpdatedAt,
        SalaryPeriod = (int?)l.SalaryPeriod,
        SalaryCurrency = (int?)l.SalaryCurrency,
        ImageUrl = l.ImageUrl
    };

    // The half of a feed item that is the same for every user, so it can be cached.
    // Per-user flags (bookmarked, application status) are added later, never cached.
    public static CachedFeedItem ToCachedFeedItem(this Listing l, int relevanceScore) => new()
    {
        Id = l.Id,
        Type = l.Type.ToString(),
        Title = l.Title,
        TitleAm = l.TitleAm,
        Organization = l.Organization,
        CountryCode = l.CountryCode,
        Category = l.Type == ListingType.Job ? (int?)l.JobCategory : (int?)l.ScholarshipField,
        Deadline = l.Deadline,
        IsFeatured = l.IsFeatured,
        RelevanceScore = relevanceScore,
        CreatedAt = l.CreatedAt,
        ImageUrl = l.ImageUrl
    };

    // Cached ranking + this user's fresh flags = what the client actually receives.
    public static ListingFeedItemDto ToFeedItemDto(
        this CachedFeedItem c,
        SubscriptionGate gate,
        SubscriptionTier tier,
        bool isBookmarked,
        string? applicationStatus) => new()
    {
        Id = c.Id,
        Type = c.Type,
        Title = c.Title,
        TitleAm = c.TitleAm,
        Organization = gate.GateOrganization(c.Organization, tier),
        ApplyUrl = gate.GateApplyUrl(c.ApplyUrl, tier),
        CountryCode = c.CountryCode,
        Category = c.Category,
        Deadline = c.Deadline,
        IsFeatured = c.IsFeatured,
        RelevanceScore = c.RelevanceScore,
        CreatedAt = c.CreatedAt,
        ImageUrl = c.ImageUrl,
        IsLocked = !gate.IsPaid(tier),
        IsBookmarked = isBookmarked,
        ApplicationStatus = applicationStatus
    };
}
