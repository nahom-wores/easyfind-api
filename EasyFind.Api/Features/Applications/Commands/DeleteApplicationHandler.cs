using EasyFind.Api.Data;
using EasyFind.Api.Models.Dto.Common;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Applications.Commands;

// Drop a tracker entry. Ownership is in the query.
public class DeleteApplicationHandler(ApplicationDbContext db)
{
    public async Task<Result> HandleAsync(
        string userId, Guid applicationId, CancellationToken ct = default)
    {
        var entry = await db.UserApplications
            .FirstOrDefaultAsync(a => a.Id == applicationId && a.UserId == userId, ct);
        if (entry is null) return Result.NotFound("Application not found.");

        db.UserApplications.Remove(entry);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
