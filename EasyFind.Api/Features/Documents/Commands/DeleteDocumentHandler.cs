using EasyFind.Api.Data;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Users;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Documents.Commands;

// Delete one of the user's own documents. Ownership is in the query.
public class DeleteDocumentHandler(
    ApplicationDbContext db,
    IStorageService storage,
    ILogger<DeleteDocumentHandler> logger)
{
    public async Task<Result> HandleAsync(string userId, Guid documentId, CancellationToken ct = default)
    {
        var doc = await db.UserDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId, ct);
        if (doc is null) return Result.NotFound("Document not found.");

        // Storage first, then the record. A failed storage delete is logged but
        // not fatal: an orphaned blob is better than a row pointing at nothing.
        try { await storage.DeleteAsync(doc.StorageKey, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Storage delete failed for {Key}", doc.StorageKey); }

        db.UserDocuments.Remove(doc);

        // If this was the CV, clear the profile's pointer to it.
        if (doc.Type == DocumentType.Cv)
        {
            var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
            if (profile != null && profile.CvFileUrl == doc.StorageKey)
            {
                profile.CvFileUrl = null;
                profile.CvUploadedAt = null;
            }
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
