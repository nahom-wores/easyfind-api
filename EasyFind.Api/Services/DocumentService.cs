using EasyFind.Api.Data;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Documents;
using EasyFind.Api.Models.Options;
using EasyFind.Api.Models.Users;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyFind.Api.Services;

public class DocumentService(ApplicationDbContext db, IStorageService storage,
    IOptions<DocumentUploadOptions> opts, ILogger<DocumentService> logger) : IDocumentService
{
    private readonly DocumentUploadOptions _opts = opts.Value;
    // Magic-byte signatures — verify the file IS what its extension claims.
    private static readonly byte[] PdfMagic = "%PDF"u8.ToArray();
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04]; // docx is a zip
    private static readonly byte[] DocMagic = [0xD0, 0xCF, 0x11, 0xE0]; // legacy .doc

    public async Task<Result<DocumentDtos.DocumentResponseDto>> UploadAsync(string userId, IFormFile file, DocumentType type, CancellationToken ct = default)
    {
        // 1. Empty?
            if (file.Length == 0)
                return Result<DocumentDtos.DocumentResponseDto>.Validation("File is empty.");

            // 2. Size cap
            if (file.Length > _opts.MaxFileSizeBytes)
                return Result<DocumentDtos.DocumentResponseDto>.Validation(
                    $"File exceeds the {_opts.MaxFileSizeBytes / (1024 * 1024)}MB limit.");

            // 3. Extension allow-list
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!_opts.AllowedExtensions.Contains(ext))
                return Result<DocumentDtos.DocumentResponseDto>.Validation(
                    $"File type '{ext}' not allowed. Accepted: {string.Join(", ", _opts.AllowedExtensions)}.");

            // 4. Magic-byte check — content must MATCH the claimed extension.
            //    Defends against renamed files (virus.exe -> cv.pdf).
            await using (var checkStream = file.OpenReadStream())
            {
                if (!await HasValidSignatureAsync(checkStream, ext, ct))
                    return Result<DocumentDtos.DocumentResponseDto>.Validation(
                        "File content does not match its extension.");
            }

            // 5. Upload to storage (interface — local now, S3 later)
            StoredFile stored;
            try
            {
                await using var uploadStream = file.OpenReadStream();
                stored = await storage.UploadAsync(
                    uploadStream, file.FileName, file.ContentType,
                    folder: $"documents/{userId}", ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Storage upload failed for user {UserId}", userId);
                return Result<DocumentDtos.DocumentResponseDto>.Failure("Upload failed. Please try again.", ErrorType.Failure);
            }

            // 6. Record it
            var doc = new UserDocument
            {
                UserId = userId,
                Type = type,
                FileName = file.FileName,       // original, for display
                StorageKey = stored.Key,        // our generated key
                FileSizeBytes = file.Length,
                ContentType = file.ContentType,
            };
            db.UserDocuments.Add(doc);
            await db.SaveChangesAsync(ct);

            // 7. If it's a CV, also update the profile's CvFileUrl pointer
            if (type == DocumentType.Cv)
            {
                var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
                if (profile != null)
                {
                    profile.CvFileUrl = stored.Key;
                    profile.CvUploadedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            }

            return Result<DocumentDtos.DocumentResponseDto>.Success(Map(doc));
    }

    public async Task<Result<List<DocumentDtos.DocumentResponseDto>>> GetUserDocumentsAsync(string userId, CancellationToken ct = default)
    {
        var docs = await db.UserDocuments
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => Map(d))
            .ToListAsync(ct);

        return Result<List<DocumentDtos.DocumentResponseDto>>.Success(docs);
    }

    public async Task<Result<DocumentDtos.DocumentWithUrlDto>> GetDownloadUrlAsync(string userId, Guid documentId, CancellationToken ct = default)
    {
        // Ownership enforced in the query
        var doc = await db.UserDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId, ct);

        if (doc == null) return Result<DocumentDtos.DocumentWithUrlDto>.NotFound("Document not found.");

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

    public async Task<Result> DeleteAsync(string userId, Guid documentId, CancellationToken ct = default)
    {
        var doc = await db.UserDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId, ct);
        if (doc == null) return Result.NotFound("Document not found.");

        // Remove from storage first, then the record
        try { await storage.DeleteAsync(doc.StorageKey, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Storage delete failed for {Key}", doc.StorageKey); }

        db.UserDocuments.Remove(doc);

        // If it was the CV, clear the profile pointer
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
    
    private static async Task<bool> HasValidSignatureAsync(Stream stream, string ext, CancellationToken ct)
    {
        var buffer = new byte[4];
        var read = await stream.ReadAsync(buffer.AsMemory(0, 4), ct);
        if (read < 4) return false;

        return ext switch
        {
            ".pdf"  => buffer.AsSpan(0, 4).SequenceEqual(PdfMagic),
            ".docx" => buffer.AsSpan(0, 4).SequenceEqual(ZipMagic),
            ".doc"  => buffer.AsSpan(0, 4).SequenceEqual(DocMagic),
            _ => false
        };
    }

    private static DocumentDtos.DocumentResponseDto Map(UserDocument d) => new()
    {
        Id = d.Id,
        Type = d.Type.ToString(),
        FileName = d.FileName,
        FileSizeBytes = d.FileSizeBytes,
        UploadedAt = d.UploadedAt
    };
}