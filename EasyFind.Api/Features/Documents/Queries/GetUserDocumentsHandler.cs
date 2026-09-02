using EasyFind.Api.Data;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Documents;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Documents.Queries;

// Everything the user has uploaded, newest first.
public class GetUserDocumentsHandler(ApplicationDbContext db)
{
    public async Task<Result<List<DocumentDtos.DocumentResponseDto>>> HandleAsync(
        string userId, CancellationToken ct = default)
    {
        var docs = await db.UserDocuments
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => d.ToResponseDto())
            .ToListAsync(ct);

        return Result<List<DocumentDtos.DocumentResponseDto>>.Success(docs);
    }
}
