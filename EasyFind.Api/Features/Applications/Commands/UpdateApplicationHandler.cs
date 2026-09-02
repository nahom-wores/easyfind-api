using EasyFind.Api.Data;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Listings;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Applications.Commands;

// Move a tracker entry along (status / notes). Ownership is in the query.
public class UpdateApplicationHandler(ApplicationDbContext db)
{
    public async Task<Result> HandleAsync(
        string userId, Guid applicationId, UpdateApplicationDto dto, CancellationToken ct = default)
    {
        var entry = await db.UserApplications
            .FirstOrDefaultAsync(a => a.Id == applicationId && a.UserId == userId, ct);
        if (entry is null) return Result.NotFound("Application not found.");

        // First time it reaches "Applied", record when.
        if (entry.AppliedAt == null && dto.Status >= ApplicationTrackStatus.Applied)
            entry.AppliedAt = DateTimeOffset.UtcNow;

        entry.Status = dto.Status;
        entry.Notes = dto.Notes;
        entry.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
