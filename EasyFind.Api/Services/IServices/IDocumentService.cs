using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Documents;
using EasyFind.Api.Models.Users;

namespace EasyFind.Api.Services.IServices;

public interface IDocumentService
{
    Task<Result<DocumentDtos.DocumentResponseDto>> UploadAsync(string userId, IFormFile file, DocumentType type, CancellationToken ct = default);
    Task<Result<List<DocumentDtos.DocumentResponseDto>>> GetUserDocumentsAsync(string userId, CancellationToken ct = default);
    Task<Result<DocumentDtos.DocumentWithUrlDto>> GetDownloadUrlAsync(string userId, Guid documentId, CancellationToken ct = default);
    Task<Result> DeleteAsync(string userId, Guid documentId, CancellationToken ct = default);
}
