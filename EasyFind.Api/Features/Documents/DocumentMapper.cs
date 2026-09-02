using EasyFind.Api.Models.Dto.Documents;
using EasyFind.Api.Models.Users;

namespace EasyFind.Api.Features.Documents;

public static class DocumentMapper
{
    // Note there is no StorageKey here on purpose — the key is internal, and
    // access goes through a time-limited URL from the download endpoint.
    public static DocumentDtos.DocumentResponseDto ToResponseDto(this UserDocument d) => new()
    {
        Id = d.Id,
        Type = d.Type.ToString(),
        FileName = d.FileName,
        FileSizeBytes = d.FileSizeBytes,
        UploadedAt = d.UploadedAt
    };
}
