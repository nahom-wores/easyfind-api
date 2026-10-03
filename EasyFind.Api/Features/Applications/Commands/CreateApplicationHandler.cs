using EasyFind.Api.Data;
using EasyFind.Api.Features.Listings;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Listings;
using EasyFind.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Applications.Commands;

public sealed record CreateApplicationCommand(string UserId, CreateApplicationDto Application);

// Start tracking an application against a listing. Paid feature.
public class CreateApplicationHandler(
    ApplicationDbContext db,
    ListingAuthorizationService listings,
    SubscriptionGate gate)
{
    public async Task<Result<ApplicationItemDto>> HandleAsync(CreateApplicationCommand command, CancellationToken ct = default)
    {
        var (userId, dto) = command;
        var tier = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.SubscriptionTier)
            .FirstOrDefaultAsync(ct);

        if (!gate.IsPaid(tier))
            return Result<ApplicationItemDto>.Forbidden("Upgrade to a paid plan to track applications.");

        var listing = await listings.AuthorizedListings()
            .FirstOrDefaultAsync(l => l.Id == dto.ListingId && l.IsActive, ct);
        if (listing is null)
            return Result<ApplicationItemDto>.NotFound("Listing not found.");

        var already = await db.UserApplications
            .AnyAsync(a => a.UserId == userId && a.ListingId == dto.ListingId, ct);
        if (already)
            return Result<ApplicationItemDto>.Conflict("Already in your tracker.");

        var entry = new UserApplication
        {
            UserId = userId,
            ListingId = dto.ListingId,
            Status = dto.Status,
            Notes = dto.Notes,
            // Only stamp AppliedAt once the status says they actually applied
            AppliedAt = dto.Status >= ApplicationTrackStatus.Applied ? DateTimeOffset.UtcNow : null,
        };

        db.UserApplications.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Unique index (UserId, ListingId) — two requests raced.
            return Result<ApplicationItemDto>.Conflict("Already in your tracker.");
        }

        return Result<ApplicationItemDto>.Success(entry.ToItemDto(listing));
    }
}
