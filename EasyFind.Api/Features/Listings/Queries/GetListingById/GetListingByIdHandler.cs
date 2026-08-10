using EasyFind.Api.Data;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Listings;
using EasyFind.Api.Models.Subscriptions;
using EasyFind.Api.Services;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Listings.Queries.GetListingById;

public class GetListingByIdHandler(ApplicationDbContext db,
    UserManager<ApplicationUser> userManager, SubscriptionGate gate) 
{
    public async Task<Result<ListingDetailDto>> FetchAsync(
        Guid listingId, string userId, CancellationToken ct = default)
    {
        var listing = await db.Listings
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == listingId && l.IsActive, ct);

        if (listing is null)
            return Result<ListingDetailDto>.NotFound("Listing not found.");

        // Subscription gate — free users don't get the company name or apply link
        var user = await userManager.FindByIdAsync(userId);
        var tier = user?.SubscriptionTier ?? SubscriptionTier.Free;

        return Result<ListingDetailDto>.Success(Map(listing, tier));
    }
    private ListingDetailDto Map(Listing l, SubscriptionTier tier) => new()
    {
        Id = l.Id,
        Type = l.Type.ToString(),
        Title = l.Title,
        TitleAm = l.TitleAm,

        // ── Gated fields: only real values for paid users ──
        Organization = gate.GateOrganization(l.Organization, tier),
        ApplyUrl     = gate.GateApplyUrl(l.ApplyUrl, tier),
        IsLocked     = !gate.IsPaid(tier),

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
}