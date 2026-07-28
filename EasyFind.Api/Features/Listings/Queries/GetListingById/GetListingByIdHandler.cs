using EasyFind.Api.Data;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Listings;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Listings.Queries.GetListingById;

public class GetListingByIdHandler(ApplicationDbContext db) : IRequestHandler<GetListingByIdQuery,  Result<ListingDetailDto>>
{
    public async Task<Result<ListingDetailDto>> Handle(GetListingByIdQuery request, CancellationToken ct)
    {
        var listing = await db.Listings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == request.Id, ct);
        if(listing is null) return Result<ListingDetailDto>.NotFound("Listing not found.");
        return Result<ListingDetailDto>.Success(Map(listing));
        
    }
    private static ListingDetailDto Map(Listing l) => new()
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
}