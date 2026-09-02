using EasyFind.Api.Data;
using EasyFind.Api.Models.Dto.Common;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Bookmarks.Commands;

// Un-save a listing. Ownership is enforced by the query itself.
public class RemoveBookmarkHandler(ApplicationDbContext db)
{
    public async Task<Result> HandleAsync(string userId, Guid listingId, CancellationToken ct = default)
    {
        var bookmark = await db.Bookmarks
            .FirstOrDefaultAsync(b => b.UserId == userId && b.ListingId == listingId, ct);

        if (bookmark is null) return Result.NotFound("Bookmark not found");

        db.Bookmarks.Remove(bookmark);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
