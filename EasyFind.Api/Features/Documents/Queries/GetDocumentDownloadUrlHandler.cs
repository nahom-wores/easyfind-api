using EasyFind.Api.Data;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Documents;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Documents.Queries;

public sealed record GetDocumentDownloadUrlQuery(string UserId, Guid DocumentId);

// Hands back a time-limited URL rather than the file itself — documents are
// private, so the storage key never leaves the server.
public class GetDocumentDownloadUrlHandler(
    ApplicationDbContext db,
    IStorageService storage)
{
    public async Task<Result<DocumentDtos.DocumentWithUrlDto>> HandleAsync(GetDocumentDownloadUrlQuery query, CancellationToken ct = default)
    {
        var (userId, documentId) = query;
        // Ownership enforced in the query — a wrong user gets NotFound, not the file.
        var doc = await db.UserDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId, ct);

        if (doc is null) return Result<DocumentDtos.DocumentWithUrlDto>.NotFound("Document not found.");

        var url = await storage.GetAccessUrlAsync(doc.StorageKey, ct);

        return Result<DocumentDtos.DocumentWithUrlDto>.Success(new DocumentDtos.DocumentWithUrlDto
        {
            Id = doc.Id,
            Type = doc.Type.ToString(),
            FileName = doc.FileName,
            FileSizeBytes = doc.FileSizeBytes,
            UploadedAt = doc.UploadedAt,
            AccessUrl = url
        });
    }
}
