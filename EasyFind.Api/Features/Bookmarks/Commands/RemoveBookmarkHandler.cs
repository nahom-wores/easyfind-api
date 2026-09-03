using EasyFind.Api.Data;
using EasyFind.Api.Models.Dto.Common;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Bookmarks.Commands;

public sealed record RemoveBookmarkCommand(string UserId, Guid ListingId);

// Un-save a listing. Ownership is enforced by the query itself.
public class RemoveBookmarkHandler(ApplicationDbContext db)
{
    public async Task<Result> HandleAsync(RemoveBookmarkCommand command, CancellationToken ct = default)
    {
        var (userId, listingId) = command;
        var bookmark = await db.Bookmarks
            .FirstOrDefaultAsync(b => b.UserId == userId && b.ListingId == listingId, ct);

        if (bookmark is null) return Result.NotFound("Bookmark not found");

        db.Bookmarks.Remove(bookmark);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
