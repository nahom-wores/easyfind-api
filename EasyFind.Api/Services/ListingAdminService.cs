using EasyFind.Api.Data;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Listings;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Services;

public class ListingAdminService(ApplicationDbContext db, IRedisCacheService cache) : IListingAdminService
{
    public async Task<Result<AdminListingDto>> CreateAsync(CreateListingDto dto, CancellationToken ct = default)
    {
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

    public async Task<Result<AdminListingDto>> UpdateAsync(Guid id, UpdateListingDto dto, CancellationToken ct = default)
    {
        var listing = await db.Listings
            .IgnoreQueryFilters()   // admin can edit even soft-deleted
            .FirstOrDefaultAsync(l => l.Id == id, ct);
        if (listing == null) return Result<AdminListingDto>.NotFound("Listing not found.");

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
        listing.JobCategory = dto.JobCategory;
        listing.SalaryMin = dto.SalaryMin;
        listing.SalaryMax = dto.SalaryMax;
        listing.EmploymentType = dto.EmploymentType;
        listing.MinExperienceYears = dto.MinExperienceYears;
        listing.ScholarshipField = dto.ScholarshipField;
        listing.DegreeLevel = dto.DegreeLevel;
        listing.FundingType = dto.FundingType;
        listing.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        await cache.RemoveByPatternAsync("feed:*");

        return Result<AdminListingDto>.Success(Map(listing));
    }

    public async Task<Result> SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var listing = await db.Listings.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (listing == null) return Result.NotFound("Listing not found.");

        listing.DeletedAt = DateTimeOffset.UtcNow;
        listing.IsActive = false;
        await db.SaveChangesAsync(ct);
        await cache.RemoveByPatternAsync("feed:*");
        return Result.Success();
    }

    public async Task<Result> RestoreAsync(Guid id, CancellationToken ct = default)
    {
        var listing = await db.Listings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Id == id, ct);
        if (listing == null) return Result.NotFound("Listing not found.");

        listing.DeletedAt = null;
        await db.SaveChangesAsync(ct);
        await cache.RemoveByPatternAsync("feed:*");
        return Result.Success();
    }

    public async Task<Result> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        var listing = await db.Listings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Id == id, ct);
        if (listing == null) return Result.NotFound("Listing not found.");

        listing.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        await cache.RemoveByPatternAsync("feed:*");
        return Result.Success();
    }

    public async Task<Result<AdminListingDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var listing = await db.Listings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, ct);
        if (listing == null) return Result<AdminListingDto>.NotFound("Listing not found.");
        return Result<AdminListingDto>.Success(Map(listing));
    }

    public async Task<PagedResult<AdminListingDto>> GetAllAsync(AdminListingFilterDto filter, CancellationToken ct = default)
    {
        // Start from base query; optionally include soft-deleted
        var query = filter.IncludeDeleted
            ? db.Listings.IgnoreQueryFilters().AsNoTracking()
            : db.Listings.AsNoTracking();

        if (filter.Type.HasValue)
            query = query.Where(l => l.Type == filter.Type.Value);

        if (filter.IsActive.HasValue)
            query = query.Where(l => l.IsActive == filter.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(filter.CountryCode))
        {
            var cc = filter.CountryCode.ToUpperInvariant();
            query = query.Where(l => l.CountryCode == cc);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(l =>
                EF.Functions.ILike(l.Title, $"%{term}%") ||
                EF.Functions.ILike(l.Organization, $"%{term}%"));
        }

        query = query.OrderByDescending(l => l.CreatedAt);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(l => Map(l))
            .ToListAsync(ct);

        return new PagedResult<AdminListingDto>
        {
            Items = items, TotalCount = totalCount, Page = filter.Page, PageSize = filter.PageSize
        };
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
    };
}