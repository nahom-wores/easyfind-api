using EasyFind.Api.Data;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Listings;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Services;

public class ApplicationService(ApplicationDbContext db) : IApplicationService
{
    private readonly ApplicationDbContext _db = db;

    public async Task<Result<ApplicationItemDto>> CreateAsync(
        string userId, CreateApplicationDto dto, CancellationToken ct = default)
    {
        var listing = await _db.Listings
            .FirstOrDefaultAsync(l => l.Id == dto.ListingId && l.IsActive, ct);
        if (listing == null)
            return Result<ApplicationItemDto>.NotFound("Listing not found.");

        var already = await _db.UserApplications
            .AnyAsync(a => a.UserId == userId && a.ListingId == dto.ListingId, ct);
        if (already)
            return Result<ApplicationItemDto>.Conflict("Already in your tracker.");

        var entry = new UserApplication
        {
            UserId = userId,
            ListingId = dto.ListingId,
            Status = dto.Status,
            Notes = dto.Notes,
            AppliedAt = dto.Status >= ApplicationTrackStatus.Applied ? DateTimeOffset.UtcNow : null,
        };

        _db.UserApplications.Add(entry);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Result<ApplicationItemDto>.Conflict("Already in your tracker.");
        }

        return Result<ApplicationItemDto>.Success(Map(entry, listing));
    }


    public async Task<Result> UpdateAsync(
        string userId, Guid applicationId, UpdateApplicationDto dto, CancellationToken ct = default)
    {
        var entry = await _db.UserApplications
            .FirstOrDefaultAsync(a => a.Id == applicationId && a.UserId == userId, ct);
        if (entry == null) return Result.NotFound("Application not found.");

        if (entry.AppliedAt == null && dto.Status >= ApplicationTrackStatus.Applied)
            entry.AppliedAt = DateTimeOffset.UtcNow;

        entry.Status = dto.Status;
        entry.Notes = dto.Notes;
        entry.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(
        string userId, Guid applicationId, CancellationToken ct = default)
    {
        var entry = await _db.UserApplications
            .FirstOrDefaultAsync(a => a.Id == applicationId && a.UserId == userId, ct);
        if (entry == null) return Result.NotFound("Application not found.");

        _db.UserApplications.Remove(entry);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<PagedResult<ApplicationItemDto>> GetUserApplicationsAsync(string userId,
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.UserApplications
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.UpdatedAt)
            .Select(a => new ApplicationItemDto
            {
                Id = a.Id,
                ListingId = a.ListingId,
                ListingTitle = a.Listing.Title,
                Organization = a.Listing.Organization,
                CountryCode = a.Listing.CountryCode,
                Status = a.Status.ToString(),
                Notes = a.Notes,
                AppliedAt = a.AppliedAt,
                Deadline = a.Listing.Deadline,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt
            });

        var totalCount = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new PagedResult<ApplicationItemDto>
        {
            Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize
        };
    }
    private static ApplicationItemDto Map(UserApplication a, Listing l) => new()
    {
        Id = a.Id,
        ListingId = a.ListingId,
        ListingTitle = l.Title,
        Organization = l.Organization,
        CountryCode = l.CountryCode,
        Status = a.Status.ToString(),
        Notes = a.Notes,
        AppliedAt = a.AppliedAt,
        Deadline = l.Deadline,
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt
    };
}