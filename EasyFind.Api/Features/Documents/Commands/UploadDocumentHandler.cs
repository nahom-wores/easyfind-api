using EasyFind.Api.Data;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Documents;
using EasyFind.Api.Models.Options;
using EasyFind.Api.Models.Users;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyFind.Api.Features.Documents.Commands;

public sealed record UploadDocumentCommand(string UserId, IFormFile File, DocumentType Type);

// Upload a CV or supporting document.
//
// The validation ladder runs cheapest-first and every rung is a 400, so a bad
// file never reaches storage: empty -> too big -> wrong extension -> content
// doesn't match the extension.
public class UploadDocumentHandler(
    ApplicationDbContext db,
    IStorageService storage,
    IOptions<DocumentUploadOptions> opts,
    ILogger<UploadDocumentHandler> logger)
{
    private readonly DocumentUploadOptions _opts = opts.Value;

    // Magic-byte signatures — proof the file IS what its extension claims.
    private static readonly byte[] PdfMagic = "%PDF"u8.ToArray();
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04]; // docx is a zip
    private static readonly byte[] DocMagic = [0xD0, 0xCF, 0x11, 0xE0]; // legacy .doc

    public async Task<Result<DocumentDtos.DocumentResponseDto>> HandleAsync(UploadDocumentCommand command, CancellationToken ct = default)
    {
        var (userId, file, type) = command;
        if (file.Length == 0)
            return Result<DocumentDtos.DocumentResponseDto>.Validation("File is empty.");

        if (file.Length > _opts.MaxFileSizeBytes)
            return Result<DocumentDtos.DocumentResponseDto>.Validation(
                $"File exceeds the {_opts.MaxFileSizeBytes / (1024 * 1024)}MB limit.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!_opts.AllowedExtensions.Contains(ext))
            return Result<DocumentDtos.DocumentResponseDto>.Validation(
                $"File type '{ext}' not allowed. Accepted: {string.Join(", ", _opts.AllowedExtensions)}.");

        // Defends against renamed files (virus.exe -> cv.pdf)
        await using (var checkStream = file.OpenReadStream())
        {
            if (!await HasValidSignatureAsync(checkStream, ext, ct))
                return Result<DocumentDtos.DocumentResponseDto>.Validation(
                    "File content does not match its extension.");
        }

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
            return Result<DocumentDtos.DocumentResponseDto>.Failure(
                "Upload failed. Please try again.", ErrorType.Failure);
        }

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

        // A CV is also pointed at from the profile, so keep that pointer in step.
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

        return Result<DocumentDtos.DocumentResponseDto>.Success(doc.ToResponseDto());
    }

    private static async Task<bool> HasValidSignatureAsync(Stream stream, string ext, CancellationToken ct)
    {
        var buffer = new byte[4];
        var read = await stream.ReadAsync(buffer.AsMemory(0, 4), ct);
        if (read < 4) return false;

        return ext switch
        {
            ".pdf" => buffer.AsSpan(0, 4).SequenceEqual(PdfMagic),
            ".docx" => buffer.AsSpan(0, 4).SequenceEqual(ZipMagic),
            ".doc" => buffer.AsSpan(0, 4).SequenceEqual(DocMagic),
            _ => false
        };
    }
}
