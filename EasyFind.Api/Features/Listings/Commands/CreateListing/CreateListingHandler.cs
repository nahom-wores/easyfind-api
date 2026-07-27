using EasyFind.Api.Data;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Listings;
using EasyFind.Api.Services.IServices;
using MediatR;

namespace EasyFind.Api.Features.Listings.Commands.CreateListing;

public class CreateListingHandler(ApplicationDbContext db, IRedisCacheService cache) :
    IRequestHandler<CreateListingCommand, Result<AdminListingDto>>
{
    public async Task<Result<AdminListingDto>> Handle(CreateListingCommand request, CancellationToken ct)
    {
        var dto = request.CreateListingDto;
        var listing = new Listing
        {
            Type = dto.Type,
            Title = dto.Title,
            TitleAm = dto.TitleAm,
            Organization = dto.Organization,
            CountryCode = dto.CountryCode.ToUpperInvariant(),
            Description = dto.Description,
            DescriptionAm = dto.DescriptionAm,
            ApplyUrl = dto.ApplyUrl,
            Deadline = dto.Deadline,
            IsFeatured = dto.IsFeatured,
            IsActive = true,
            Source = dto.Source ?? "Manual",
            JobCategory = dto.JobCategory,
            SalaryMin = dto.SalaryMin,
            SalaryMax = dto.SalaryMax,
            EmploymentType = dto.EmploymentType,
            MinExperienceYears = dto.MinExperienceYears,
            ScholarshipField = dto.ScholarshipField,
            DegreeLevel = dto.DegreeLevel,
            FundingType = dto.FundingType,
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync(ct);
        // New listing changes everyone's feed — clear all cached feeds
        await cache.RemoveByPatternAsync("feed:*");
        return Result<AdminListingDto>.Success(Map(listing));
    }
    private static AdminListingDto Map(Listing l) => new()
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
        
    };
}