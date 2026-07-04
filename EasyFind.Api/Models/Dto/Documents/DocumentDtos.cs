namespace EasyFind.Api.Models.Dto.Documents;

public class DocumentDtos
{
    public class DocumentResponseDto
    {
        public Guid Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public DateTimeOffset UploadedAt { get; set; }
    }

    public class DocumentWithUrlDto : DocumentResponseDto
    {
        public string AccessUrl { get; set; } = string.Empty;
    }
}