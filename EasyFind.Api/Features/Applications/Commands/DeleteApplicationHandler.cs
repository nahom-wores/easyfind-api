using EasyFind.Api.Data;
using EasyFind.Api.Models.Dto.Common;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Applications.Commands;

public sealed record DeleteApplicationCommand(string UserId, Guid ApplicationId);

// Drop a tracker entry. Ownership is in the query.
public class DeleteApplicationHandler(ApplicationDbContext db)
{
    public async Task<Result> HandleAsync(DeleteApplicationCommand command, CancellationToken ct = default)
    {
        var (userId, applicationId) = command;
        var entry = await db.UserApplications
            .FirstOrDefaultAsync(a => a.Id == applicationId && a.UserId == userId, ct);
        if (entry is null) return Result.NotFound("Application not found.");

        db.UserApplications.Remove(entry);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
